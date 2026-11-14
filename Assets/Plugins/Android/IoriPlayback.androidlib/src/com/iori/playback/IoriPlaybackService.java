package com.iori.playback;

import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioManager;
import android.os.Build;
import android.os.Looper;
import android.os.PowerManager;
import android.util.Log;

import androidx.annotation.Nullable;
import androidx.core.content.ContextCompat;
import androidx.media3.common.ForwardingPlayer;
import androidx.media3.common.Player;
import androidx.media3.session.MediaSession;
import androidx.media3.session.MediaSessionService;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.List;

/**
 * IORI のバックグラウンド再生サービス。Unity から渡された予定表(filesDir/iori_plan.json)を鳴らし続ける。
 * - Media3 の MediaSessionService なので、再生中は通知が出てフォアグラウンドで動く(画面を消しても止まらない)
 * - オーディオフォーカスはこのサービスが 1 つだけ持つ(中の ExoPlayer は持たない)
 * - ラジオが ON の間は partial wake lock を持つ(静けさの時間で何も鳴っていなくてもフレーズの時刻を守るため)
 * - 音楽プレーヤー(端末の曲)もここで鳴らす(MusicEngine)。プレーヤーの番の間は、通知とロック画面にプレーヤーの曲を出し、
 *   そこからの再生/一時停止はプレーヤーの曲と環境音の両方に効く(アプリを開かずに全部の音を止められるように)。
 *   アプリの中で曲だけ止めて環境音が鳴っている間は、通知をラジオのものに戻す(「鳴っている」と伝え続けて前面に残るため)
 */
