package com.iori.playback;

import android.os.Looper;

import androidx.annotation.NonNull;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import androidx.media3.common.Player;
import androidx.media3.common.SimpleBasePlayer;

import com.google.common.collect.ImmutableList;
import com.google.common.util.concurrent.Futures;
import com.google.common.util.concurrent.ListenableFuture;

/**
 * MediaSession に見せるための「見かけ上のプレイヤー」。実際の音は PlanEngine が複数の ExoPlayer で鳴らす。
 * これが「再生中」と報告している間、Media3 が通知を出してサービスを前面に保つ(静けさの時間で曲が無くても)。
 * 通知・ロック画面・イヤホンのボタンからの再生/一時停止をサービスに伝える。
 */
final class RadioSessionPlayer extends SimpleBasePlayer {
    interface Controls {
        boolean isPlaying();

        boolean hasPlan();

        void onUserPlay();

        void onUserPause();

        void onUserStop();
    }

    private final Controls controls;
    private String subtitle = "";

    RadioSessionPlayer(Looper looper, Controls controls) {
        super(looper);
        this.controls = controls;
    }

    void setSubtitle(String subtitle) {
        this.subtitle = subtitle;
        invalidateState();
    }

    void refresh() {
        invalidateState();
    }

    @NonNull
    @Override
    protected State getState() {
        MediaMetadata metadata = new MediaMetadata.Builder()
                .setTitle("IORI ラジオ")
                .setArtist(subtitle)
                .build();
        MediaItem item = new MediaItem.Builder()
                .setMediaId("iori-radio")
                .setMediaMetadata(metadata)
                .build();
        MediaItemData data = new MediaItemData.Builder("iori-radio")
                .setMediaItem(item)
                .setMediaMetadata(metadata)
                .setIsSeekable(false)
                .build();
        Commands commands = new Commands.Builder()
                .addAll(Player.COMMAND_PLAY_PAUSE,
                        Player.COMMAND_STOP,
                        Player.COMMAND_GET_CURRENT_MEDIA_ITEM,
                        Player.COMMAND_GET_METADATA,
                        Player.COMMAND_GET_TIMELINE)
                .build();
        return new State.Builder()
                .setAvailableCommands(commands)
                .setPlaylist(ImmutableList.of(data))
                .setPlayWhenReady(controls.isPlaying(), Player.PLAY_WHEN_READY_CHANGE_REASON_USER_REQUEST)
                .setPlaybackState(controls.hasPlan() ? Player.STATE_READY : Player.STATE_IDLE)
                .build();
    }

    @NonNull
    @Override
    protected ListenableFuture<?> handleSetPlayWhenReady(boolean playWhenReady) {
        if (playWhenReady) controls.onUserPlay();
        else controls.onUserPause();
        return Futures.immediateVoidFuture();
    }

    @NonNull
    @Override
    protected ListenableFuture<?> handleStop() {
        controls.onUserStop();
        return Futures.immediateVoidFuture();
    }
}
