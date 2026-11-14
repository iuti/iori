using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Iori.Camp
{
    public enum CampQualityLevel { Auto, Phone, Full }

    /// <summary>
    /// How much the camp spends on its looks. "Full" is everything (PC). "Phone" drops what a
    /// mid-range phone cannot afford at 30 fps: depth of field and the depth-based effects (soft
    /// water's edge, flame cut by the logs), the fire's shadows, a third of the grass, the puddles'
    /// own mirror image, and it renders a little smaller. Auto picks Phone on Android.
    /// In development builds it logs the frame rate every few seconds ("[IORI] perf ...") so the
    /// cost can be read from adb logcat without looking at the screen.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-60)]
    public sealed class CampQuality : MonoBehaviour
    {
        public CampQualityLevel level = CampQualityLevel.Auto;
        public int phoneFrameRate = 30;
        public int desktopFrameRate = 60;
        public bool logPerformance = true;
        public float logInterval = 5f;

        [Header("シーンの部品(「シーンを組み立てる」が入れる)")]
        public Renderer[] denseGrass;
        public Light fireLight;
        public Light keyLight;
        public Renderer flame;
        public Renderer flameMirror;
        public CampReflection lakeMirror;
        public CampReflection puddleMirror;

        /// <summary>Read by CampAtmosphere when it sets up the picture's finish.</summary>
        public static bool DepthOfFieldOn { get; private set; } = true;
        public static CampQuality Instance { get; private set; }

        public CampQualityLevel Applied { get; private set; }
        public float Fps { get; private set; }

        static readonly int DepthFxId = Shader.PropertyToID("_IoriDepthFx");
        float _t, _worst;
        int _frames;
        CampQualityLevel _last = (CampQualityLevel)(-1);
        MaterialPropertyBlock _block;

        void OnEnable()
        {
            Instance = this;
            _last = (CampQualityLevel)(-1);
            Apply();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void OnValidate() => _last = (CampQualityLevel)(-1);

        public CampQualityLevel Resolve()
        {
            if (level != CampQualityLevel.Auto) return level;
            return Application.isMobilePlatform ? CampQualityLevel.Phone : CampQualityLevel.Full;
        }

        public void Apply()
        {
            var q = Resolve();
            Applied = q;
            _last = q;
            bool full = q == CampQualityLevel.Full;

            DepthOfFieldOn = full;
            Shader.SetGlobalFloat(DepthFxId, full ? 1f : 0f);
            if (denseGrass != null)
                for (int i = 0; i < denseGrass.Length; i++)
                    if (denseGrass[i]) denseGrass[i].enabled = full || i == 0;
            if (fireLight) fireLight.shadows = full ? LightShadows.Soft : LightShadows.None;
            if (keyLight) keyLight.shadows = full ? LightShadows.Soft : LightShadows.Hard;
            // The flame: with the depth texture it is cut by the logs and only the main camera sees that
            // version; the plain version is for the mirror images, and for everything on a phone.
            _block ??= new MaterialPropertyBlock();
            if (flame)
            {
                flame.gameObject.SetActive(full);
                flame.gameObject.layer = CampLayers.NoMirror;
                flame.GetPropertyBlock(_block);
                _block.SetFloat("_Steps", 22f);
                flame.SetPropertyBlock(_block);
            }
            if (flameMirror)
            {
                flameMirror.gameObject.layer = full ? CampLayers.MirrorOnly : 0;
                flameMirror.GetPropertyBlock(_block);
                _block.SetFloat("_Steps", full ? 14f : 12f);
                flameMirror.SetPropertyBlock(_block);
            }
            if (lakeMirror) lakeMirror.scale = full ? 0.5f : 0.33f;
            if (puddleMirror) { puddleMirror.render = full; puddleMirror.scale = 0.5f; }

            if (Application.isPlaying)
            {
#if !UNITY_EDITOR
                // The pipeline asset is shared; in the editor it is left as authored (Full).
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                {
                    urp.supportsCameraDepthTexture = full;
                    urp.msaaSampleCount = full ? 4 : 2;
                    urp.renderScale = full ? 1f : 0.85f;
                    urp.shadowDistance = full ? 28f : 16f;
                }
#endif
                QualitySettings.vSyncCount = full ? 1 : 0;
                Application.targetFrameRate = full ? desktopFrameRate : phoneFrameRate;
            }
        }

        void Update()
        {
            if (_last != Resolve()) Apply();
            if (!Application.isPlaying) return;
            _t += Time.unscaledDeltaTime;
            _frames++;
            _worst = Mathf.Max(_worst, Time.unscaledDeltaTime);
            if (_t >= logInterval)
            {
                Fps = _frames / _t;
                if (logPerformance && Debug.isDebugBuild && !Application.isEditor)
                    Debug.Log($"[IORI] perf fps={Fps:0.0} avg_ms={_t / _frames * 1000f:0.0} worst_ms={_worst * 1000f:0.0} target={Application.targetFrameRate} screen={Screen.width}x{Screen.height} quality={Applied}");
                _t = 0; _frames = 0; _worst = 0;
            }
        }
    }
}
