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
import androidx.media3.session.MediaSession;
import androidx.media3.session.MediaSessionService;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

/**
 * IORI のバックグラウンド再生サービス。Unity から渡された予定表(filesDir/iori_plan.json)を鳴らし続ける。
 * - Media3 の MediaSessionService なので、再生中は通知が出てフォアグラウンドで動く(画面を消しても止まらない)
 * - オーディオフォーカスはこのサービスが 1 つだけ持つ(中の ExoPlayer は持たない)
 * - ラジオが ON の間は partial wake lock を持つ(静けさの時間で何も鳴っていなくてもフレーズの時刻を守るため)
 */
public final class IoriPlaybackService extends MediaSessionService
        implements RadioSessionPlayer.Controls, PlanEngine.Listener {
    private static final String TAG = "IoriPlayback";

    private PlanEngine engine;
    private RadioSessionPlayer sessionPlayer;
    private MediaSession session;
    private PowerManager.WakeLock wakeLock;
    private AudioManager audioManager;
    private AudioFocusRequest focusRequest;
    private boolean hasFocus;
    private boolean resumeOnFocusGain;

    private final AudioManager.OnAudioFocusChangeListener focusListener = change -> {
        switch (change) {
            case AudioManager.AUDIOFOCUS_GAIN:
                hasFocus = true;
                engine.setDuck(1f);
                if (resumeOnFocusGain) {
                    resumeOnFocusGain = false;
                    startRadio();
                }
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK:
                engine.setDuck(0.3f);
                break;
            case AudioManager.AUDIOFOCUS_LOSS_TRANSIENT:
                resumeOnFocusGain = engine.isRunning();
                pauseRadio(false);
                break;
            case AudioManager.AUDIOFOCUS_LOSS:
                resumeOnFocusGain = false;
                pauseRadio(true);
                break;
            default:
                break;
        }
    };

    private final BroadcastReceiver noisyReceiver = new BroadcastReceiver() {
        @Override
        public void onReceive(Context context, Intent intent) {
            // イヤホンが抜けたら止める(スピーカーから急に鳴らさない)。
            if (AudioManager.ACTION_AUDIO_BECOMING_NOISY.equals(intent.getAction())) pauseRadio(true);
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
    }

    void pauseRadio(boolean releaseFocus) {
        engine.pause();
        if (wakeLock.isHeld()) wakeLock.release();
        if (releaseFocus) abandonFocus();
        sessionPlayer.refresh();
    }

    void stopRadio() {
        pauseRadio(true);
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
        if (!isPlaying()) stopSelf();
    }

    @Override
    public void onDestroy() {
        IoriPlayback.onServiceDestroyed(this);
        unregisterReceiver(noisyReceiver);
        engine.release();
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
