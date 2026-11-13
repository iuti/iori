package com.iori.playback;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Unity(C#)の PlaybackPlan と同じ予定表。形式と決まりは
 * Assets/IORI/Runtime/Audio/Core/PlaybackPlan.cs に合わせること(あちらが正)。
 */
final class Plan {
    static final long DAY_MS = 24L * 3600 * 1000;

    static final class Clip {
        String id;
        long start;
        long end;
        String file;
        float volume;
        int fadeIn;
        int fadeOut;
        boolean loop;
        long offset;
        String phase;

        boolean isActiveAt(long t) {
            return t >= start && t < end;
        }

        /** 等パワーのフェード係数(PlaybackPlan.cs の MusicClip.FadeGainAt と同じ)。 */
        float fadeGainAt(long t) {
            if (!isActiveAt(t)) return 0f;
            double gain = 1.0;
            if (fadeIn > 0 && t - start < fadeIn) gain *= Math.sin((t - start) / (double) fadeIn * Math.PI / 2);
            if (fadeOut > 0 && end - t < fadeOut) gain *= Math.sin((end - t) / (double) fadeOut * Math.PI / 2);
            return (float) gain;
        }

        /** 再生位置(MusicClip.PositionAt と同じ)。 */
        long positionAt(long t, long durationMs) {
            long p = t - start + offset;
            if (p < 0) p = 0;
            if (loop && durationMs > 0) p %= durationMs;
            return p;
        }
    }

    static final class OneShot {
        long at;
        String file;
        float volume;
        String kind;
    }

    static final class Ambient {
        String id;
        String file;
        float volume;
    }

    static final class Span {
        long start;
        long end;
        String phase;
        boolean quiet;
    }

    long generatedAt;
    long validUntil;
    long clockOffsetMs;
    String weather;
    float musicVolume = 1f;
    float oneShotVolume = 1f;
    final List<Span> phases = new ArrayList<>();
    final List<Clip> music = new ArrayList<>();
    final List<OneShot> oneShots = new ArrayList<>();
    final List<Ambient> ambient = new ArrayList<>();
    final Map<String, Long> durations = new HashMap<>();

    /** 予定表の時刻に直す。期限切れなら最後の 24 時間を繰り返す(PlaybackPlan.ToPlanTime と同じ)。 */
    long toPlanTime(long t) {
        if (validUntil <= 0 || t < validUntil || validUntil - generatedAt < DAY_MS) return t;
        return validUntil - DAY_MS + (t - validUntil) % DAY_MS;
    }

    Span spanAt(long t) {
        for (Span s : phases) if (t >= s.start && t < s.end) return s;
        return null;
    }

    Clip findClip(String id) {
        for (Clip c : music) if (c.id.equals(id)) return c;
        return null;
    }

    long durationOf(String file) {
        Long d = durations.get(file);
        return d == null ? 0 : d;
    }

    static Plan parse(String json) throws JSONException {
        JSONObject root = new JSONObject(json);
        Plan p = new Plan();
        p.generatedAt = root.optLong("generatedAt");
        p.validUntil = root.optLong("validUntil");
        p.clockOffsetMs = root.optLong("clockOffsetMs");
        p.weather = root.optString("weather", "clear");
        p.musicVolume = (float) root.optDouble("musicVolume", 1);
        p.oneShotVolume = (float) root.optDouble("oneShotVolume", 1);

        JSONArray phases = root.optJSONArray("phases");
        for (int i = 0; phases != null && i < phases.length(); i++) {
            JSONObject o = phases.getJSONObject(i);
            Span s = new Span();
            s.start = o.getLong("start");
            s.end = o.getLong("end");
            s.phase = o.optString("phase");
            s.quiet = "quiet".equals(o.optString("mode"));
            p.phases.add(s);
        }
        JSONArray music = root.optJSONArray("music");
        for (int i = 0; music != null && i < music.length(); i++) {
            JSONObject o = music.getJSONObject(i);
            Clip c = new Clip();
            c.id = o.optString("id");
            c.start = o.getLong("start");
            c.end = o.getLong("end");
            c.file = o.getString("file");
            c.volume = (float) o.optDouble("volume", 1);
            c.fadeIn = o.optInt("fadeIn");
            c.fadeOut = o.optInt("fadeOut");
            c.loop = o.optBoolean("loop");
            c.offset = o.optLong("offset");
            c.phase = o.optString("phase");
            p.music.add(c);
        }
        JSONArray oneShots = root.optJSONArray("oneShots");
        for (int i = 0; oneShots != null && i < oneShots.length(); i++) {
            JSONObject o = oneShots.getJSONObject(i);
            OneShot s = new OneShot();
            s.at = o.getLong("at");
            s.file = o.getString("file");
            s.volume = (float) o.optDouble("volume", 1);
            s.kind = o.optString("kind");
            p.oneShots.add(s);
        }
        JSONArray ambient = root.optJSONArray("ambient");
        for (int i = 0; ambient != null && i < ambient.length(); i++) {
            JSONObject o = ambient.getJSONObject(i);
            Ambient a = new Ambient();
            a.id = o.getString("id");
            a.file = o.getString("file");
            a.volume = (float) o.optDouble("volume", 0);
            p.ambient.add(a);
        }
        JSONObject durations = root.optJSONObject("durations");
        if (durations != null) {
            JSONArray names = durations.names();
            for (int i = 0; names != null && i < names.length(); i++) {
                String name = names.getString(i);
                p.durations.put(name, durations.getLong(name));
            }
        }
        return p;
    }
}
