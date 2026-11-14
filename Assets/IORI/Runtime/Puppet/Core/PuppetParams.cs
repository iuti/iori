// What a character's pose and face are driven by (LivLiv Player's parameters, FORMAT.md Appendix D).
namespace Iori.Puppet
{
    public sealed class PuppetParams
    {
        /// <summary>the head's turn, nod and tilt, -30..30</summary>
        public float angleX, angleY, angleZ;
        /// <summary>0..1 (L is the picture's left)</summary>
        public float eyeLOpen = 1, eyeROpen = 1;
        /// <summary>-1..1</summary>
        public float eyeBallX, eyeBallY;
        /// <summary>0..1</summary>
        public float mouthOpen, smile;
        /// <summary>the voice's vowel: -1 round (o, u) .. +1 wide (i, e)</summary>
        public float vowel;
        /// <summary>the torso's own turn and lean, -10..10</summary>
        public float bodyX, bodyZ;
        /// <summary>0..1</summary>
        public float breath;
    }
}
