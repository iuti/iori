package com.iori.playback;

import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Unity(AndroidMusicBackend.cs)から呼ぶ、音楽プレーヤー(端末の曲)の入り口。
 * Unity のスレッドから呼ばれるので、鳴らす操作はメインスレッドに回し、曲を集めるのとジャケットを用意するのは別のスレッドでする。
 * 実際に鳴らすのは再生サービスの中の MusicEngine(アプリが裏に回っても鳴り続ける)。
 */
public final class IoriMusic {
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static final ExecutorService worker = Executors.newSingleThreadExecutor();
    private static final long[] NO_STATE = {-1, 0, 0, 0, 0};

    private static volatile boolean scanning;
    private static volatile String scanResult;
    /** サービスがまだ無い間にたのまれた列(サービスができたら渡す)。 */
    private static Runnable pending;
    /** プレーヤーの番か(サービスができたときに伝え直す)。 */
    private static boolean wantFloor;
    /** プレーヤーの音量(鳴らす係ができたときに伝える)。 */
    private static volatile float volume = 1f;

    private IoriMusic() {
    }

    // ---- 曲を集める

    /**
     * 端末の曲を集め始める。終わったら takeScanResult が JSON を返す。
     * folder が空でなければ、その名前のフォルダの下の曲だけ集める。
     */
    public static void scan(Context context, final String folder) {
        if (scanning) return;
        final Context app = context.getApplicationContext();
        scanning = true;
        worker.execute(() -> {
            String result;
            try {
                result = MusicStore.scanJson(app, folder);
            } catch (RuntimeException e) {
                Log.w("IoriMusic", "曲を集められませんでした", e);
                result = "[]";
            }
            scanResult = result;
            scanning = false;
        });
    }

    /** 集め終えていたら曲の一覧の JSON(1 度だけ)。まだなら null。 */
    public static String takeScanResult() {
        if (scanning) return null;
        String r = scanResult;
        scanResult = null;
        return r;
    }

    // ---- ジャケット

    /** ジャケットの絵を書いておく場所。絵は {albumId}_{size}.jpg、絵が無いと分かったら同じ名前に .none。 */
    public static String artFolder(Context context) {
        return MusicStore.artFolder(context.getApplicationContext()).getAbsolutePath();
    }

    public static void requestArt(Context context, final long albumId, final String trackUri, final int size) {
        final Context app = context.getApplicationContext();
        worker.execute(() -> MusicStore.writeArt(app, albumId, trackUri, size));
    }

    // ---- 鳴らす

    /** サービスがあればすぐ、なければ起こしてから実行する(アプリが前面にいる間に呼ぶこと)。 */
    private static void withService(Context context, final Runnable action, final boolean startIfNeeded) {
        final Context app = context.getApplicationContext();
        main.post(() -> {
            if (IoriPlayback.service() != null) {
                action.run();
            } else if (startIfNeeded) {
                pending = action;
                try {
                    app.startService(new Intent(app, IoriPlaybackService.class));
                } catch (IllegalStateException e) {
                    // (アプリが裏にいる間はサービスを起こせない)
                    pending = null;
                    Log.w("IoriMusic", "再生サービスを起こせませんでした", e);
                }
            }
        });
    }

    static void onServiceCreated() {
        Runnable action = pending;
        pending = null;
        if (action != null) action.run();
        if (wantFloor && IoriPlayback.service() != null) IoriPlayback.service().setMusicFloor(true);
    }

    /** 列を渡す。uris は曲の URI を改行で区切ったもの(鳴らす順)。repeat は 0 なし、1 全部、2 1 曲。 */
    public static void setQueue(Context context, String uris, final int index, final long positionMs, final int repeat, final boolean play) {
        final List<MusicStore.Track> order = new ArrayList<>();
        if (uris != null && !uris.isEmpty())
            for (String uri : uris.split("\n"))
                if (!uri.isEmpty()) order.add(MusicStore.find(uri));
        withService(context, () -> IoriPlayback.service().musicSetQueue(order, index, positionMs, repeat, play), !order.isEmpty());
    }

    public static void setRepeat(final int repeat) {
        main.post(() -> {
            MusicEngine m = engine();
            if (m != null) m.setRepeat(repeat);
        });
    }

    public static void play(Context context) {
        withService(context, () -> IoriPlayback.service().musicPlay(), false);
    }

    public static void pause() {
        main.post(() -> {
            if (IoriPlayback.service() != null) IoriPlayback.service().musicPause();
        });
    }

    public static void seek(final long positionMs) {
        main.post(() -> {
            MusicEngine m = engine();
            if (m != null) m.seek(positionMs);
        });
    }

    public static void setVolume(final float v) {
        volume = v;
        main.post(() -> {
            MusicEngine m = engine();
            if (m != null) m.setVolume(v);
        });
    }

    static float volume() {
        return volume;
    }

    /** 鳴らす係(サービスが無い、またはまだ使っていなければ null)。 */
    private static MusicEngine engine() {
        IoriPlaybackService s = IoriPlayback.service();
        return s == null ? null : s.musicOrNull();
    }

    /** プレーヤーの番かどうか(番の間、通知とロック画面にはプレーヤーの曲が出る)。 */
    public static void setActive(final boolean active) {
        main.post(() -> {
            wantFloor = active;
            if (IoriPlayback.service() != null) IoriPlayback.service().setMusicFloor(active);
        });
    }

    /** いまの状態: { 列の中の位置, 曲の中の位置(ミリ秒), 曲の長さ(ミリ秒), 鳴らすつもりなら 1, 鳴らし終えて止まっていたら 1 } */
    public static long[] state() {
        MusicEngine m = engine();
        return m == null ? NO_STATE : m.state();
    }
}