public final class IoriPlaybackService extends MediaSessionService
        implements RadioSessionPlayer.Controls, PlanEngine.Listener, MusicEngine.Host {
    private static final String TAG = "IoriPlayback";

    private PlanEngine engine;
    private RadioSessionPlayer sessionPlayer;
    private MediaSession session;
    private PowerManager.WakeLock wakeLock;
    private AudioManager audioManager;
    private AudioFocusRequest focusRequest;
    private boolean hasFocus;
    private boolean resumeOnFocusGain;
    private MusicEngine music;
    private ForwardingPlayer musicSessionPlayer;
    private boolean musicFloor;
    private boolean resumeMusicOnFocusGain;
    private Player sessionShown;
    private boolean sessionHold;

    private final AudioManager.OnAudioFocusChangeListener focusListener = change -> {
        switch (change) {
            case AudioManager.AUDIOFOCUS_GAIN:
                hasFocus = true;
                engine.setDuck(1f);
                if (music != null) music.setDuck(1f);
                sessionHold = true;
                if (resumeOnFocusGain) {
                    resumeOnFocusGain = false;
                    startRadio();
                }
                if (resumeMusicOnFocusGain) {
                    resumeMusicOnFocusGain = false;
                    if (music != null) music.play();
                }
                sessionHold = false;
                updateSession();
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK:
                engine.setDuck(0.3f);
                if (music != null) music.setDuck(0.3f);
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT:
                resumeOnFocusGain = engine.isRunning();
                resumeMusicOnFocusGain = music != null && music.wantsToPlay();
                pauseAll(false);
                break;
            case AudioManager.AUDIOFOCUS_LOSS:
                resumeOnFocusGain = false;
                resumeMusicOnFocusGain = false;
                pauseAll(true);
                break;
            default:
                break;
        }
    };

    private final BroadcastReceiver noisyReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            // イヤホンが抜けたら止める(スピーカーから急に鳴らさない)。
            if (AudioManager.ACTION_AUDIO_BECOMING_NOISY.equals(intent.getAction())) {
                resumeMusicOnFocusGain = false;
                pauseAll(true);
            }
        }
    };

    @Override
    public void onCreate() {
        super.onCreate();
        engine = new PlanEngine(this, this);
        sessionPlayer = new RadioSessionPlayer(Looper.getMainLooper(), this);
        MediaSession.Builder builder = new MediaSession.Builder(this, sessionPlayer);
        Intent launch = getPackageManager().getLaunchIntentForPackage(getPackageName());
        if (launch != null) {
            builder.setSessionActivity(PendingIntent.getActivity(this, 0, launch,
                    PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT));
        }
        session = builder.build();
        sessionShown = sessionPlayer;
        // startService で起動したときは onGetSession が呼ばれないので、ここで登録する。
        // 登録しないと Media3 が通知を出さず、フォアグラウンドにもならない(画面オフで止められる)。
        addSession(session);
        audioManager = (AudioManager) getSystemService(Context.AUDIO_SERVICE);
        PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "IORI:radio");
        wakeLock.setReferenceCounted(false);
        ContextCompat.registerReceiver(this, noisyReceiver,
                new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY), ContextCompat.RECEIVER_NOT_EXPORTED);
        IoriPlayback.onServiceCreated(this);
        reloadPlan();
    }

    @Nullable
    @Override
    public MediaSession onGetSession(MediaSession.ControllerInfo controllerInfo) {
        return session;
    }

    /** Unity が書いた予定表を読み直して鳴らす。 */
    void reloadPlan() {
        File file = IoriPlayback.planFile(this);
        if (!file.exists()) return;
        try {
            engine.setPlan(Plan.parse(readUtf8(file)));
        } catch (Exception e) {
            Log.e(TAG, "予定表を読めませんでした", e);
            return;
        }
        startRadio();
    }

    PlanEngine engine() {
        return engine;
    }

    void startRadio() {
        if (!engine.hasPlan()) return;
        if (!requestFocus()) {
            Log.w(TAG, "オーディオフォーカスを取れませんでした");
            return;
        }
        wakeLock.acquire();
        engine.resume();
        sessionPlayer.refresh();
        updateSession();
    }

    void pauseRadio(boolean releaseFocus) {
        engine.pause();
        // (音楽プレーヤーが鳴っている間は、起きたまま、フォーカスも持ったまま)
        boolean musicOn = music != null && music.wantsToPlay();
        if (wakeLock.isHeld() && !musicOn) wakeLock.release();
        if (releaseFocus && !musicOn) abandonFocus();
        sessionPlayer.refresh();
        updateSession();
    }

    /** プレーヤーの曲も環境音も止める(通知やロック画面のボタン、イヤホンが抜けた、ほかのアプリが鳴らし始めた)。 */
    private void pauseAll(boolean releaseFocus) {
        sessionHold = true;
        if (music != null) music.pause();
        pauseRadio(releaseFocus);
        sessionHold = false;
        updateSession();
    }

    // ---- 音楽プレーヤー(端末の曲)

    /** 音楽プレーヤーを鳴らす係(最初に使うときに作る)。 */
    MusicEngine music() {
        if (music == null) {
            music = new MusicEngine(this, this);
            music.setVolume(IoriMusic.volume());
            // 通知やロック画面のボタンは、プレーヤーの曲と環境音の両方に効かせる
            musicSessionPlayer = new ForwardingPlayer(music.player()) {
                @Override
                public void play() {
                    setPlayWhenReady(true);
                }

                @Override
                public void pause() {
                    setPlayWhenReady(false);
                }

                @Override
                public void setPlayWhenReady(boolean playWhenReady) {
                    resumeOnFocusGain = false;
                    resumeMusicOnFocusGain = false;
                    if (playWhenReady) musicPlay();
                    else pauseAll(true);
                }

                @Override
                public void stop() {
                    setPlayWhenReady(false);
                }
            };
        }
        return music;
    }

    MusicEngine musicOrNull() {
        return music;
    }

    void musicSetQueue(List<MusicStore.Track> order, int index, long positionMs, int repeat, boolean play) {
        sessionHold = true;
        if (play) {
            // (通知から全部止めたあとでも、曲を鳴らすなら環境音も戻す)
            startRadio();
            if (!requestFocus()) {
                Log.w(TAG, "オーディオフォーカスを取れませんでした");
                play = false;
            }
        }
        music().setQueue(order, index, positionMs, repeat, play);
        sessionHold = false;
        updateSession();
    }

    /** プレーヤーの曲を鳴らす。止まっていた環境音も戻す。 */
    void musicPlay() {
        sessionHold = true;
        startRadio();
        if (requestFocus()) music().play();
        else Log.w(TAG, "オーディオフォーカスを取れませんでした");
        sessionHold = false;
        updateSession();
    }

    /** アプリの中の一時停止: プレーヤーの曲だけ止める(環境音は鳴り続ける)。 */
    void musicPause() {
        resumeMusicOnFocusGain = false;
        if (music != null) music.pause();
    }

    /** プレーヤーの番になった、ラジオの番に戻った。通知とロック画面に出すものを切り替える。 */
    void setMusicFloor(boolean floor) {
        if (floor == musicFloor) return;
        if (floor) music();
        musicFloor = floor;
        updateSession();
    }

    /**
     * 通知とロック画面に出すものを選ぶ。プレーヤーの番でも、曲が止まっていて環境音だけ鳴っている間はラジオのものを出す
     * (ラジオのほうは環境音が鳴っている間「再生中」と伝えるので、サービスが前面に残る)。
     */
    private void updateSession() {
        if (sessionHold || session == null) return;
        boolean useMusic = musicFloor && music != null && (music.wantsToPlay() || !engine.isRunning());
        Player want = useMusic ? musicSessionPlayer : sessionPlayer;
        if (want == sessionShown) return;
        sessionShown = want;
        if (!useMusic) sessionPlayer.refresh();
        session.setPlayer(want);
    }

    @Override
    public void onMusicChanged() {
        // プレーヤーが鳴っている間は、環境音が止まっていても起きたままでいる
        if (music != null && music.wantsToPlay()) {
            if (!wakeLock.isHeld()) wakeLock.acquire();
        } else if (!engine.isRunning() && wakeLock.isHeld()) {
            wakeLock.release();
        }
        updateSession();
    }

    void stopRadio() {
        pauseAll(true);
        engine.release();
        sessionPlayer.refresh();
        stopSelf();
    }

    // RadioSessionPlayer.Controls(通知やロック画面のボタン)
    @Override
    public boolean isPlaying() {
        return engine != null && engine.isRunning();
    }

    @Override
    public boolean hasPlan() {
        return engine != null && engine.hasPlan();
    }

    @Override
    public void onUserPlay() {
        resumeOnFocusGain = false;
        startRadio();
    }

    @Override
    public void onUserPause() {
        resumeOnFocusGain = false;
        pauseRadio(true);
    }

    @Override
    public void onUserStop() {
        stopRadio();
    }

    // PlanEngine.Listener
    @Override
    public void onPhaseChanged(String phase, boolean quiet) {
        sessionPlayer.setSubtitle(quiet ? "静けさの時間" : PhaseLabels.of(phase));
    }

    @Override
    public void onTaskRemoved(@Nullable Intent rootIntent) {
        // アプリを履歴から消してもラジオは鳴らし続ける。止まっているときだけサービスも終える。
        if (!isPlaying() && (music == null || !music.wantsToPlay())) stopSelf();
    }

    @Override
    public void onDestroy() {
        sessionHold = true;
        IoriPlayback.onServiceDestroyed(this);
        unregisterReceiver(noisyReceiver);
        engine.release();
        if (music != null) music.release();
        if (wakeLock.isHeld()) wakeLock.release();
        abandonFocus();
        session.release();
        sessionPlayer.release();
        super.onDestroy();
    }

    private boolean requestFocus() {
        if (hasFocus) return true;
        int result;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (focusRequest == null) {
                focusRequest = new AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                        .setAudioAttributes(new AudioAttributes.Builder()
                                .setUsage(AudioAttributes.USAGE_MEDIA)
                                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                                .build())
                        .setOnAudioFocusChangeListener(focusListener)
                        .setWillPauseWhenDucked(false)
                        .build();
            }
            result = audioManager.requestAudioFocus(focusRequest);
        } else {
            result = requestFocusLegacy();
        }
        hasFocus = result == AudioManager.AUDIOFOCUS_REQUEST_GRANTED;
        return hasFocus;
    }

    @SuppressWarnings("deprecation")
    private int requestFocusLegacy() {
        return audioManager.requestAudioFocus(focusListener, AudioManager.STREAM_MUSIC, AudioManager.AUDIOFOCUS_GAIN);
    }

    @SuppressWarnings("deprecation")
    private void abandonFocus() {
        if (!hasFocus) return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && focusRequest != null) {
            audioManager.abandonAudioFocusRequest(focusRequest);
        } else {
            audioManager.abandonAudioFocus(focusListener);
        }
        hasFocus = false;
    }

    private static String readUtf8(File file) throws IOException {
        try (FileInputStream in = new FileInputStream(file)) {
            byte[] buf = new byte[(int) file.length()];
            int read = 0;
            while (read < buf.length) {
                int n = in.read(buf, read, buf.length - read);
                if (n < 0) break;
                read += n;
            }
            return new String(buf, 0, read, StandardCharsets.UTF_8);
        }
    }
}
