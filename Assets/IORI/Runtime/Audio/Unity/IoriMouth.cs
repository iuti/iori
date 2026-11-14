using System.Collections.Generic;

namespace Iori.Audio
{
    /// <summary>
    /// イオリの口を動かす相手。いまの人形(口のコマ差し替え)でも、この先の別のモデル(VRoid など)でも、
    /// これを実装して <see cref="IoriMouth.Register"/> すれば、声に合わせた同じ値を受け取れる。
    /// </summary>
    public interface IMouthTarget
    {
        /// <summary>
        /// 毎フレーム、話している間だけ呼ばれる。open は口の開き(0: 閉じ 〜 1: いちばん開く)、
        /// vowel は口の形(-1: お・う の丸い口 〜 0: ふつう 〜 1: い・え の横に広い口)。
        /// 段の切り替えやちらつき止めは、受け取った側の仕事。
        /// </summary>
        void SetMouth(float open, float vowel);

        /// <summary>話し終わった。口を閉じて、あとは自分の動き(待機の動きなど)に任せる。</summary>
        void ReleaseMouth();
    }

    /// <summary>
    /// 声から出した口の値の受け渡し口。声の係(<see cref="VoiceDirector"/>)が値を書き、登録された相手に配る。
    /// 声の係は口の相手のことを知らず、相手は声のことを知らない。
    /// </summary>
    public static class IoriMouth
    {
        static readonly List<IMouthTarget> Targets = new List<IMouthTarget>();

        public static float Open { get; private set; }
        public static float Vowel { get; private set; }
        /// <summary>声が鳴っている間 true。</summary>
        public static bool Speaking { get; private set; }

        public static void Register(IMouthTarget target)
        {
            if (target != null && !Targets.Contains(target)) Targets.Add(target);
        }

        public static void Unregister(IMouthTarget target) => Targets.Remove(target);

        /// <summary>話している間、毎フレーム呼ぶ。</summary>
        public static void Set(float open, float vowel)
        {
            Open = open < 0 ? 0 : open > 1 ? 1 : open;
            Vowel = vowel < -1 ? -1 : vowel > 1 ? 1 : vowel;
            Speaking = true;
            for (var i = Targets.Count - 1; i >= 0; i--) Targets[i].SetMouth(Open, Vowel);
        }

        /// <summary>話し終わったら 1 回呼ぶ。</summary>
        public static void Release()
        {
            if (!Speaking) return;
            Open = Vowel = 0;
            Speaking = false;
            for (var i = Targets.Count - 1; i >= 0; i--) Targets[i].ReleaseMouth();
        }
    }
}
