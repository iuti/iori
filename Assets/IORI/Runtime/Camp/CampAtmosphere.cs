using System;
using Iori.Audio;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Iori.Camp
{
    public enum CampWeather { Clear, Rain, AfterRain }
    public enum CampSeason { Auto, Spring, Summer, Autumn, Winter }

    /// <summary>
    /// Decides what the camp looks like right now (time of day, weather, season, sleep mode) and
    /// applies it: turns the sun or moon, sets the fire, lantern and tent lights, the light from the
    /// sky, the air, the picture's finish, and the values the camp shaders read.
    /// The lights are real Unity lights (the fire's casts shadows); the shaders take them one brush
    /// stroke at a time.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-50)]
    public sealed class CampAtmosphere : MonoBehaviour
    {
        public static CampAtmosphere Instance { get; private set; }

        [Tooltip("見た目の設定。空なら既定値")] public CampLook look;
        public Material skyMaterial;
        public Texture2D strokeTexture;
        public Texture3D noiseTexture;
        [Tooltip("山並みの輪郭(iori_ridges.png)")] public Texture2D ridgeTexture;

        [Header("時刻")]
        [Tooltip("Play 中は端末の時計(ラジオの時計のずれも反映)に合わせる。オフなら下の時刻")]
        public bool followClock = true;
        [Range(0, 24)] public float previewHour = 18.6f;
        [Tooltip("時計に合わせないとき、1 秒に何時間進めるか(0 で止める)。一日の移り変わりを見る用")]
        public float hoursPerSecond = 0f;

        [Header("天気")]
        [Tooltip("Play 中はラジオの天気に合わせる(雨がやむと雨上がりになり、少しずつ乾く)")]
        public bool followRadioWeather = true;
        public CampWeather weather = CampWeather.Clear;
        [Tooltip("雨上がりから乾ききるまでの分")] public float dryMinutes = 90f;
        [Tooltip("雨の降り始めとやみの切り替えにかける秒")] public float weatherFadeSeconds = 6f;

        [Header("季節")]
        public CampSeason season = CampSeason.Auto;

        [Header("おやすみモード")]
        public bool sleep;
        public float sleepFadeSeconds = 3f;

        [Header("シーンの部品(「シーンを組み立てる」が入れる)")]
        public Light keyLight;
        public Light fireLight;
        public Light lanternLight;
        public Light tentLight;
        public Transform fire;
        public Renderer puddle;
        [Tooltip("水たまりがいちばん満ちたときの水面の高さ")] public float puddleY = -0.03f;
        [Tooltip("地面の模様の画像が覆う範囲(左下の x, z と一辺の長さ)")] public Vector3 groundMap = new Vector3(-9f, -6.5f, 18f);
        [Tooltip("筆あとの大きさを測る基準の位置(カメラのいつもの位置)")] public Vector3 strokeOrigin = new Vector3(0f, 1.48f, -4.67f);

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
        static readonly Color MoonWhite = CampPalette.Hex("#e6ecff");
        static readonly Color LampOrange = CampPalette.Hex("#ffb060");
        static readonly Color DialAmber = CampPalette.Hex("#ffc880");
        static readonly Color PuddleDeep = CampPalette.Hex("#0a0c2a");
        CampLook _defaultLook;
        readonly Vector4[] _glow = new Vector4[8];
        Vector3 _fireLightHome;
        bool _fireHomeSet;
        bool _relight;

        // picture finish, on top of the scene's own volume
        GameObject _postGo;
        VolumeProfile _postProfile;
        ColorAdjustments _color;
        Bloom _bloom;
        DepthOfField _dof;
        float _focusDistance = 4.7f;
        float _focusClose;

        public float Rain => _rain;
        public float Wet => _wet;
        public float SleepAmount => _sleep;
        public float CurrentHour { get; private set; }
        public float ShaderTime { get; private set; }
        public CampSeason CurrentSeason { get; private set; }
        public CampPalette Palette => _pal;
        public CampLook Look => look != null ? look : (_defaultLook ??= CampLook.CreateDefault());
        /// <summary>Minutes until the ground is dry again (0 unless it has just rained).</summary>
        public float MinutesUntilDry => weather == CampWeather.AfterRain ? _wet * dryMinutes : 0f;

        public event Action<bool> SleepChanged;

        static class Id
        {
            public static readonly int Stroke = Shader.PropertyToID("_IoriStroke");
            public static readonly int StrokeOrigin = Shader.PropertyToID("_IoriStrokeOrigin");
            public static readonly int StrokeTex = Shader.PropertyToID("_IoriStrokeTex");
            public static readonly int Noise3D = Shader.PropertyToID("_IoriNoise3D");
            public static readonly int RidgeTex = Shader.PropertyToID("_IoriRidgeTex");
            public static readonly int TentLamp = Shader.PropertyToID("_IoriTentLamp");
            public static readonly int AirFire = Shader.PropertyToID("_IoriAirFire");
            public static readonly int AirTent = Shader.PropertyToID("_IoriAirTent");
            public static readonly int AmbientSky = Shader.PropertyToID("_IoriAmbientSky");
            public static readonly int AmbientEquator = Shader.PropertyToID("_IoriAmbientEquator");
            public static readonly int AmbientGround = Shader.PropertyToID("_IoriAmbientGround");
            public static readonly int FogColor = Shader.PropertyToID("_IoriFogColor");
            public static readonly int MistColor = Shader.PropertyToID("_IoriMistColor");
            public static readonly int Fog = Shader.PropertyToID("_IoriFog");
            public static readonly int Weather = Shader.PropertyToID("_IoriWeather");
            public static readonly int Wind = Shader.PropertyToID("_IoriWind");
            public static readonly int Glow = Shader.PropertyToID("_IoriGlow");
            public static readonly int GroundMap = Shader.PropertyToID("_IoriGroundMap");
            public static readonly int FirePos = Shader.PropertyToID("_IoriFirePos");
            public static readonly int Light = Shader.PropertyToID("_IoriLight");
            public static readonly int Lamp = Shader.PropertyToID("_IoriLamp");
            public static readonly int Fire = Shader.PropertyToID("_IoriFire");
            public static readonly int SeasonGrass = Shader.PropertyToID("_IoriSeasonGrass");
            public static readonly int SeasonLeaf = Shader.PropertyToID("_IoriSeasonLeaf");
            public static readonly int SkyHorizon = Shader.PropertyToID("_IoriSkyHorizon");
            public static readonly int SkyMid = Shader.PropertyToID("_IoriSkyMid");
            public static readonly int SkyZenith = Shader.PropertyToID("_IoriSkyZenith");
            public static readonly int SunDir = Shader.PropertyToID("_IoriSunDir");
            public static readonly int MoonDir = Shader.PropertyToID("_IoriMoonDir");
            public static readonly int SunColor = Shader.PropertyToID("_IoriSunColor");
            public static readonly int MoonColor = Shader.PropertyToID("_IoriMoonColor");
            public static readonly int SkyParams = Shader.PropertyToID("_IoriSkyParams");
            public static readonly int Cloud = Shader.PropertyToID("_IoriCloud");
            public static readonly int CloudDark = Shader.PropertyToID("_IoriCloudDark");
            public static readonly int CloudParams = Shader.PropertyToID("_IoriCloudParams");
            public static readonly int FarNear = Shader.PropertyToID("_IoriFarNear");
            public static readonly int FarFar = Shader.PropertyToID("_IoriFarFar");
            public static readonly int Town = Shader.PropertyToID("_IoriTown");
            public static readonly int SkyStroke = Shader.PropertyToID("_IoriSkyStroke");
            public static readonly int WaterTint = Shader.PropertyToID("_IoriWaterTint");
            public static readonly int PuddleTint = Shader.PropertyToID("_IoriPuddleTint");
        }

        void OnEnable()
        {
            Instance = this;
            _snap = true;
            Apply(0f);
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_postGo) DestroyImmediate(_postGo);
            if (_postProfile) DestroyImmediate(_postProfile);
            _postGo = null; _postProfile = null; _color = null; _bloom = null; _dof = null;
        }

        void OnValidate() => _snap = true;

        public void SetSleep(bool on)
        {
            if (sleep == on) return;
            sleep = on;
            SleepChanged?.Invoke(on);
        }

        /// <summary>Sets the weather by hand (the panel). The radio's rain sound follows, and the radio does not undo it.</summary>
        public void SetWeatherByHand(CampWeather w)
        {
            weather = w;
            _afterRainSince = -1f;
            var radio = RadioDirector.Instance;
            if (radio != null)
            {
                string id = w == CampWeather.Rain ? WeatherIds.Rain : WeatherIds.Clear;
                if (radio.Weather != id) radio.SetWeather(id);
                _lastRadioWeather = id;
            }
        }

        /// <summary>Back to following the clock, the radio's weather and the calendar.</summary>
        public void FollowEverything()
        {
            followClock = true;
            followRadioWeather = true;
            hoursPerSecond = 0f;
            season = CampSeason.Auto;
            SetSleep(false);
            var radio = RadioDirector.Instance;
            if (radio != null) { weather = radio.Weather == WeatherIds.Rain ? CampWeather.Rain : CampWeather.Clear; _lastRadioWeather = radio.Weather; }
            else weather = CampWeather.Clear;
        }

        /// <summary>True while time, weather and season all follow the real world.</summary>
        public bool FollowingEverything => followClock && followRadioWeather && season == CampSeason.Auto && !sleep;

        /// <summary>Jump to the targets without fading (for screenshots and on start).</summary>
        public void Snap()
        {
            _snap = true;
            Apply(0f);
        }

        /// <summary>The camera tells where it is looking: distance to what should be sharp, and how close-up the shot is (0..1).</summary>
        public void SetFocus(float distance, float closeUp)
        {
            _focusDistance = Mathf.Max(0.3f, distance);
            _focusClose = Mathf.Clamp01(closeUp);
        }

        void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0.1f;
            if (Application.isPlaying && !followClock && hoursPerSecond != 0f)
                previewHour = Mathf.Repeat(previewHour + hoursPerSecond * Time.deltaTime, 24f);
            Apply(dt);
        }

        void Apply(float dt)
        {
            ShaderTime = timeOverride >= 0 ? timeOverride : (Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
            CurrentHour = ResolveHour();
            ResolveWeather();
            CurrentSeason = ResolveSeason();

            float rainTarget = weather == CampWeather.Rain ? 1f : 0f;
            float wetTarget = WetTarget();
            float sleepTarget = sleep ? 1f : 0f;
            if (_snap) { _rain = rainTarget; _wet = wetTarget; _sleep = sleepTarget; _snap = false; }
            else
            {
                _rain = Mathf.MoveTowards(_rain, rainTarget, dt / Mathf.Max(0.01f, weatherFadeSeconds));
                // puddles fill over a few moments of rain and go away slowly
                _wet = Mathf.MoveTowards(_wet, wetTarget, dt / Mathf.Max(0.01f, weatherFadeSeconds * 4f));
                _sleep = Mathf.MoveTowards(_sleep, sleepTarget, dt / Mathf.Max(0.01f, sleepFadeSeconds));
            }

            Look.Evaluate(CurrentHour, _pal);
            Push(_pal);
        }

        float ResolveHour()
        {
            if (!Application.isPlaying || !followClock) return previewHour;
            DateTimeOffset now = RadioDirector.Instance != null ? RadioDirector.Instance.VirtualNow : DateTimeOffset.UtcNow;
            return (float)now.ToLocalTime().TimeOfDay.TotalHours;
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
                    else if (_lastRadioWeather == WeatherIds.Rain) weather = CampWeather.AfterRain;
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

        float WetTarget()
        {
            if (weather == CampWeather.Rain) return 1f;
            if (weather == CampWeather.Clear) return 0f;
            if (!Application.isPlaying || _afterRainSince < 0) return 1f;
            return Mathf.Clamp01(1f - (Time.realtimeSinceStartup - _afterRainSince) / Mathf.Max(1f, dryMinutes * 60f));
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

        static Vector4 Lin(Color c, float k = 1f)
        {
            var l = c.linear;
            return new Vector4(l.r * k, l.g * k, l.b * k, 1f);
        }

        /// <summary>Direction toward a point in the sky. Azimuth 0 looks across the lake (+z), positive turns right.</summary>
        public static Vector3 SkyDirection(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad, el = elevationDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Cos(az) * Mathf.Cos(el));
        }

        /// <summary>The moon drifts slowly across the sky over the lake during the night: right to left, always inside
        /// the upright picture (which shows about ten degrees to either side), above the mountains.</summary>
        public static Vector3 MoonDirection(float hour)
        {
            float n = Mathf.Repeat(hour - 18f, 24f) / 11.5f;
            n = Mathf.Clamp01(n);
            return SkyDirection(Mathf.Lerp(6.5f, -4.5f, n), 8.6f + 2f * Mathf.Sin(n * Mathf.PI));
        }

        /// <summary>
        /// A light's strength is meant in linear light here (twice the number, twice the light). The render
        /// pipeline switches that on whenever it draws a frame the usual way, but not when a script asks
        /// it for a single picture, as the batch screenshots do; without it a lamp of strength 8 came out
        /// about a hundred times as bright as a lamp of strength 1. So it is made sure of here.
        /// </summary>
        void MakeSureOfLinearLights()
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear || GraphicsSettings.lightsUseLinearIntensity) return;
            GraphicsSettings.lightsUseLinearIntensity = true;
            _relight = true;
        }

        /// <summary>Makes a light work its color out again (after the rule above changed).</summary>
        static void Relight(Light l)
        {
            if (!l) return;
            var c = l.color;
            l.color = Color.black;
            l.color = c;
        }

        void Push(CampPalette p)
        {
            var L = Look;
            float t = ShaderTime;
            float sl = _sleep, rain = _rain;
            MakeSureOfLinearLights();

            // ---- sky and air, with the rain mixed in
            Color rt = p.rainTint;
            float skyMix = L.rainSkyMix * rain;
            Color horizon = Color.Lerp(p.horizon, rt, skyMix), mid = Color.Lerp(p.mid, rt, skyMix), zenith = Color.Lerp(p.zenith, rt * 0.8f, skyMix);
            Color fog = Color.Lerp(p.fog, rt, skyMix * 0.8f), mist = Color.Lerp(p.mist, rt, skyMix * 0.6f);
            float skyDim = Mathf.Lerp(1f, 0.85f, sl);
            float clearSky = 1f - rain;

            Vector3 sunDir = SkyDirection(p.sunAzimuth, p.sunElevation);
            Vector3 moonDir = MoonDirection(CurrentHour);
            Shader.SetGlobalVector(Id.SkyHorizon, Lin(horizon, skyDim));
            Shader.SetGlobalVector(Id.SkyMid, Lin(mid, skyDim));
            Shader.SetGlobalVector(Id.SkyZenith, Lin(zenith, skyDim));
            Shader.SetGlobalVector(Id.SunDir, sunDir);
            Shader.SetGlobalVector(Id.MoonDir, moonDir);
            Shader.SetGlobalVector(Id.SunColor, Lin(p.sunGlowColor));
            Shader.SetGlobalVector(Id.MoonColor, Lin(MoonWhite));
            Shader.SetGlobalVector(Id.SkyParams, new Vector4(p.sunGlow * Mathf.Lerp(1f, 0.2f, rain), p.moon * clearSky, p.stars * clearSky, p.sunDisc * clearSky));
            Shader.SetGlobalVector(Id.Cloud, Lin(Color.Lerp(p.cloud, rt * 1.15f, skyMix), skyDim));
            Shader.SetGlobalVector(Id.CloudDark, Lin(Color.Lerp(p.cloudDark, rt * 0.7f, skyMix), skyDim));
            Shader.SetGlobalVector(Id.CloudParams, new Vector4(p.cloudAmount, rain, 0.004f * (1f + rain * 3f), 0f));
            Shader.SetGlobalVector(Id.FarNear, Lin(Color.Lerp(p.farNear, rt * 0.6f, skyMix * 0.5f), skyDim));
            Shader.SetGlobalVector(Id.FarFar, Lin(Color.Lerp(p.farFar, rt, skyMix * 0.7f), skyDim));
            Shader.SetGlobalVector(Id.Town, Lin(LampOrange, p.town * L.townGlow * Mathf.Lerp(1f, 0.6f, rain)));
            Shader.SetGlobalVector(Id.SkyStroke, new Vector4(L.skyStrokeTiles, L.skyStrokeAmount, 0f, 0f));
            if (skyMaterial && RenderSettings.skybox != skyMaterial) RenderSettings.skybox = skyMaterial;

            Shader.SetGlobalVector(Id.FogColor, Lin(fog, skyDim));
            Shader.SetGlobalVector(Id.MistColor, Lin(mist, skyDim));
            Shader.SetGlobalVector(Id.Fog, new Vector4(p.fogDensity * Mathf.Lerp(1f, L.rainFog, rain), p.mistDensity + L.rainMist * rain, 1.2f, 0.97f));

            // ---- light from the sky
            float amb = p.ambientIntensity * Mathf.Lerp(1f, L.sleepAmbient, sl) * Mathf.Lerp(1f, L.rainAmbient, rain);
            Color aSky = Color.Lerp(p.ambientSky, rt, skyMix * 0.6f), aEq = Color.Lerp(p.ambientEquator, rt, skyMix * 0.6f), aGr = Color.Lerp(p.ambientGround, rt * 0.6f, skyMix * 0.4f);
            Shader.SetGlobalVector(Id.AmbientSky, Lin(aSky, amb));
            Shader.SetGlobalVector(Id.AmbientEquator, Lin(aEq, amb));
            Shader.SetGlobalVector(Id.AmbientGround, Lin(aGr, amb));
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = aSky * amb;
            RenderSettings.ambientEquatorColor = aEq * amb;
            RenderSettings.ambientGroundColor = aGr * amb;
            RenderSettings.fog = false;

            // ---- the sun, its afterglow, or the moon. The light fades through zero where it changes hands.
            if (keyLight)
            {
                bool moonLit = p.moon > 0.5f;
                Vector3 toLight = moonLit ? moonDir : SkyDirection(p.sunAzimuth, Mathf.Max(p.sunElevation, 5f));
                keyLight.transform.rotation = Quaternion.LookRotation(-toLight, Vector3.up);
                keyLight.color = p.keyColor;
                keyLight.intensity = p.keyIntensity * Mathf.Abs(2f * p.moon - 1f) * Mathf.Lerp(1f, L.rainKey, rain) * Mathf.Lerp(1f, L.sleepAmbient, sl);
                keyLight.shadowStrength = p.keyShadow * Mathf.Lerp(1f, 0.4f, rain);
            }

            // ---- the fire: it breathes, and its light wanders a little so the shadows move
            float flick = 0.9f + 0.07f * Mathf.Sin(t * 2.3f) * Mathf.Sin(t * 1.7f + 1f) + 0.035f * Mathf.Sin(t * 5.1f) + 0.02f * Mathf.Sin(t * 11.3f + 2f);
            float fireLevel = p.fire * Mathf.Lerp(1f, L.sleepFire, sl) * Mathf.Lerp(1f, L.rainFire, rain);
            float lanternLevel = p.lantern * (1f - sl);
            float tentLevel = p.tent * Mathf.Lerp(1f, L.sleepTent, sl);
            Vector3 firePos = fire ? fire.position + new Vector3(0f, 0.32f, 0f) : new Vector3(-0.22f, 0.3f, -0.85f);
            if (fireLight)
            {
                if (!_fireHomeSet) { _fireLightHome = fireLight.transform.position; _fireHomeSet = true; }
                if (fire) _fireLightHome = firePos;
                fireLight.transform.position = _fireLightHome + new Vector3(Mathf.Sin(t * 3.1f) * 0.02f, Mathf.Sin(t * 4.3f + 1f) * 0.025f, Mathf.Cos(t * 2.7f) * 0.02f);
                fireLight.color = L.fireColor;
                fireLight.intensity = L.fireIntensity * fireLevel * flick;
                fireLight.range = L.fireRange;
                fireLight.enabled = fireLevel > 0.01f;
            }
            if (lanternLight)
            {
                lanternLight.color = L.lanternColor;
                lanternLight.intensity = L.lanternIntensity * lanternLevel * (0.97f + 0.03f * Mathf.Sin(t * 3.1f));
                lanternLight.range = L.lanternRange;
                lanternLight.enabled = lanternLevel > 0.01f;
            }
            if (tentLight)
            {
                tentLight.color = L.tentColor;
                tentLight.intensity = L.tentIntensity * tentLevel;
                tentLight.range = L.tentRange;
                tentLight.enabled = tentLevel > 0.01f;
            }
            if (_relight)
            {
                Relight(keyLight); Relight(fireLight); Relight(lanternLight); Relight(tentLight);
                _relight = false;
            }
            Shader.SetGlobalVector(Id.FirePos, new Vector4(firePos.x, firePos.y, firePos.z, L.fireRange));
            Shader.SetGlobalVector(Id.Light, new Vector4(L.lampSize * L.lampSize, L.litKnee, L.litTop, L.lampGlaze));
            Shader.SetGlobalVector(Id.Lamp, new Vector4(L.lampWins, L.lampKnee, L.lampTop, 0f));
            Vector3 tentLamp = tentLight ? tentLight.transform.position : new Vector3(-2.2f, 0.6f, 1.2f);
            Shader.SetGlobalVector(Id.TentLamp, new Vector4(tentLamp.x, tentLamp.y, tentLamp.z, tentLevel * L.tentGlow));
            // the air around the lamps catches their light: more of it in mist and in rain
            float airCatch = L.lampAir * (1f + rain * 1.5f + Mathf.Clamp01(p.mistDensity * 40f));
            Shader.SetGlobalVector(Id.AirFire, Lin(L.fireColor, L.fireIntensity * fireLevel * flick * airCatch));
            Shader.SetGlobalVector(Id.AirTent, Lin(L.tentColor, L.tentIntensity * tentLevel * airCatch * 0.3f));
            // flame size: a low flame over embers when sleeping, never quite out
            float flameSize = Mathf.Clamp(0.35f + 0.65f * fireLevel, 0.3f, 1.2f) * Mathf.Lerp(1f, 0.55f, sl);
            Shader.SetGlobalVector(Id.Fire, new Vector4(flameSize, Mathf.Lerp(0.85f, 1.1f, Mathf.Clamp01(fireLevel)) * flick, Mathf.Clamp01(fireLevel * (1f - sl * 0.7f)), 0f));

            // ---- things that glow by themselves (channels as in campkit.py: 1 fire, 2 tent, 3 lantern, 4 town, 5 radio, 6 player, 7 embers)
            float lampDim = Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(p.keyIntensity - 1f));
            _glow[0] = Vector4.zero;
            _glow[1] = Lin(L.fireColor, fireLevel * flick);
            // the tent's cloth is drawn by IoriLit's "lit from inside" (see _IoriTentLamp); this is for anything
            // else on that channel, glowing in its own color: w = 1
            float tg = L.tentGlow * tentLevel;
            _glow[2] = new Vector4(tg, tg, tg, 1f);
            _glow[3] = Lin(L.lanternColor, L.lanternGlow * lanternLevel);
            _glow[4] = Lin(LampOrange, p.town * L.townGlow);
            _glow[5] = Lin(DialAmber, radioLit ? L.dialGlow * lampDim : 0f);
            _glow[6] = Lin(LampOrange, playerLit ? L.dialGlow * lampDim : 0f);
            _glow[7] = new Vector4(1f, 1f, 1f, 1f) * (L.emberGlow * Mathf.Clamp(fireLevel * 1.2f, 0.5f, 1.3f) * (0.92f + 0.08f * flick));
            for (int i = 0; i < _glow.Length; i++) if (i != 2) _glow[i].w = 0f;
            Shader.SetGlobalVectorArray(Id.Glow, _glow);

            // ---- brush, weather, wind, ground
            Shader.SetGlobalVector(Id.Stroke, new Vector4(L.strokeTiles, L.strokeNear, L.strokeAmount, L.lightBands));
            Shader.SetGlobalVector(Id.StrokeOrigin, strokeOrigin);
            if (strokeTexture) Shader.SetGlobalTexture(Id.StrokeTex, strokeTexture);
            if (noiseTexture) Shader.SetGlobalTexture(Id.Noise3D, noiseTexture);
            if (ridgeTexture) Shader.SetGlobalTexture(Id.RidgeTex, ridgeTexture);
            Shader.SetGlobalVector(Id.Weather, new Vector4(rain, _wet, sl, t));
            Vector2 wd = L.windDirection.sqrMagnitude > 1e-6f ? L.windDirection.normalized : Vector2.right;
            Shader.SetGlobalVector(Id.Wind, new Vector4(wd.x, wd.y, Mathf.Lerp(L.wind, L.rainWind, rain), 0f));
            float puddleNow = Mathf.Lerp(puddleY - 0.1f, puddleY, Mathf.SmoothStep(0f, 1f, _wet));
            Shader.SetGlobalVector(Id.GroundMap, new Vector4(groundMap.x, groundMap.y, 1f / Mathf.Max(0.01f, groundMap.z), puddleNow));
            if (puddle)
            {
                var pt = puddle.transform;
                var pp = pt.position;
                if (!Mathf.Approximately(pp.y, puddleNow)) { pp.y = puddleNow; pt.position = pp; }
                bool show = _wet > 0.01f;
                if (puddle.enabled != show) puddle.enabled = show;
            }

            // ---- season
            Color sg, sf;
            switch (CurrentSeason)
            {
                case CampSeason.Spring: sg = L.springGrass; sf = L.springLeaf; break;
                case CampSeason.Autumn: sg = L.autumnGrass; sf = L.autumnLeaf; break;
                case CampSeason.Winter: sg = L.winterGrass; sf = L.winterLeaf; break;
                default: sg = L.summerGrass; sf = L.summerLeaf; break;
            }
            Shader.SetGlobalVector(Id.SeasonGrass, new Vector4(sg.r, sg.g, sg.b, 1f));
            Shader.SetGlobalVector(Id.SeasonLeaf, new Vector4(sf.r, sf.g, sf.b, 1f));

            // ---- water
            Shader.SetGlobalVector(Id.WaterTint, Lin(Color.Lerp(p.water, rt, skyMix * 0.6f), skyDim * 0.55f));
            Shader.SetGlobalVector(Id.PuddleTint, Lin(Color.Lerp(p.ambientGround, PuddleDeep, 0.5f), 0.5f));

            PushFinish(p, L, sl);
        }

        void PushFinish(CampPalette p, CampLook L, float sl)
        {
            if (!_postGo)
            {
                _postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _postProfile.hideFlags = HideFlags.HideAndDontSave;
                _color = _postProfile.Add<ColorAdjustments>(true);
                _bloom = _postProfile.Add<Bloom>(true);
                _dof = _postProfile.Add<DepthOfField>(true);
                _postGo = new GameObject("Camp finish (自動生成)") { hideFlags = HideFlags.HideAndDontSave };
                var v = _postGo.AddComponent<Volume>();
                v.isGlobal = true;
                v.priority = 10f;
                v.sharedProfile = _postProfile;
            }
            _color.postExposure.Override(p.exposure + L.sleepExposure * sl);
            _color.saturation.Override(p.saturation);
            _color.contrast.Override(p.contrast);
            _color.colorFilter.Override(p.filter);
            _bloom.intensity.Override(p.bloom);
            _bloom.threshold.Override(1.0f);
            _bloom.scatter.Override(0.6f);
            _dof.mode.Override(CampQuality.DepthOfFieldOn ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off);
            _dof.focusDistance.Override(_focusDistance);
            _dof.focalLength.Override(L.focalLength);
            _dof.aperture.Override(Mathf.Lerp(L.aperture, L.apertureClose, _focusClose));
            _dof.bladeCount.Override(6);
        }
    }
}
