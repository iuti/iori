namespace Iori.Camp
{
    /// <summary>The layers the camp uses to decide what shows up in the mirror images of the water.</summary>
    public static class CampLayers
    {
        /// <summary>Ground and grass: not mirrored (they lie under the water's surface or right at it).</summary>
        public const int Ground = 8;
        public const string GroundName = "CampGround";
        /// <summary>Water surfaces (Unity's built-in "Water" layer): never mirrored in themselves.</summary>
        public const int Water = 4;
        /// <summary>Seen by the main camera only: rain, and the flame that uses the depth texture.</summary>
        public const int NoMirror = 9;
        public const string NoMirrorName = "CampNoMirror";
        /// <summary>Seen in the mirror images only: the flame that works without the depth texture.</summary>
        public const int MirrorOnly = 10;
        public const string MirrorOnlyName = "CampMirrorOnly";

        public const int MirrorMask = ~((1 << Ground) | (1 << Water) | (1 << NoMirror) | (1 << 5));
        public const int MainMask = ~(1 << MirrorOnly);
    }
}
