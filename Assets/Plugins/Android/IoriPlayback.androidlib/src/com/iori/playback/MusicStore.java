package com.iori.playback;

import android.content.ContentResolver;
import android.content.ContentUris;
import android.content.Context;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.net.Uri;
import android.os.Build;
import android.provider.MediaStore;
import android.util.Log;
import android.util.Size;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.Locale;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 端末に入っている曲を MediaStore から集める係と、アルバムのジャケットの絵を用意する係。
 * どちらも時間がかかるので、IoriMusic が別のスレッドで呼ぶ。
 */
final class MusicStore {
    private static final String TAG = "IoriMusic";

    /** 曲 1 つ。uri が曲を区別する名前でもある(Unity 側の MusicTrack.Id)。 */
    static final class Track {
        String uri = "";
        String title = "";
        String artist = "";
        String album = "";
        long albumId;
    }

    /** 最後に集めた曲(uri から引く)。通知に出す曲名などに使う。 */
    private static final ConcurrentHashMap<String, Track> byUri = new ConcurrentHashMap<>();

    private MusicStore() {
    }

    static Track find(String uri) {
        Track t = byUri.get(uri);
        if (t == null) {
            // (集め直す前の曲: 名前は分からないが鳴らせる)
            t = new Track();
            t.uri = uri;
        }
        return t;
    }

    static Uri albumArtUri(long albumId) {
        return ContentUris.withAppendedId(Uri.parse("content://media/external/audio/albumart"), albumId);
    }

    /**
     * 端末の曲の一覧を JSON の配列にして返す(Unity 側の MediaStoreJson.cs が読む形)。読めなければ空の配列。
     * 各要素: uri, title, artist, album, albumArtist, disc, number, durationMs, albumId
     * folder が空でなければ、その名前のフォルダ(例 "PC-Music"。共有ストレージのどこにあってもよい)の下の曲だけ。
     */
    static String scanJson(Context context, String folder) {
        // (album_artist は API 30 より前は隠れた列。無い端末では、その列を抜いてやり直す)
        String json = scanJson(context, folder, true);
        if (json == null) json = scanJson(context, folder, false);
        return json != null ? json : "[]";
    }

    /** path(ファイルの場所)が、folder という名前のフォルダの下にあるか。大文字と小文字は区別しない。 */
    static boolean under(String path, String folder) {
        if (folder == null) return true;
        String want = folder.replace('\\', '/').toLowerCase(Locale.ROOT);
        while (want.startsWith("/")) want = want.substring(1);
        while (want.endsWith("/")) want = want.substring(0, want.length() - 1);
        if (want.isEmpty()) return true;
        if (path == null) return false;
        return ("/" + path.toLowerCase(Locale.ROOT)).contains("/" + want + "/");
    }

    private static String scanJson(Context context, String folder, boolean withAlbumArtist) {
        JSONArray out = new JSONArray();
        Uri collection = Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q
                ? MediaStore.Audio.Media.getContentUri(MediaStore.VOLUME_EXTERNAL)
                : MediaStore.Audio.Media.EXTERNAL_CONTENT_URI;
        String[] base = {
                MediaStore.Audio.Media._ID,
                MediaStore.Audio.Media.TITLE,
                MediaStore.Audio.Media.ARTIST,
                MediaStore.Audio.Media.ALBUM,
                MediaStore.Audio.Media.ALBUM_ID,
                MediaStore.Audio.Media.DURATION,
                MediaStore.Audio.Media.TRACK,
                MediaStore.Audio.Media.DATA,
        };
        String[] columns = base;
        if (withAlbumArtist) {
            columns = new String[base.length + 1];
            System.arraycopy(base, 0, columns, 0, base.length);
            columns[base.length] = "album_artist";
        }
        ConcurrentHashMap<String, Track> found = new ConcurrentHashMap<>();
        try (Cursor c = context.getContentResolver().query(collection, columns,
                MediaStore.Audio.Media.IS_MUSIC + " != 0", null, null)) {
            if (c != null) {
                while (c.moveToNext()) {
                    if (!under(c.getString(7), folder)) continue;
                    Track t = new Track();
                    t.uri = ContentUris.withAppendedId(collection, c.getLong(0)).toString();
                    t.title = clean(c.getString(1));
                    t.artist = clean(c.getString(2));
                    t.album = clean(c.getString(3));
                    t.albumId = c.getLong(4);
                    long duration = c.getLong(5);
                    // TRACK は「ディスク番号 × 1000 + 曲番号」のことがある
                    int track = c.getInt(6);
                    String albumArtist = withAlbumArtist ? clean(c.getString(8)) : "";
                    found.put(t.uri, t);
                    JSONObject o = new JSONObject();
                    o.put("uri", t.uri);
                    o.put("title", t.title);
                    o.put("artist", t.artist);
                    o.put("album", t.album);
                    o.put("albumArtist", albumArtist);
                    o.put("disc", track >= 1000 ? track / 1000 : 0);
                    o.put("number", track >= 1000 ? track % 1000 : track);
                    o.put("durationMs", duration);
                    o.put("albumId", t.albumId);
                    out.put(o);
                }
            }
        } catch (Exception e) {
            // (許可が無い、列が無いなど)
            Log.w(TAG, "曲を集められませんでした", e);
            return null;
        }
        byUri.clear();
        byUri.putAll(found);
        return out.toString();
    }

