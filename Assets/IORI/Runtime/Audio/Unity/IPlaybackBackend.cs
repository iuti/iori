namespace Iori.Audio
{
    /// <summary>予定表を鳴らす係。Android はネイティブのサービス、PC は Unity のオーディオ。</summary>
    public interface IPlaybackBackend
    {
        string Name { get; }

        /// <summary>予定表を渡して鳴らし始める(鳴っていれば差し替える。同じクリップは途切れずに続く)。</summary>
        void ApplyPlan(PlaybackPlan plan, string planJson);

        /// <summary>スライダー操作用。予定表を作り直さずに即反映する。</summary>
        void SetMusicVolume(float volume);

        void SetAmbientVolume(string id, float volume);

        void Stop();

        /// <summary>動作確認パネルに出す状態の文字列。</summary>
        string Status { get; }
    }
}
