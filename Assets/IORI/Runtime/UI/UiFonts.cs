using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Iori.Ui
{
    /// <summary>
    /// The UI's typefaces, made ready for TextMeshPro when the UI starts: Zen Kaku Gothic New for
    /// reading text and Shippori Mincho for the large names ("夕方", song titles). The letters are
    /// drawn into the font's sheet the first time each one is shown, so no list of characters has to
    /// be prepared and any song title works.
    /// </summary>
    public static class UiFonts
    {
        public static TMP_FontAsset Regular { get; private set; }
        public static TMP_FontAsset Medium { get; private set; }
        public static TMP_FontAsset Bold { get; private set; }
        public static TMP_FontAsset Mincho { get; private set; }

        static bool _made;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            _made = false;
            Regular = Medium = Bold = Mincho = null;
        }

        public static void Ensure()
        {
            if (_made) return;
            _made = true;
            TMP_FontAsset fallback = null;
            try { fallback = TMP_Settings.defaultFontAsset; } catch (System.Exception) { }
            if (!fallback) Debug.LogWarning("[IORI] TextMeshPro の基本素材が入っていません(メニュー IORI/UI/TextMeshPro の基本素材を入れる)");

            Medium = Make("ZenKakuGothicNew-Medium", 48, 5, 1024) ?? fallback;
            Regular = Make("ZenKakuGothicNew-Regular", 48, 5, 1024) ?? Medium;
            Bold = Make("ZenKakuGothicNew-Bold", 48, 5, 1024) ?? Medium;
            Mincho = Make("ShipporiMincho-SemiBold", 72, 7, 1024) ?? Medium;

            // a letter one face lacks is taken from the next
            Chain(Mincho, Medium);
            Chain(Regular, Medium);
            Chain(Bold, Medium);
            if (fallback) Chain(Medium, fallback);
        }

        static void Chain(TMP_FontAsset font, TMP_FontAsset next)
        {
            if (!font || !next || font == next) return;
            if (font.fallbackFontAssetTable == null) font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
            if (!font.fallbackFontAssetTable.Contains(next)) font.fallbackFontAssetTable.Add(next);
        }

        static TMP_FontAsset Make(string file, int size, int padding, int sheet)
        {
            var font = Resources.Load<Font>("IoriUI/Fonts/" + file);
            if (!font)
            {
                Debug.LogWarning("[IORI] フォントが見つかりません: IoriUI/Fonts/" + file);
                return null;
            }
            TMP_FontAsset asset = null;
            try
            {
                asset = TMP_FontAsset.CreateFontAsset(font, size, padding, GlyphRenderMode.SDFAA, sheet, sheet, AtlasPopulationMode.Dynamic, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[IORI] フォントを用意できませんでした: " + file + " (" + e.Message + ")");
            }
            if (!asset) return null;
            asset.name = file;
            asset.hideFlags = HideFlags.HideAndDontSave;
            return asset;
        }
    }
}
