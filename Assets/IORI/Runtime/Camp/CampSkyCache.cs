using UnityEngine;
using UnityEngine.Rendering;

namespace Iori.Camp
{
    /// <summary>
    /// Phones: the far view (sky, clouds, stars, the mountains, the town, the far shore: IoriSky) costs more
    /// than everything else in the picture when it is painted anew for every pixel of every frame, and it
    /// hardly changes from one frame to the next. So it is painted into a wide picture that covers all the
    /// sky the camp's camera can ever see, one narrow strip per frame (the whole of it about every three
    /// seconds: clouds drift and stars twinkle at that pace), and the skybox only reads that picture.
    /// A second, smaller picture without the stars and the town's lights is kept for the mirror images.
    /// When the sky changes at once (the time or the weather set by hand), the whole picture is painted
    /// in one go. Owned and driven by CampQuality.
    /// </summary>
    public sealed class CampSkyCache
    {
        // What the camera can see of the sky, with room to spare. Up and down: from 5 degrees below the horizon
        // to 27 above (the camera never looks higher than 19). Left and right: the camera turns up to 28 degrees
        // to either side (CampCameraRig: yawLimit and the sway), plus half of what the screen shows, plus 6.
        const float ElLow = -5f, ElHigh = 27f, TurnAndRoom = 34f;
        const int MaxWidth = 4096;

        static readonly int MapId = Shader.PropertyToID("_IoriSkyCacheMap");
        static readonly int MainId = Shader.PropertyToID("_IoriSkyCacheA");
        static readonly int MirrorId = Shader.PropertyToID("_IoriSkyCacheB");
        static readonly int DirectId = Shader.PropertyToID("_IoriSkyBakeDirect");
        const string Keyword = "IORI_SKY_CACHED";

        // a jump in any of these means the sky changed at once
        static readonly int[] Watched =
        {
            Shader.PropertyToID("_IoriSkyHorizon"), Shader.PropertyToID("_IoriSkyMid"), Shader.PropertyToID("_IoriSkyZenith"),
            Shader.PropertyToID("_IoriSunDir"), Shader.PropertyToID("_IoriMoonDir"), Shader.PropertyToID("_IoriSunColor"),
            Shader.PropertyToID("_IoriMoonColor"), Shader.PropertyToID("_IoriSkyParams"), Shader.PropertyToID("_IoriCloud"),
            Shader.PropertyToID("_IoriCloudDark"), Shader.PropertyToID("_IoriCloudParams"), Shader.PropertyToID("_IoriFarNear"),
            Shader.PropertyToID("_IoriFarFar"), Shader.PropertyToID("_IoriTown"), Shader.PropertyToID("_IoriFogColor"),
            Shader.PropertyToID("_IoriMistColor"),
        };

        readonly Vector4[] _seen = new Vector4[Watched.Length];
        RenderTexture _main, _mirror;
        Material _material;
        CommandBuffer _cmd;
        int _next;
        bool _whole = true;
        bool _failed;

        /// <summary>How many frames it takes to paint the whole picture once.</summary>
        public int strips = 90;

        /// <summary>True while the skybox reads the picture.</summary>
        public bool On { get; private set; }

        /// <summary>Paints the whole picture on the next frame (the sky was changed at once).</summary>
        public void Invalidate() => _whole = true;

        static RenderTexture Make(string name, int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.DefaultHDR)
            {
                name = name, hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1,
            };
            rt.Create();
            return rt;
        }

        static void Free(ref RenderTexture rt)
        {
            if (!rt) return;
            rt.Release();
            Object.DestroyImmediate(rt);
            rt = null;
        }

