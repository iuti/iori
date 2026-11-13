package com.iori.playback;

/** 通知に出す時間帯の名前。radio.json の phase id に対応させる。知らない id はそのまま出す。 */
final class PhaseLabels {
    private PhaseLabels() {
    }

    static String of(String phase) {
        if (phase == null) return "";
        switch (phase) {
            case "early_morning": return "早朝";
            case "morning": return "朝";
            case "day": return "昼";
            case "evening": return "夕方";
            case "night": return "夜";
            case "late_night": return "深夜";
            default: return phase;
        }
    }
}
