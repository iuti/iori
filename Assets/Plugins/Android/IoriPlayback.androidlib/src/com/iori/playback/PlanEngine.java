package com.iori.playback;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;

import androidx.media3.common.AudioAttributes;
import androidx.media3.common.C;
import androidx.media3.common.MediaItem;
import androidx.media3.common.Player;
import androidx.media3.exoplayer.ExoPlayer;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

/**
 * 予定表を鳴らすだけの係。何を鳴らすかは決めない(決めるのは Unity の RadioScheduler)。
 * 決まりは PC 用の UnityAudioBackend.cs と同じにしてある。
 * - 音量 = フェード × クリップ音量 × 曲の音量 × 途中参加のフェード × ダック
 * - 再生位置 = t - start + offset(loop ならファイル長で割った余り)
 * - 予定表の差し替えで消えたクリップは 1 秒でフェードアウト。同じ id のクリップは鳴らし続ける
 * 時刻は壁時計(System.currentTimeMillis)。サービスが partial wake lock を持つので CPU は眠らない。
 */
final class PlanEngine {
    interface Listener {
        void onPhaseChanged(String phase, boolean quiet);
    }

    private static final String TAG = "IoriPlayback";
    private static final int JOIN_FADE_MS = 1500;
    private static final int REMOVED_FADE_MS = 1000;
    private static final long ONE_SHOT_LATE_LIMIT_MS = 2000;
    private static final long FAST_TICK_MS = 40;
    private static final long MAX_IDLE_TICK_MS = 30_000;
    private static final float AMBIENT_RAMP_PER_SEC = 1.5f;

    private static final class Voice {
        Plan.Clip clip;
        ExoPlayer player;
        long joinedAt = -1;
        long removedAt = -1;
        float removedFromGain;
        float lastGain;
    }

    private static final class AmbientVoice {
        ExoPlayer player;
        float current;
        float target;
    }

