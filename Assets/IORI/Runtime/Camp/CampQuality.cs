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
    /// own mirror image, and it renders smaller. Three things are done another way on a phone, because
    /// measuring on a Reno7 A showed them to be most of the cost: the flame is a baked flip book on a
    /// card, the far view is read from a picture that is painted a strip at a time (CampSkyCache), and
    /// the lake's mirror image is smaller and drawn every other frame. Auto picks Phone on Android.
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
        [Tooltip("スマホでは、炎を立体の計算ではなく、前もって焼いた連番の絵(tools/camp/bake_fire.py)の板で出す")]
        public bool flameCardOnPhone = true;
        [Tooltip("スマホで、カメラが止まっている間、湖の鏡像を何フレームに 1 回描き直すか")]
        [Min(1)] public int phoneMirrorInterval = 2;
        [Tooltip("スマホで、湖の鏡像を画面の何倍の大きさで描くか")]
        [Range(0.1f, 0.5f)] public float phoneMirrorScale = 0.3f;
        [Tooltip("スマホで、景色を画面の何倍の大きさで描くか(UI は画面どおり)")]
        [Range(0.4f, 1f)] public float phoneRenderScale = 0.7f;
        [Tooltip("スマホでは、空と遠景を毎フレーム描かず、少しずつ描きためた絵から読む(CampSkyCache)")]
        public bool skyCacheOnPhone = true;
        [Tooltip("スマホでは、主な光の影を 1 枚の小さな影の地図で描く(影は筆の跡ごとに取るので、粗くても見た目は変わらない)")]
        public bool lightShadowsOnPhone = true;
        [Tooltip("画面いっぱいのシートが景色を覆っている間(ぼかしの向こうにしか見えない)、スマホで景色を描く大きさ")]
        [Range(0.2f, 1f)] public float phoneCoveredScale = 0.3f;
        [Tooltip("スマホでは、光のにじみ(ブルーム)を 4 分の 1 の大きさから作る")]
        public bool lightBloomOnPhone = true;

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
        /// <summary>Read by CampAtmosphere too: the cheaper glow (phones).</summary>
        public static bool LightBloom { get; private set; }
        public static CampQuality Instance { get; private set; }
        /// <summary>Set by the UI while a sheet covers the whole screen: the scene shows only through the blurred glass then.</summary>
        public static bool SceneCovered;

        public CampQualityLevel Applied { get; private set; }
        public float Fps { get; private set; }

        static readonly int DepthFxId = Shader.PropertyToID("_IoriDepthFx");
        float _t, _worst;
        int _frames;
        GameObject _card;
        Material _cardMaterial;
        Mesh _cardMesh;
        bool _cardFailed;
        CampSkyCache _sky;
        bool _covered;
        CampQualityLevel _last = (CampQualityLevel)(-1);
        MaterialPropertyBlock _block;

        void OnEnable()
        {
            Instance = this;
            // "-ioriQuality phone" (or full) on the command line: to look at the phone's picture on a desktop
            if (Application.isPlaying && !Application.isEditor)
            {
                var args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == "-ioriQuality")
                        level = args[i + 1] == "phone" ? CampQualityLevel.Phone : args[i + 1] == "full" ? CampQualityLevel.Full : level;
            }
            _last = (CampQualityLevel)(-1);
            Apply();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_card) DestroyImmediate(_card);
            if (_cardMaterial) DestroyImmediate(_cardMaterial);
            if (_cardMesh) DestroyImmediate(_cardMesh);
            _card = null; _cardMaterial = null; _cardMesh = null;
            _sky?.Dispose();
            _sky = null;
        }

        /// <summary>True while the far view is read from the picture painted a strip at a time (phones).</summary>
        public bool SkyIsCached => _sky != null && _sky.On;

        /// <summary>The sky was changed at once (time or weather set by hand): paint the whole picture anew.</summary>
        public void SkyChanged() => _sky?.Invalidate();

        /// <summary>True while the flame is shown as the baked flip book on a card (phones), not as the volume.</summary>
        public bool FlameIsCard => _card && _card.activeSelf;
        /// <summary>The card's renderer, once it has been made (for measuring).</summary>
        public Renderer FlameCardRenderer => _card ? _card.GetComponent<Renderer>() : null;

        /// <summary>
        /// The flame as a card with the baked flip book, standing at the foot of the volume's box. Made on
        /// first use; null if the picture or its shader is missing (then the volume stays).
        /// </summary>
        GameObject FlameCard()
        {
            if (_card || _cardFailed || !flameMirror) return _card;
            var shader = Resources.Load<Shader>("IoriCamp/IoriFireCard");
            var book = Resources.Load<Texture2D>("IoriCamp/fire_flipbook");
            if (!shader || !shader.isSupported || !book)
            {
                Debug.LogWarning("[IORI] 炎の連番の絵(Resources/IoriCamp/fire_flipbook と IoriFireCard)が使えないので、立体の炎のままにします");
                _cardFailed = true;
                return null;
            }
            _cardMaterial = new Material(shader) { name = "CampFlameCard (自動生成)", hideFlags = HideFlags.HideAndDontSave };
            _cardMaterial.SetTexture("_Book", book);
            _cardMesh = new Mesh { name = "Flame card", hideFlags = HideFlags.HideAndDontSave };
            _cardMesh.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) });
            _cardMesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _cardMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            // (the shader places the corners itself: the card is never left out for being off screen while the fire is near)
            _cardMesh.bounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(3f, 3f, 3f));

            var box = flameMirror.transform;
            _card = new GameObject("炎(連番の絵、自動生成)") { hideFlags = HideFlags.HideAndDontSave };
            _card.transform.SetParent(box.parent, false);
            // the flame starts at the bottom of the volume's box
            _card.transform.position = box.position - Vector3.up * (box.lossyScale.y * 0.5f);
            _card.transform.rotation = Quaternion.identity;
            _card.AddComponent<MeshFilter>().sharedMesh = _cardMesh;
            var r = _card.AddComponent<MeshRenderer>();
            r.sharedMaterial = _cardMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return _card;
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
            LightBloom = !full && lightBloomOnPhone;
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
            // On a phone the flame is the baked flip book on a card, for the main camera and the mirror images alike.
            var card = !full && flameCardOnPhone && Application.isPlaying ? FlameCard() : null;
            if (_card) _card.SetActive(card);
            if (flameMirror)
            {
                flameMirror.gameObject.SetActive(!card);
                flameMirror.gameObject.layer = full ? CampLayers.MirrorOnly : 0;
                flameMirror.GetPropertyBlock(_block);
                _block.SetFloat("_Steps", full ? 14f : 12f);
                flameMirror.SetPropertyBlock(_block);
            }
            _covered = SceneCovered;
            bool covered = !full && _covered;
            if (lakeMirror)
            {
                lakeMirror.scale = full ? 0.5f : phoneMirrorScale;
                lakeMirror.interval = full ? 1 : Mathf.Max(covered ? 8 : 1, phoneMirrorInterval);
            }
            if (puddleMirror) { puddleMirror.render = full; puddleMirror.scale = 0.5f; }

            if (Application.isPlaying)
            {
#if !UNITY_EDITOR
                // The pipeline asset is shared; in the editor it is left as authored (Full).
                if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                {
                    urp.supportsCameraDepthTexture = full;
                    urp.msaaSampleCount = full ? 4 : 2;
                    urp.renderScale = full ? 1f : (covered ? Mathf.Min(phoneCoveredScale, phoneRenderScale) : phoneRenderScale);
                    urp.shadowDistance = full ? 28f : 16f;
                    bool lightShadows = !full && lightShadowsOnPhone;
                    urp.shadowCascadeCount = lightShadows ? 1 : 2;
                    SetShadowMapSize(urp, lightShadows ? 1024 : 2048);
                }
#endif
                QualitySettings.vSyncCount = full ? 1 : 0;
                Application.targetFrameRate = full ? desktopFrameRate : phoneFrameRate;
            }
        }

        // (the pipeline asset keeps the size of the main light's shadow map in a field of its own; set through its
        // name, so this works whether or not the installed version offers a way to set it)
        static void SetShadowMapSize(UniversalRenderPipelineAsset urp, int size)
        {
            try
            {
                var field = typeof(UniversalRenderPipelineAsset).GetField("m_MainLightShadowmapResolution", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (field != null && field.FieldType.IsEnum) field.SetValue(urp, System.Enum.ToObject(field.FieldType, size));
            }
            catch (System.Exception) { }
        }

        void LateUpdate()
        {
            // the far view's picture: after the atmosphere has set the sky's values, before the cameras draw
            bool cached = Application.isPlaying && Applied == CampQualityLevel.Phone && skyCacheOnPhone;
            if (!cached) { _sky?.Stop(); return; }
            // (behind a sheet that covers the whole screen the picture is left as it is)
            if (_covered && _sky != null && _sky.On) return;
            var cam = lakeMirror && lakeMirror.source ? lakeMirror.source : Camera.main;
            _sky ??= new CampSkyCache();
            _sky.Step(cam, RenderSettings.skybox, phoneRenderScale, phoneMirrorScale);
        }

        void Update()
        {
            if (_last != Resolve() || _covered != SceneCovered) Apply();
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
