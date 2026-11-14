package com.iori.playback;

import android.content.Context;
import android.content.Intent;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

/**
 * Unity(AndroidPlaybackBackend.cs)から呼ぶ入り口。Unity のスレッドから呼ばれるので、処理はメインスレッドに回す。
 * 予定表は Intent に載せず filesDir/iori_plan.json に書く(サイズ制限を避け、サービスが再起動しても読み直せる)。
 */
public final class IoriPlayback {
    private static final String TAG = "IoriPlayback";
    private static final String PLAN_FILE = "iori_plan.json";
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static volatile IoriPlaybackService service;

    private IoriPlayback() {
    }

    static File planFile(Context context) {
        return new File(context.getFilesDir(), PLAN_FILE);
    }

    static void onServiceCreated(IoriPlaybackService s) {
        service = s;
        IoriMusic.onServiceCreated();
    }

    static IoriPlaybackService service() {
        return service;
    }

    static void onServiceDestroyed(IoriPlaybackService s) {
        if (service == s) service = null;
    }

    /** 予定表を渡して鳴らす。アプリが前面にいる間に呼ぶこと(裏からはサービスを起動できない)。 */
    public static void applyPlan(Context context, String planJson) {
        final Context app = context.getApplicationContext();
        try {
            File tmp = new File(app.getFilesDir(), PLAN_FILE + ".tmp");
            try (FileOutputStream out = new FileOutputStream(tmp)) {
                out.write(planJson.getBytes(StandardCharsets.UTF_8));
            }
            if (!tmp.renameTo(planFile(app))) throw new IOException("rename failed");
        } catch (IOException e) {
            Log.e(TAG, "予定表を書けませんでした", e);
            return;
        }
        main.post(() -> {
            if (service != null) {
                service.reloadPlan();
            } else {
                // 前面にいる間の起動なので startService でよい。再生が始まると Media3 がフォアグラウンドにする。
                app.startService(new Intent(app, IoriPlaybackService.class));
            }
        });
    }

    public static void setMusicVolume(final float volume) {
        main.post(() -> {
            if (service != null) service.engine().setMusicVolume(volume);
        });
    }

    public static void setAmbientVolume(final String id, final float volume) {
        main.post(() -> {
            if (service != null) service.engine().setAmbientVolume(id, volume);
        });
    }

    public static void stop(Context context) {
        main.post(() -> {
            if (service != null) service.stopRadio();
        });
    }

    /** 動作確認パネル用の状態。 */
    public static String status() {
        IoriPlaybackService s = service;
        return s == null ? "サービス停止中" : s.engine().status();
    }
}