    private final Context context;
    private final Listener listener;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Runnable tick = this::tick;
    private final AudioAttributes attributes = new AudioAttributes.Builder()
            .setUsage(C.USAGE_MEDIA)
            .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC)
            .build();

    private final Map<String, Voice> voices = new HashMap<>();
    private final Map<String, AmbientVoice> ambient = new HashMap<>();
    private final List<ExoPlayer> oneShotPlayers = new ArrayList<>();

    private Plan plan;
    private boolean running;
    private float musicVolume = 1f;
    private float duck = 1f;
    private long lastOneShotT = Long.MIN_VALUE;
    private long lastTickUptime;
    private String lastPhaseKey = "";
    private volatile String status = "停止中";

    PlanEngine(Context context, Listener listener) {
        this.context = context.getApplicationContext();
        this.listener = listener;
    }

    boolean isRunning() {
        return running;
    }

    boolean hasPlan() {
        return plan != null;
    }

    String status() {
        return status;
    }

    void setPlan(Plan newPlan) {
        long now = newPlan.toPlanTime(System.currentTimeMillis() + newPlan.clockOffsetMs);
        plan = newPlan;
        musicVolume = newPlan.musicVolume;
        if (lastOneShotT == Long.MIN_VALUE || lastOneShotT > now) lastOneShotT = now;

        for (Voice v : voices.values()) {
            Plan.Clip same = newPlan.findClip(v.clip.id);
            if (same != null) {
                v.clip = same;
                v.removedAt = -1;
            } else if (v.removedAt < 0) {
                v.removedAt = now;
                v.removedFromGain = v.lastGain;
            }
        }

        for (Plan.Ambient layer : newPlan.ambient) {
            AmbientVoice av = ambient.get(layer.id);
            if (av == null) {
                av = new AmbientVoice();
                av.player = newPlayer(layer.file, true);
                av.player.setPlayWhenReady(running);
                ambient.put(layer.id, av);
            }
            av.target = layer.volume;
        }
        scheduleNow();
    }

    void setMusicVolume(float volume) {
        musicVolume = clamp01(volume);
        scheduleNow();
    }

    void setAmbientVolume(String id, float volume) {
        AmbientVoice av = ambient.get(id);
        if (av != null) av.target = clamp01(volume);
        scheduleNow();
    }

    void setDuck(float factor) {
        duck = clamp01(factor);
        scheduleNow();
    }

    void resume() {
        if (running) return;
        running = true;
        for (AmbientVoice av : ambient.values()) av.player.setPlayWhenReady(true);
        lastTickUptime = SystemClock.uptimeMillis();
        scheduleNow();
    }

    /** 曲は捨て(再開時に正しい位置から鳴らし直す)、環境音は一時停止する。 */
    void pause() {
        running = false;
        handler.removeCallbacks(tick);
        for (Voice v : voices.values()) v.player.release();
        voices.clear();
        for (ExoPlayer p : oneShotPlayers) p.release();
        oneShotPlayers.clear();
        for (AmbientVoice av : ambient.values()) av.player.setPlayWhenReady(false);
        status = "一時停止中";
    }

    void release() {
        pause();
        for (AmbientVoice av : ambient.values()) av.player.release();
        ambient.clear();
        plan = null;
        status = "停止中";
    }

    private void scheduleNow() {
        handler.removeCallbacks(tick);
        if (running) handler.post(tick);
    }

    private void tick() {
        if (!running || plan == null) return;
        long t = plan.toPlanTime(System.currentTimeMillis() + plan.clockOffsetMs);
        long uptime = SystemClock.uptimeMillis();
        float dt = Math.min(1f, (uptime - lastTickUptime) / 1000f);
        lastTickUptime = uptime;
        boolean fading = false;

        // 曲: 鳴っているべきクリップを用意する。
        for (Plan.Clip clip : plan.music) {
            if (clip.start > t) break;
            if (!clip.isActiveAt(t) || voices.containsKey(clip.id)) continue;
            long duration = plan.durationOf(clip.file);
            long pos = clip.positionAt(t, clip.loop ? duration : 0);
            if (duration > 0 && pos >= duration) continue; // もう曲が終わっている
            Voice v = new Voice();
            v.clip = clip;
            v.player = newPlayer(clip.file, clip.loop);
            if (clip.loop && duration <= 0) pos = 0; // 長さが分からないループは頭から
            v.player.seekTo(pos);
            v.player.setPlayWhenReady(true);
            if (t - clip.start > clip.fadeIn) v.joinedAt = t;
            voices.put(clip.id, v);
        }

        // 曲: 音量とお片付け。
        for (Iterator<Map.Entry<String, Voice>> it = voices.entrySet().iterator(); it.hasNext(); ) {
            Voice v = it.next().getValue();
            float gain;
            boolean done = false;
            if (v.removedAt >= 0) {
                float k = 1f - (t - v.removedAt) / (float) REMOVED_FADE_MS;
                gain = v.removedFromGain * clamp01(k);
                done = k <= 0f;
                fading = true;
            } else if (t >= v.clip.end) {
                gain = 0f;
                done = true;
            } else {
                gain = v.clip.fadeGainAt(t) * v.clip.volume * musicVolume * duck;
                if (t - v.clip.start < v.clip.fadeIn || v.clip.end - t < v.clip.fadeOut) fading = true;
                if (v.joinedAt >= 0) {
                    float k = clamp01((t - v.joinedAt) / (float) JOIN_FADE_MS);
                    gain *= (float) Math.sin(k * Math.PI / 2);
                    if (k < 1f) fading = true;
                }
            }
            if (done) {
                v.player.release();
                it.remove();
                continue;
            }
            v.lastGain = gain;
            v.player.setVolume(gain);
        }

        // 単発音
        if (t < lastOneShotT - 1000) lastOneShotT = t; // 時計が戻った
        for (Plan.OneShot s : plan.oneShots) {
            if (s.at <= lastOneShotT) continue;
            if (s.at > t) break;
            if (t - s.at <= ONE_SHOT_LATE_LIMIT_MS) playOneShot(s);
        }
        lastOneShotT = t;
        for (Iterator<ExoPlayer> it = oneShotPlayers.iterator(); it.hasNext(); ) {
            ExoPlayer p = it.next();
            if (p.getPlaybackState() == Player.STATE_ENDED) {
                p.release();
                it.remove();
            }
        }

        // 環境音
        for (AmbientVoice av : ambient.values()) {
            if (av.current != av.target) {
                float step = AMBIENT_RAMP_PER_SEC * dt;
                av.current = av.target > av.current ? Math.min(av.target, av.current + step) : Math.max(av.target, av.current - step);
                fading = true;
            }
            av.player.setVolume(av.current * duck);
        }

        // 時間帯の変化を通知(ロック画面の表示用)
        Plan.Span span = plan.spanAt(t);
        String key = span == null ? "" : span.phase + (span.quiet ? "/quiet" : "");
        if (!key.equals(lastPhaseKey)) {
            lastPhaseKey = key;
            if (span != null) listener.onPhaseChanged(span.phase, span.quiet);
        }

        updateStatus(t, span);
        handler.postDelayed(tick, fading ? FAST_TICK_MS : nextEventDelay(t));
    }

    /** 次に何かが変わる(フェードが始まる、単発音が鳴る)までの時間。 */
    private long nextEventDelay(long t) {
        long next = MAX_IDLE_TICK_MS;
        for (Plan.Clip c : plan.music) {
            if (c.start - t > next) break;
            if (c.start > t) next = Math.min(next, c.start - t);
            long fadeOutStart = c.end - c.fadeOut;
            if (fadeOutStart > t) next = Math.min(next, fadeOutStart - t);
            if (c.end > t) next = Math.min(next, c.end - t);
        }
        for (Plan.OneShot s : plan.oneShots) {
            if (s.at > t) {
                next = Math.min(next, s.at - t);
                break;
            }
        }
        return Math.max(FAST_TICK_MS, next);
    }

    private void playOneShot(Plan.OneShot s) {
        ExoPlayer p = newPlayer(s.file, false);
        p.setVolume(clamp01(s.volume * plan.oneShotVolume * duck));
        p.setPlayWhenReady(true);
        oneShotPlayers.add(p);
    }

    private ExoPlayer newPlayer(String file, boolean loop) {
        // 再生の向き先は Unity の StreamingAssets(APK の assets/ に入る)。
        ExoPlayer p = new ExoPlayer.Builder(context)
                .setAudioAttributes(attributes, /* handleAudioFocus= */ false) // フォーカスはサービスが 1 つだけ持つ
                .setWakeMode(C.WAKE_MODE_LOCAL)
                .setLooper(Looper.getMainLooper())
                .build();
        p.setMediaItem(MediaItem.fromUri("asset:///IORI/Audio/" + file));
        p.setRepeatMode(loop ? Player.REPEAT_MODE_ONE : Player.REPEAT_MODE_OFF);
        p.setVolume(0f);
        p.addListener(new Player.Listener() {
            @Override
            public void onPlayerError(androidx.media3.common.PlaybackException error) {
                Log.w(TAG, "再生できませんでした: " + file, error);
            }
        });
        p.prepare();
        return p;
    }

    private void updateStatus(long t, Plan.Span span) {
        StringBuilder sb = new StringBuilder();
        sb.append(span == null ? "-" : span.phase).append(span != null && span.quiet ? " (静けさ)" : "").append('\n');
        for (Voice v : voices.values()) {
            sb.append("♪ ").append(v.clip.file)
                    .append(String.format(java.util.Locale.ROOT, "  vol %.2f  %.1fs", v.lastGain, v.player.getCurrentPosition() / 1000f))
                    .append(v.removedAt >= 0 ? " (消去中)" : "").append('\n');
        }
        for (Map.Entry<String, AmbientVoice> e : ambient.entrySet()) {
            sb.append("~ ").append(e.getKey()).append(String.format(java.util.Locale.ROOT, "  vol %.2f", e.getValue().current)).append('\n');
        }
        status = sb.toString();
    }

    private static float clamp01(float v) {
        return v < 0f ? 0f : Math.min(1f, v);
    }
}
