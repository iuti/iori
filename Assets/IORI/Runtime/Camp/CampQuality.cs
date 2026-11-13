using UnityEngine;

namespace Iori.Camp
{
    /// <summary>Phone-friendly defaults: the scene is calm, so 30 fps on Android keeps the phone cool.</summary>
    public sealed class CampQuality : MonoBehaviour
    {
        public int androidFrameRate = 30;
        public int desktopFrameRate = 60;

        void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = androidFrameRate;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
#else
            Application.targetFrameRate = desktopFrameRate;
#endif
        }
    }
}
