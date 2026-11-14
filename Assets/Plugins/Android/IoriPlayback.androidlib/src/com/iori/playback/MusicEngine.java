package com.iori.playback;

import android.content.Context;
import android.net.Uri;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.media3.common.AudioAttributes;
import androidx.media3.common.C;
import androidx.media3.common.MediaItem;
import androidx.media3.common.MediaMetadata;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.Player;
import androidx.media3.exoplayer.ExoPlayer;

import java.util.ArrayList;
import java.util.List;

/**
 * 音楽プレーヤー(端末の曲)を鳴らす係。Unity の PlayQueue が決めた順の列を受け取り、その順に鳴らす。
 * PC の UnityMusicBackend.cs と同じ決まりで進む: 曲が終わったら、くり返しなしは次へ進んで最後で止まる、
 * 全部は最後の次が最初、1 曲は同じ曲をもう一度。読めない曲は飛ばす(全部読めなければ止まる)。
 * メインスレッドだけで使う。Unity のスレッドには、いまの状態の写し(state)を渡す。
 */
final class MusicEngine implements Player.Listener {
    private static final String TAG = "IoriMusic";

    interface Host {
        /** 鳴っているかどうか、曲、列のどれかが変わった。 */
        void onMusicChanged();
    }

    private final ExoPlayer player;
    private final Host host;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private float volume = 1f, duck = 1f;
    private int failures;
    private boolean finished;

    // Unity のスレッドが読む写し
    private volatile int snapIndex = -1;
    private volatile long snapPosition, snapAt, snapDuration;
    private volatile boolean snapWant, snapAdvancing, snapFinished;

    private final Runnable tick = new Runnable() {
        @Override
        public void run() {
            snapshot();
            if (player.isPlaying()) handler.postDelayed(this, 500);
        }
    };

    MusicEngine(Context context, Host host) {
        this.host = host;
        AudioAttributes attributes = new AudioAttributes.Builder()
                .setUsage(C.USAGE_MEDIA)
                .setContentType(C.AUDIO_CONTENT_TYPE_MUSIC)
                .build();
        player = new ExoPlayer.Builder(context)
                .setAudioAttributes(attributes, /* handleAudioFocus= */ false) // フォーカスはサービスが 1 つだけ持つ
                .setWakeMode(C.WAKE_MODE_LOCAL)
                .build();
        player.addListener(this);
    }

    ExoPlayer player() {
        return player;
    }

    private static MediaItem item(MusicStore.Track t) {
        MediaMetadata.Builder meta = new MediaMetadata.Builder()
                .setTitle(t.title)
                .setArtist(t.artist)
                .setAlbumTitle(t.album);
        if (t.albumId > 0) meta.setArtworkUri(MusicStore.albumArtUri(t.albumId));
        return new MediaItem.Builder()
                .setMediaId(t.uri)
                .setUri(Uri.parse(t.uri))
                .setMediaMetadata(meta.build())
                .build();
    }

    private static int repeatMode(int repeat) {
        return repeat == 1 ? Player.REPEAT_MODE_ALL : repeat == 2 ? Player.REPEAT_MODE_ONE : Player.REPEAT_MODE_OFF;
    }

    /**
     * 列を受け取る。order[index] がいま用意している曲と同じなら、その曲は途切れずに続く(positionMs は使わない)。
     * repeat は 0 なし、1 全部、2 1 曲。
     */
    void setQueue(List<MusicStore.Track> order, int index, long positionMs, int repeat, boolean play) {
        failures = 0;
        finished = false;
        if (order.isEmpty()) {
            player.setPlayWhenReady(false);
            player.clearMediaItems();
            player.stop();
            snapshot();
            return;
        }
        index = Math.max(0, Math.min(index, order.size() - 1));
        List<MediaItem> items = new ArrayList<>(order.size());
        for (MusicStore.Track t : order) items.add(item(t));

        MediaItem current = player.getCurrentMediaItem();
        int count = player.getMediaItemCount();
        int state = player.getPlaybackState();
        boolean same = current != null && count > 0 && current.mediaId.equals(items.get(index).mediaId)
                && state != Player.STATE_IDLE && state != Player.STATE_ENDED;
        if (state != Player.STATE_IDLE && sameList(items)) {
            // 列は同じ: 曲を移るだけ(同じ曲ならそのまま続く)
            if (!same) player.seekTo(index, Math.max(0, positionMs));
        } else if (same) {
            // いまの曲だけ残して、前と後ろを入れ替える
            int at = player.getCurrentMediaItemIndex();
            if (at + 1 < count) player.removeMediaItems(at + 1, count);
            if (at > 0) player.removeMediaItems(0, at);
            if (index > 0) player.addMediaItems(0, items.subList(0, index));
            if (index + 1 < items.size()) player.addMediaItems(items.subList(index + 1, items.size()));
        } else {
            player.setMediaItems(items, index, Math.max(0, positionMs));
            player.prepare();
        }
        player.setRepeatMode(repeatMode(repeat));
        player.setPlayWhenReady(play);
        snapshot();
    }

