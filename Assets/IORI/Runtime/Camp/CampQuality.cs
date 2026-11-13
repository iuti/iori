using UnityEngine;

namespace Iori.Camp
{
    /// <summary>
    /// Phone-friendly defaults: the scene is calm, so 30 fps on Android keeps the phone cool.
    /// In development builds it logs the frame rate every few seconds ("[IORI] perf ...") so the
    /// cost can be read from adb logcat without looking at the screen.
    /// </summary>
    public sealed class CampQuality : MonoBehaviour
    {
        public int androidFrameRate = 30;
        public int desktopFrameRate = 60;
        public bool logPerformance = true;
        public float logInterval = 5f;

        float _t;
        int _frames;
        float _worst;

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = androidFrameRate;
#else
            Application.targetFrameRate = desktopFrameRate;
#endif
        }

        void Update()
        {
            if (!logPerformance || !Debug.isDebugBuild || Application.isEditor) return;
            _t += Time.unscaledDeltaTime;
            _frames++;
            _worst = Mathf.Max(_worst, Time.unscaledDeltaTime);
            if (_t >= logInterval)
            {
                Debug.Log($"[IORI] perf fps={_frames / _t:0.0} avg_ms={_t / _frames * 1000f:0.0} worst_ms={_worst * 1000f:0.0} target={Application.targetFrameRate} screen={Screen.width}x{Screen.height}");
                _t = 0; _frames = 0; _worst = 0;
            }
        }
    }
}
