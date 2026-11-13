using System;
using System.Collections.Generic;
using System.IO;

namespace Iori.Audio
{
    /// <summary>
    /// PC で、フォルダの中(下のフォルダも)の音楽ファイルを曲の一覧にする。
    /// ジャケットの絵がファイルに入っていない曲は、同じフォルダの cover / folder / front / album という名前の画像を使う。
    /// 読めないフォルダやファイルは飛ばす。時間がかかるので、画面を止めない所(別スレッド)から呼ぶ。
    /// </summary>
    public static class MusicFolder
    {
        static readonly string[] ArtNames = { "cover", "folder", "front", "album" };
        static readonly string[] ArtExtensions = { ".jpg", ".jpeg", ".png" };

        /// <param name="cancelled">true を返したら途中でやめる(それまでに見つけた分を返す)</param>
        /// <param name="maxTracks">ここまで見つけたらやめる</param>
        public static List<MusicTrack> Scan(string root, Func<bool> cancelled = null, int maxTracks = 50000)
        {
            var found = new List<MusicTrack>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return found;
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0 && found.Count < maxTracks)
            {
                if (cancelled != null && cancelled()) break;
                var dir = pending.Pop();
                string[] files, dirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    dirs = Directory.GetDirectories(dir);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                for (var i = dirs.Length - 1; i >= 0; i--) pending.Push(dirs[i]);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);

                string folderArt = null;
                var lookedForArt = false;
                foreach (var file in files)
                {
                    if (!AudioFileInfo.IsSupported(file)) continue;
                    if (found.Count >= maxTracks || (cancelled != null && cancelled())) break;
                    MusicTrack t;
                    try { t = AudioFileInfo.Read(file); }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    if (t.ArtLength == 0)
                    {
                        if (!lookedForArt)
                        {
                            lookedForArt = true;
                            folderArt = FindArt(files);
                        }
                        if (folderArt != null) t.ArtUri = folderArt;
                    }
                    found.Add(t);
                }
            }
            return found;
        }

        static string FindArt(string[] files)
        {
            foreach (var name in ArtNames)
                foreach (var file in files)
                {
                    var ext = (Path.GetExtension(file) ?? "").ToLowerInvariant();
                    if (Array.IndexOf(ArtExtensions, ext) < 0) continue;
                    if (string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase)) return file;
                }
            return null;
        }
    }
}