    private boolean sameList(List<MediaItem> items) {
        int count = player.getMediaItemCount();
        if (count != items.size()) return false;
        for (int i = 0; i < count; i++)
            if (!player.getMediaItemAt(i).mediaId.equals(items.get(i).mediaId)) return false;
        return true;
    }

    void setRepeat(int repeat) {
        player.setRepeatMode(repeatMode(repeat));
    }

    void play() {
        if (player.getMediaItemCount() == 0) return;
        if (finished || player.getPlaybackState() == Player.STATE_ENDED) {
            // 最後まで鳴らし終えていたら、いまの曲の頭から
            finished = false;
            player.seekTo(player.getCurrentMediaItemIndex(), 0);
        }
        if (player.getPlaybackState() == Player.STATE_IDLE) player.prepare();
        player.setPlayWhenReady(true);
        snapshot();
    }

    void pause() {
        player.setPlayWhenReady(false);
        snapshot();
    }

    void seek(long positionMs) {
        if (player.getMediaItemCount() == 0) return;
        player.seekTo(Math.max(0, positionMs));
        snapshot();
    }

    void setVolume(float v) {
        volume = Math.max(0f, Math.min(1f, v));
        player.setVolume(volume * duck);
    }

    void setDuck(float factor) {
        duck = factor;
        player.setVolume(volume * duck);
    }

    /** 鳴らすつもりでいるか(読み込み中も true)。 */
    boolean wantsToPlay() {
        int state = player.getPlaybackState();
        return player.getPlayWhenReady() && state != Player.STATE_IDLE && state != Player.STATE_ENDED && player.getMediaItemCount() > 0;
    }

    void release() {
        handler.removeCallbacks(tick);
        player.removeListener(this);
        player.release();
        snapIndex = -1;
        snapWant = false;
        snapAdvancing = false;
    }

    private void snapshot() {
        snapIndex = player.getMediaItemCount() == 0 ? -1 : player.getCurrentMediaItemIndex();
        long duration = player.getDuration();
        snapDuration = duration == C.TIME_UNSET ? 0 : duration;
        snapPosition = player.getCurrentPosition();
        snapAt = SystemClock.elapsedRealtime();
        snapAdvancing = player.isPlaying();
        snapWant = wantsToPlay();
        snapFinished = finished;
    }

    /**
     * Unity のスレッドから読む、いまの状態: { 列の中の位置, 曲の中の位置(ミリ秒), 曲の長さ(ミリ秒。分からなければ 0),
     * 鳴らすつもりなら 1, 列を鳴らし終えて止まっていたら 1 }
     */
    long[] state() {
        long position = snapPosition;
        if (snapAdvancing) position += SystemClock.elapsedRealtime() - snapAt;
        long duration = snapDuration;
        if (duration > 0 && position > duration) position = duration;
        return new long[]{snapIndex, position, duration, snapWant ? 1 : 0, snapFinished ? 1 : 0};
    }

    // Player.Listener

    @Override
    public void onEvents(@NonNull Player p, @NonNull Player.Events events) {
        if (events.contains(Player.EVENT_PLAYBACK_STATE_CHANGED) && player.getPlaybackState() == Player.STATE_ENDED) {
            // くり返しなしで最後の曲が終わった
            finished = true;
            player.setPlayWhenReady(false);
        }
        if (events.contains(Player.EVENT_PLAYBACK_STATE_CHANGED) && player.getPlaybackState() == Player.STATE_READY) failures = 0;
        snapshot();
        handler.removeCallbacks(tick);
        if (player.isPlaying()) handler.postDelayed(tick, 500);
        if (events.containsAny(Player.EVENT_IS_PLAYING_CHANGED, Player.EVENT_PLAY_WHEN_READY_CHANGED,
                Player.EVENT_PLAYBACK_STATE_CHANGED, Player.EVENT_MEDIA_ITEM_TRANSITION, Player.EVENT_TIMELINE_CHANGED)) {
            host.onMusicChanged();
        }
    }

    @Override
    public void onPlayerError(@NonNull PlaybackException error) {
        Log.w(TAG, "曲を読めませんでした: " + (player.getCurrentMediaItem() != null ? player.getCurrentMediaItem().mediaId : ""), error);
        int count = player.getMediaItemCount();
        // 読めない曲は飛ばす(全部読めなければ止まる)
        if (++failures >= count) {
            finished = true;
            player.setPlayWhenReady(false);
            snapshot();
            return;
        }
        if (player.hasNextMediaItem()) {
            player.seekToNextMediaItem();
        } else if (player.getRepeatMode() != Player.REPEAT_MODE_OFF) {
            player.seekTo(0, 0);
        } else {
            finished = true;
            player.setPlayWhenReady(false);
            snapshot();
            return;
        }
        player.prepare();
    }
}
