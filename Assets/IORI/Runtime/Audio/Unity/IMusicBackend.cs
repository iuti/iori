using System;
using System.Collections.Generic;
using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// 音楽プレーヤーの再生係。端末の曲を集めることと、渡された列をその順に鳴らすことだけをする
    /// (どの順に鳴らすかを決めるのは <see cref="PlayQueue"/>)。PC は Unity のオーディオ、Android はネイティブのサービス。
    /// どちらも同じ決まりで進む: 曲が終わったら、くり返しなしは次へ進んで最後で止まる、All は最後の次が最初、One は同じ曲をもう一度。
    /// </summary>
    /// <summary>ジャケットの絵の大きさ(ピクセル)。</summary>
    public static class MusicArt
    {
        /// <summary>一覧の行や下の帯に出す小さな絵。</summary>
        public const int Small = 160;
        /// <summary>プレーヤーの画面に大きく出す絵。</summary>
        public const int Large = 1024;
    }

    public interface IMusicBackend
    {
        string Name { get; }

        /// <summary>端末の曲を集める。終わったら done を画面のスレッドで呼ぶ。</summary>
        void Scan(Action<List<MusicTrack>> done);

        /// <summary>
        /// 列を渡す。order[index] がいま用意している曲と同じなら、その曲は途切れずに続く(positionMs は使わない)。
        /// 違う曲なら positionMs から用意する。play が true なら鳴らし、false なら止めたままにする。
        /// </summary>
        void SetQueue(IReadOnlyList<MusicTrack> order, int index, long positionMs, RepeatMode repeat, bool play);

        void SetRepeat(RepeatMode repeat);
        void Play();
        void Pause();
        void Seek(long positionMs);
        void SetVolume(float volume);

        /// <summary>鳴らすつもりでいるか(読み込み中も true)。</summary>
        bool IsPlaying { get; }
        /// <summary>列の中の、いまの曲の位置(再生係が自分で次の曲へ進むと変わる)。</summary>
        int Index { get; }
        long PositionMs { get; }
        /// <summary>いまの曲の長さ。分からない間は 0。</summary>
        long DurationMs { get; }
        /// <summary>列の最後まで鳴らし終えて止まったら true(次に SetQueue か Play を呼ぶまで)。</summary>
        bool Finished { get; }

        /// <summary>
        /// 曲のジャケットの絵を用意して done に渡す(無ければ null)。絵は真四角で、一辺は size 以下(<see cref="MusicArt"/> の大きさを渡す)。
        /// 絵は再生係の持ち物で、受け取った側は壊さない。たくさん用意すると古い絵から捨てられるので、
        /// 出しっぱなしにする側は、絵が消えていたらもう一度たのむ。done はすぐその場で呼ばれることもある。
        /// </summary>
        void LoadArt(MusicTrack track, int size, Action<Texture2D> done);

        /// <summary>動作確認用の状態の文字列。</summary>
        string Status { get; }
    }
}