        /// <summary>
        /// Call once a frame, after the atmosphere has set the sky's values and before the cameras draw.
        /// cam: the camp's camera. sky: the skybox material (its numbers are copied). renderScale: how much
        /// smaller than the screen the scene is drawn. mirrorScale: the same for the mirror image.
        /// </summary>
        public void Step(Camera cam, Material sky, float renderScale, float mirrorScale)
        {
            if (_failed || !cam || !sky) { Stop(); return; }
            if (!_material)
            {
                var shader = Resources.Load<Shader>("IoriCamp/IoriSkyBake");
                if (!shader || !shader.isSupported)
                {
                    Debug.LogWarning("[IORI] 空を描きためるシェーダー(Resources/IoriCamp/IoriSkyBake)が使えないので、空は毎フレーム描きます");
                    _failed = true;
                    Stop();
                    return;
                }
                _material = new Material(shader) { name = "CampSkyBake (自動生成)", hideFlags = HideFlags.HideAndDontSave };
                _cmd = new CommandBuffer { name = "IORI sky cache" };
            }
            if (sky.HasProperty("_MountTiles")) _material.SetFloat("_MountTiles", sky.GetFloat("_MountTiles"));
            if (sky.HasProperty("_FacetLean")) _material.SetFloat("_FacetLean", sky.GetFloat("_FacetLean"));

            // as fine as the screen shows it straight ahead (pixels per degree), within what a texture may be
            float halfAcross = Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.1f, cam.aspect)) * Mathf.Rad2Deg;
            float azHalf = Mathf.Clamp(Mathf.Ceil(TurnAndRoom + halfAcross), 40f, 85f);
            float tanLow = Mathf.Tan(ElLow * Mathf.Deg2Rad), tanHigh = Mathf.Tan(ElHigh * Mathf.Deg2Rad);
            float heightDeg = (tanHigh - tanLow) * Mathf.Rad2Deg;
            float perDegree = cam.pixelHeight / Mathf.Max(1f, cam.fieldOfView);
            float fine = Mathf.Min(perDegree * Mathf.Clamp(renderScale, 0.3f, 1f), MaxWidth / (2f * azHalf));
            float coarse = Mathf.Min(perDegree * Mathf.Clamp(mirrorScale, 0.1f, 1f), fine);
            int w = Mathf.Max(64, Mathf.RoundToInt(2f * azHalf * fine)), h = Mathf.Max(32, Mathf.RoundToInt(heightDeg * fine));
            int wm = Mathf.Max(64, Mathf.RoundToInt(2f * azHalf * coarse)), hm = Mathf.Max(32, Mathf.RoundToInt(heightDeg * coarse));
            // (a few pixels' difference is not worth painting everything again: the screen's size settles within the first frames)
            if (!_main || Mathf.Abs(_main.width - w) > 8 || Mathf.Abs(_main.height - h) > 8)
            {
                Free(ref _main);
                _main = Make("IORI sky cache", w, h);
                _whole = true;
            }
            if (!_mirror || Mathf.Abs(_mirror.width - wm) > 8 || Mathf.Abs(_mirror.height - hm) > 8)
            {
                Free(ref _mirror);
                _mirror = Make("IORI sky cache (mirror)", wm, hm);
                _whole = true;
            }
            if (!_main.IsCreated() || !_mirror.IsCreated()) { _main.Create(); _mirror.Create(); _whole = true; }

            float azLeft = -azHalf * Mathf.Deg2Rad;
            Shader.SetGlobalVector(MapId, new Vector4(azLeft, 1f / (2f * azHalf * Mathf.Deg2Rad), tanLow, 1f / (tanHigh - tanLow)));

            // did the sky change at once?
            for (int i = 0; i < Watched.Length; i++)
            {
                Vector4 v = Shader.GetGlobalVector(Watched[i]);
                Vector4 d = v - _seen[i];
                float size = Mathf.Max(Mathf.Abs(d.x), Mathf.Max(Mathf.Abs(d.y), Mathf.Max(Mathf.Abs(d.z), Mathf.Abs(d.w))));
                if (size > 0.03f) _whole = true;
                _seen[i] = v;
            }

            int count = Mathf.Max(1, strips);
            _cmd.Clear();
            Paint(_main, 1f, count);
            Paint(_mirror, 0f, count);
            _cmd.DisableScissorRect();
            Graphics.ExecuteCommandBuffer(_cmd);
            Graphics.SetRenderTarget(null);
            if (_whole) { _whole = false; _next = 0; }
            else _next = (_next + 1) % count;

            Shader.SetGlobalTexture(MainId, _main);
            Shader.SetGlobalTexture(MirrorId, _mirror);
            if (!On) { Shader.EnableKeyword(Keyword); On = true; }
        }

        void Paint(RenderTexture rt, float direct, int count)
        {
            _cmd.SetGlobalFloat(DirectId, direct);
            _cmd.SetRenderTarget(rt);
            if (_whole) _cmd.DisableScissorRect();
            else
            {
                // (strips reach across the picture's whole height: which way up a device keeps its pictures does not matter then)
                int x0 = Mathf.FloorToInt((float)_next * rt.width / count);
                int x1 = Mathf.Min(rt.width, Mathf.CeilToInt((float)(_next + 1) * rt.width / count));
                _cmd.EnableScissorRect(new Rect(x0, 0, Mathf.Max(1, x1 - x0), rt.height));
            }
            _cmd.DrawProcedural(Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3);
        }

        /// <summary>The skybox paints itself again (the pictures are kept for the next time).</summary>
        public void Stop()
        {
            if (On) { Shader.DisableKeyword(Keyword); On = false; }
            _whole = true;
        }

        public void Dispose()
        {
            Stop();
            Free(ref _main);
            Free(ref _mirror);
            if (_material) Object.DestroyImmediate(_material);
            _material = null;
            _cmd?.Release();
            _cmd = null;
        }
    }
}
