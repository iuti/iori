using System;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

namespace Iori.Audio
{
    /// <summary>
    /// API キーのような秘密の文字の置き場所。端末の中だけに、その端末(とそのユーザー)でしか読めない形で置く。
    /// - Android: Keystore の鍵で暗号化(SecretBox.java)。
    /// - Windows: DPAPI(ログインしているユーザーでしか戻せない)。
    /// - それ以外: ファイルには書かず、動いている間だけ覚える。
    /// 暗号化した物は persistentDataPath/secrets/ に置く。キーをログに書かないこと。
    /// </summary>
    public static class SecretStore
    {
        static readonly System.Collections.Generic.Dictionary<string, string> Cache = new System.Collections.Generic.Dictionary<string, string>();

        static string PathOf(string name) => Path.Combine(Application.persistentDataPath, "secrets", name + ".bin");

        /// <summary>しまってある文字(無ければ空)。</summary>
        public static string Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;
            string value = "";
            try
            {
                var path = PathOf(name);
                if (File.Exists(path)) value = Open(File.ReadAllText(path)) ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IORI] しまってあるキーが読めません(" + name + "): " + e.GetType().Name);
            }
            Cache[name] = value;
            return value;
        }

        /// <summary>しまう。空を渡すと消す。ファイルに置けたら true(置けない端末では、動いている間だけ覚える)。</summary>
        public static bool Set(string name, string value)
        {
            value = (value ?? "").Trim();
            Cache[name] = value;
            try
            {
                var path = PathOf(name);
                if (value.Length == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return true;
                }
                var sealedText = Seal(value);
                if (sealedText == null) return false;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sealedText);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[IORI] キーをしまえません(" + name + "): " + e.GetType().Name);
                return false;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static string Seal(string plain)
        {
            using (var box = new AndroidJavaClass("com.iori.playback.SecretBox"))
                return box.CallStatic<string>("seal", plain);
        }

        static string Open(string sealedText)
        {
            using (var box = new AndroidJavaClass("com.iori.playback.SecretBox"))
                return box.CallStatic<string>("open", sealedText.Trim());
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct Blob
        {
            public int size;
            public IntPtr data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);

        [DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr memory);

        const int NoUi = 0x1;

        static byte[] Dpapi(byte[] input, bool protect)
        {
            var inBlob = new Blob { size = input.Length, data = Marshal.AllocHGlobal(Math.Max(1, input.Length)) };
            var outBlob = new Blob();
            try
            {
                Marshal.Copy(input, 0, inBlob.data, input.Length);
                bool ok = protect
                    ? CryptProtectData(ref inBlob, "IORI", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out outBlob)
                    : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, NoUi, out outBlob);
                if (!ok || outBlob.data == IntPtr.Zero) return null;
                var result = new byte[outBlob.size];
                Marshal.Copy(outBlob.data, result, 0, outBlob.size);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(inBlob.data);
                if (outBlob.data != IntPtr.Zero) LocalFree(outBlob.data);
            }
        }

        static string Seal(string plain)
        {
            var bytes = Dpapi(Encoding.UTF8.GetBytes(plain), true);
            return bytes == null ? null : Convert.ToBase64String(bytes);
        }

        static string Open(string sealedText)
        {
            var bytes = Dpapi(Convert.FromBase64String(sealedText.Trim()), false);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }
#else
        static string Seal(string plain) => null;
        static string Open(string sealedText) => null;
#endif
    }
}
