using System;
using Iori.Audio;
using UnityEngine;

namespace Iori.Camp
{
    public enum CampWeather { Clear, Rain, AfterRain }
    public enum CampSeason { Auto, Spring, Summer, Autumn, Winter }

    /// <summary>
    /// Decides what the camp looks like right now (time of day, weather, season, sleep mode) and
    /// pushes it to the shaders as global values. Nothing in the scene uses Unity lights; the
    /// fire, lantern and tent lights are evaluated in the camp shaders from these globals.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public sealed class CampAtmosphere : MonoBehaviour
    {
        public static CampAtmosphere Instance { get; private set; }

        [Tooltip("色の設定。空なら既定値")] public CampLook look;
        public Material skyMaterial;
        public Texture2D brushTexture;

        [Header("時刻")]
        [Tooltip("Play 中は端末の時計(ラジオの時計のずれも反映)に合わせる。オフなら下の時刻")]
        public bool followClock = true;
        [Range(0, 24)] public float previewHour = 18.6f;

        [Header("天気")]
        [Tooltip("Play 中はラジオの天気に合わせる(雨がやむと雨上がりになり、しばらくして乾く)")]
        public bool followRadioWeather = true;
        public CampWeather weather = CampWeather.Clear;
        [Tooltip("雨上がりから乾くまでの分")] public float dryMinutes = 90f;
        [Tooltip("雨の降り始めとやみの切り替えにかける秒")] public float weatherFadeSeconds = 6f;

        [Header("季節")]
        public CampSeason season = CampSeason.Auto;

        [Header("おやすみモード")]
        public bool sleep;
        public float sleepFadeSeconds = 3f;

        [Header("筆と光の段")]
        [Range(0, 1)] public float brushStrength = 1f;
        [Tooltip("この距離まで筆あとを細かく出す")] public float brushNear = 3f;
        [Tooltip("この距離で筆あとが消える")] public float brushFar = 16f;
        public float brushScale = 1.1f;
        [Tooltip("光の段の数(0 でなめらか)")] [Range(0, 12)] public float lightBands = 6f;
        [Range(0, 2)] public float bandBrush = 0.9f;
        [Range(0, 2)] public float slotSwitch = 1f;
        [Tooltip("全体の明るさ")] public float exposure = 1.25f;

        [Header("灯りの位置(組み立て係が入れる)")]
        public Transform fire;
        public Transform lantern;
        public Transform tent;

        [Header("ラジオの目盛りなど")]
        public bool radioLit = true;
        public bool playerLit = true;

        [Header("撮影用(負なら使わない)")]
        public float timeOverride = -1f;

        // smoothed state
        float _rain, _wet, _sleep;
        bool _snap = true;
        float _afterRainSince = -1f;
        string _lastRadioWeather;

        readonly CampPalette _pal = new CampPalette();
        CampLook _defaultLook;
        readonly Vector4[] _palArray = new Vector4[32];
        readonly Vector4[] _lightPos = new Vector4[4];
        readonly Vector4[] _lightCol = new Vector4[4];

        public float Rain => _rain;
        public float Wet => _wet;
        public float SleepAmount => _sleep;
        public float CurrentHour { get; private set; }
        public float ShaderTime { get; private set; }
        public CampLook Look => look != null ? look : (_defaultLook ??= CampLook.CreateDefault());

        public event Action<bool> SleepChanged;

        void OnEnable()
        {
            Instance = this;
            _snap = true;
            Apply(0f);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void OnValidate() => _snap = true;

        public void SetSleep(bool on)
        {
            if (sleep == on) return;
            sleep = on;
            SleepChanged?.Invoke(on);
        }

        /// <summary>Jump to the targets without fading (for screenshots and on start).</summary>
        public void Snap()
        {
            _snap = true;
            Apply(0f);
        }

        void Update()
        {
            Apply(Application.isPlaying ? Time.deltaTime : 0.1f);
        }

        void Apply(float dt)
        {
            ShaderTime = timeOverride >= 0 ? timeOverride : (Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
            CurrentHour = ResolveHour();
            ResolveWeather();

            float rainTarget = weather == CampWeather.Rain ? 1f : 0f;
            float wetTarget = weather == CampWeather.Clear ? 0f : 1f;
            float sleepTarget = sleep ? 1f : 0f;
            if (_snap) { _rain = rainTarget; _wet = wetTarget; _sleep = sleepTarget; _snap = false; }
            else
            {
                _rain = Mathf.MoveTowards(_rain, rainTarget, dt / Mathf.Max(0.01f, weatherFadeSeconds));
                _wet = Mathf.MoveTowards(_wet, wetTarget, dt / Mathf.Max(0.01f, wetTarget > _wet ? weatherFadeSeconds * 4f : weatherFadeSeconds * 2f));
                _sleep = Mathf.MoveTowards(_sleep, sleepTarget, dt / Mathf.Max(0.01f, sleepFadeSeconds));
            }

            Look.Evaluate(CurrentHour, _pal);
            ApplyRain(_pal, _rain);
            ApplySeason(_pal, ResolveSeason());
            Push(_pal);
        }

        float ResolveHour()
        {
            if (!Application.isPlaying || !followClock) return previewHour;
            DateTimeOffset now = RadioDirector.Instance != null ? RadioDirector.Instance.VirtualNow : DateTimeOffset.UtcNow;
            var local = now.ToLocalTime();
            return (float)local.TimeOfDay.TotalHours;
        }

        void ResolveWeather()
        {
            if (!Application.isPlaying) return;
            if (followRadioWeather && RadioDirector.Instance != null)
            {
                string w = RadioDirector.Instance.Weather;
                if (w != _lastRadioWeather)
                {
                    if (w == WeatherIds.Rain) weather = CampWeather.Rain;
                    else if (_lastRadioWeather == WeatherIds.Rain) { weather = CampWeather.AfterRain; _afterRainSince = Time.realtimeSinceStartup; }
                    else if (_lastRadioWeather == null) weather = CampWeather.Clear;
                    _lastRadioWeather = w;
                }
            }
            if (weather == CampWeather.AfterRain)
            {
                if (_afterRainSince < 0) _afterRainSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _afterRainSince > dryMinutes * 60f) weather = CampWeather.Clear;
            }
            else _afterRainSince = -1f;
        }

        CampSeason ResolveSeason()
        {
            if (season != CampSeason.Auto) return season;
            int m = DateTime.Now.Month;
            if (m >= 3 && m <= 5) return CampSeason.Spring;
            if (m >= 6 && m <= 8) return CampSeason.Summer;
            if (m >= 9 && m <= 11) return CampSeason.Autumn;
            return CampSeason.Winter;
        }

        void ApplyRain(CampPalette p, float r)
        {
            if (r <= 0f) return;
            var L = Look;
            Color rt = p.rainTint;
            p.horizon = Color.Lerp(p.horizon, rt, L.rainSkyMix * r);
            p.mid = Color.Lerp(p.mid, rt, L.rainSkyMix * r);
            p.zenith = Color.Lerp(p.zenith, rt, L.rainSkyMix * r);
            p.fog = Color.Lerp(p.fog, rt, L.rainFogMix * r);
            p.hill0 = Color.Lerp(p.hill0, rt, L.rainHillMix * r);
            p.hill1 = Color.Lerp(p.hill1, rt, L.rainHillMix * r);
            p.hill2 = Color.Lerp(p.hill2, rt, L.rainHillMix * r);
            float s = L.rainSilhouetteMix * r;
            p.sil0 = Color.Lerp(p.sil0, rt, s); p.sil1 = Color.Lerp(p.sil1, rt, s); p.sil2 = Color.Lerp(p.sil2, rt, s);
            p.sil3 = Color.Lerp(p.sil3, rt, s); p.sil4 = Color.Lerp(p.sil4, rt, s);
            p.water = Color.Lerp(p.water, rt, L.rainWaterMix * r);
            p.stars *= 1 - r; p.streaks *= 1 - r; p.moonDisc *= 1 - r;
            p.sunGlow *= Mathf.Lerp(1f, 0.25f, r);
            p.fogFar *= Mathf.Lerp(1f, 0.5f, r);
            p.fogNear *= Mathf.Lerp(1f, 0.7f, r);
            p.fire *= Mathf.Lerp(1f, 0.85f, r);
            p.cloudAmount = Mathf.Lerp(p.cloudAmount, 0.8f, r);
            p.hemiIntensity *= Mathf.Lerp(1f, 0.9f, r);
        }

        void ApplySeason(CampPalette p, CampSeason s)
        {
            var L = Look;
            Color g = s switch { CampSeason.Spring => L.springGrass, CampSeason.Autumn => L.autumnGrass, CampSeason.Winter => L.winterGrass, _ => L.summerGrass };
            p.grassA = Mul(p.grassA, g);
            p.grassB = Mul(p.grassB, g);
            if (s == CampSeason.Autumn) p.leaf = Mul(p.leaf, L.autumnLeaf);
            if (s == CampSeason.Winter)
            {
                p.grassA = Desaturate(p.grassA, L.winterDesaturate);
                p.grassB = Desaturate(p.grassB, L.winterDesaturate);
            }
        }

        static Color Mul(Color a, Color b) => new Color(Mathf.Clamp01(a.r * b.r), Mathf.Clamp01(a.g * b.g), Mathf.Clamp01(a.b * b.b), 1f);
        static Color Desaturate(Color c, float t)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return Color.Lerp(c, new Color(l, l, l * 1.08f), t);
        }

        static Vector4 Lin(Color c, float k = 1f)
        {
            var l = c.linear;
            return new Vector4(l.r * k, l.g * k, l.b * k, 1f);
        }

        static Vector3 FromMock(Vector3 v) => new Vector3(v.x, v.y, -v.z);

        void Push(CampPalette p)
        {
            var L = Look;
            float t = ShaderTime;
            float sl = _sleep;

            // ---- palette slots ----
            Color[] slots =
            {
                p.grassA, p.grassB, p.groundShade, p.dirt, p.dirt2, p.sand, p.rock, p.rockHi, p.tree, p.treeHi, p.leaf,
                p.sil0, p.sil1, p.sil2, p.sil3, p.sil4, p.hill0, p.hill1, p.hill2, p.water,
                L.wood, L.dark, L.crate, L.brass, L.cream, L.log, L.knob, L.stumpTop, L.fabric, L.tentCloth, L.flower, Color.magenta,
            };
            for (int i = 0; i < 32; i++) _palArray[i] = Lin(slots[i]);
            Shader.SetGlobalVectorArray("_IoriPal", _palArray);

            // ---- light ----
            float amb = p.hemiIntensity * Mathf.Lerp(1f, L.sleepAmbient, sl) * exposure;
            Shader.SetGlobalVector("_IoriHemiSky", Lin(p.hemiSky));
            var hg = Lin(p.hemiGround); hg.w = amb;
            Shader.SetGlobalVector("_IoriHemiGround", hg);
            Shader.SetGlobalVector("_IoriDirColor", Lin(p.dirColor, p.dirIntensity * Mathf.Lerp(1f, L.sleepAmbient, sl) * exposure));
            var sd = p.sunDir.normalized;
            var dirPos = new Vector3(sd.x * 50f, Mathf.Max(sd.y, 0.12f) * 50f + 4f, sd.z * 50f);
            Shader.SetGlobalVector("_IoriDirVector", FromMock(dirPos).normalized);

            // fire flicker, kept slow so the light bands do not pop
            float fl = 0.9f + 0.07f * Mathf.Sin(t * 2.3f) * Mathf.Sin(t * 1.7f + 1f) + 0.03f * Mathf.Sin(t * 5.1f);
            float fireLevel = p.fire * Mathf.Lerp(1f, L.sleepFire, sl);
            float lanternLevel = 1f - sl;
            float tentLevel = p.tent * (1f - sl * 0.7f);
            Vector3 fireP = fire ? fire.position : new Vector3(-0.48f, 0, -1.05f);
            Vector3 lanP = lantern ? lantern.position : new Vector3(-1f, 0.74f, 0.43f);
            Vector3 tentP = tent ? tent.position : new Vector3(-1.9f, 0, 5.2f);
            SetLight(0, fireP + new Vector3(0, 0.7f, 0), 6.5f, CampPalette.Hex("#ff8a3c"), 3.4f * fireLevel * fl * exposure);
            SetLight(1, fireP + new Vector3(0.5f, 0.25f, 0.6f), 4f, CampPalette.Hex("#ff6a2a"), 0.8f * fireLevel * (1.1f - 0.3f * fl) * exposure);
            SetLight(2, lanP + new Vector3(0.1f, 0.3f, -0.25f), 4f, CampPalette.Hex("#ffc070"), 1.3f * lanternLevel * (0.97f + 0.03f * Mathf.Sin(t * 3.1f)) * exposure);
            SetLight(3, tentP + new Vector3(0.3f, 0.5f, -1.7f), 6f, CampPalette.Hex("#ff9438"), 2.6f * tentLevel * exposure);
            Shader.SetGlobalVectorArray("_IoriLightPos", _lightPos);
            Shader.SetGlobalVectorArray("_IoriLightColor", _lightCol);

            float dawnish = Mathf.Clamp01(p.hemiIntensity - 1f) ; // bright looks dim the props' glow a little
            Shader.SetGlobalVector("_IoriGlowA", new Vector4(fireLevel * fl, tentLevel, lanternLevel, p.town));
            Shader.SetGlobalVector("_IoriGlowB", new Vector4(radioLit ? 1f - dawnish * 0.3f : 0f, playerLit ? 1f - dawnish * 0.3f : 0f, fireLevel * (1f - _rain) * Mathf.Clamp01(p.fire * 2f - 0.5f), Mathf.Clamp(fireLevel, 0.45f, 1.25f)));

            // ---- fog, weather, brush ----
            Shader.SetGlobalVector("_IoriFogColor", Lin(p.fog));
            Shader.SetGlobalVector("_IoriFogParams", new Vector4(p.fogNear, p.fogFar, 1f, 0f));
            Shader.SetGlobalVector("_IoriWeather", new Vector4(_rain, _wet, sl, t));
            Shader.SetGlobalVector("_IoriBrushParams", new Vector4(brushStrength, brushNear, brushFar, brushScale));
            Shader.SetGlobalVector("_IoriPaintParams", new Vector4(lightBands, bandBrush, slotSwitch, 0f));
            if (brushTexture) Shader.SetGlobalTexture("_IoriBrushTex", brushTexture);

            // ---- sky ----
            float skyDim = Mathf.Lerp(1f, 0.82f, sl);
            Shader.SetGlobalVector("_IoriSkyHorizon", Lin(p.horizon, skyDim));
            Shader.SetGlobalVector("_IoriSkyMid", Lin(p.mid, skyDim));
            Shader.SetGlobalVector("_IoriSkyZenith", Lin(p.zenith, skyDim));
            Shader.SetGlobalVector("_IoriSunDir", new Vector4(sd.x, sd.y, sd.z, 0));
            Shader.SetGlobalVector("_IoriSunColor", Lin(p.sunColor));
            Shader.SetGlobalVector("_IoriSkyParams", new Vector4(p.sunGlow, p.moonDisc, p.stars, p.streaks));
            Shader.SetGlobalVector("_IoriCloud", Lin(p.cloud, skyDim));
            Shader.SetGlobalVector("_IoriCloudDark", Lin(p.cloudDark, skyDim));
            Shader.SetGlobalVector("_IoriCloudParams", new Vector4(p.cloudAmount, _rain, 0, 0));
            if (skyMaterial && RenderSettings.skybox != skyMaterial) RenderSettings.skybox = skyMaterial;

            // ---- water, character, embers ----
            Shader.SetGlobalVector("_IoriWaterTint", Lin(p.water));
            Shader.SetGlobalVector("_IoriPuddleTint", Lin(Color.Lerp(p.groundShade, CampPalette.Hex("#0a0c2a"), 0.55f)));
            Shader.SetGlobalVector("_IoriSpriteAmbient", Lin(p.spriteAmbient, Mathf.Lerp(1f, 0.75f, sl)));
            Shader.SetGlobalVector("_IoriSpriteFire", Lin(CampPalette.Hex("#ff8a45"), 1.05f * Mathf.Min(fireLevel, 1.2f)));
            Shader.SetGlobalVector("_IoriSpriteRim", Lin(p.rim));
            Shader.SetGlobalVector("_IoriEmberColor", Lin(CampPalette.Hex("#ffb060"), 2.6f));
        }

        void SetLight(int i, Vector3 pos, float range, Color c, float intensity)
        {
            _lightPos[i] = new Vector4(pos.x, pos.y, pos.z, range);
            _lightCol[i] = Lin(c, Mathf.Max(0f, intensity));
        }
    }
}
