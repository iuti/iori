using UnityEngine;

namespace Iori.Audio
{
    /// <summary>
    /// Android 用の再生係。予定表をネイティブの再生サービス(Assets/Plugins/Android/IoriPlayback.androidlib)に渡す。
    /// Unity が裏に回って止まっても、サービスが予定表どおりに鳴らし続ける。
    /// </summary>
    public sealed class AndroidPlaybackBackend : IPlaybackBackend
    {
        const string BridgeClass = "com.iori.playback.IoriPlayback";

        public string Name => "Android ネイティブサービス";

#if UNITY_ANDROID && !UNITY_EDITOR
        readonly AndroidJavaClass _bridge = new AndroidJavaClass(BridgeClass);

        static AndroidJavaObject Activity
        {
            get
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    return player.GetStatic<AndroidJavaObject>("currentActivity");
            }
        }

        public AndroidPlaybackBackend()
        {
            // Android 13 以降は通知の許可がないと再生中の通知が出ない(再生自体は続く)。
            const string notifications = "android.permission.POST_NOTIFICATIONS";
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(notifications))
                UnityEngine.Android.Permission.RequestUserPermission(notifications);
        }

        public void ApplyPlan(PlaybackPlan plan, string planJson)
        {
            using (var activity = Activity) _bridge.CallStatic("applyPlan", activity, planJson);
        }

        public void SetMusicVolume(float volume) => _bridge.CallStatic("setMusicVolume", volume);

        public void SetAmbientVolume(string id, float volume) => _bridge.CallStatic("setAmbientVolume", id, volume);

        public void Stop()
        {
            using (var activity = Activity) _bridge.CallStatic("stop", activity);
        }

        public string Status => _bridge.CallStatic<string>("status");
#else
        public void ApplyPlan(PlaybackPlan plan, string planJson) { }
        public void SetMusicVolume(float volume) { }
        public void SetAmbientVolume(string id, float volume) { }
        public void Stop() { }
        public string Status => "Android 実機でのみ動きます";
#endif
    }
}