    private static String clean(String s) {
        if (s == null) return "";
        s = s.trim();
        return "<unknown>".equals(s) ? "" : s;
    }

    static File artFolder(Context context) {
        return new File(context.getCacheDir(), "iori_art");
    }

    /**
     * アルバムのジャケットを、一辺が size 以下の真四角の JPEG にして cacheDir/iori_art/{albumId}_{size}.jpg に書く。
     * 絵が無ければ、同じ名前に .none を付けた空のファイルを書く(Unity 側はどちらかができるのを待つ)。
     */
    static void writeArt(Context context, long albumId, String trackUri, int size) {
        File folder = artFolder(context);
        //noinspection ResultOfMethodCallIgnored
        folder.mkdirs();
        File file = new File(folder, albumId + "_" + size + ".jpg");
        File none = new File(folder, albumId + "_" + size + ".jpg.none");
        if (file.exists() || none.exists()) return;
        Bitmap bitmap = null;
        try {
            bitmap = loadArt(context.getContentResolver(), albumId, trackUri, size);
        } catch (Exception e) {
            // (絵の無いアルバムはここに来る)
        }
        try {
            if (bitmap == null) {
                //noinspection ResultOfMethodCallIgnored
                none.createNewFile();
                return;
            }
            int side = Math.min(bitmap.getWidth(), bitmap.getHeight());
            Bitmap square = Bitmap.createBitmap(bitmap, (bitmap.getWidth() - side) / 2, (bitmap.getHeight() - side) / 2, side, side);
            if (side > size) square = Bitmap.createScaledBitmap(square, size, size, true);
            File tmp = new File(folder, file.getName() + ".tmp");
            try (FileOutputStream stream = new FileOutputStream(tmp)) {
                square.compress(Bitmap.CompressFormat.JPEG, 92, stream);
            }
            if (!tmp.renameTo(file)) Log.w(TAG, "ジャケットを書けませんでした: " + file);
        } catch (Exception e) {
            Log.w(TAG, "ジャケットを書けませんでした", e);
        }
    }

    private static Bitmap loadArt(ContentResolver resolver, long albumId, String trackUri, int size) throws Exception {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            // (曲そのものからたのむと、アルバムの絵でもファイルに入った絵でも返ってくる)
            return resolver.loadThumbnail(Uri.parse(trackUri), new Size(size, size), null);
        }
        Uri art = albumArtUri(albumId);
        BitmapFactory.Options bounds = new BitmapFactory.Options();
        bounds.inJustDecodeBounds = true;
        try (InputStream in = resolver.openInputStream(art)) {
            BitmapFactory.decodeStream(in, null, bounds);
        }
        BitmapFactory.Options options = new BitmapFactory.Options();
        options.inSampleSize = 1;
        while (Math.min(bounds.outWidth, bounds.outHeight) / (options.inSampleSize * 2) >= size) options.inSampleSize *= 2;
        try (InputStream in = resolver.openInputStream(art)) {
            return BitmapFactory.decodeStream(in, null, options);
        }
    }
}
