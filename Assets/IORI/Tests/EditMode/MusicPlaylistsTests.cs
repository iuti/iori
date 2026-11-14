using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class MusicPlaylistsTests
    {
        static MusicTrack T(string id, string title, string artist = "a", string album = "b") =>
            new MusicTrack { Id = id, Uri = id, Title = title, Artist = artist, Album = album };

        static MusicLibrary Lib(params MusicTrack[] t) => new MusicLibrary(t);

        static string[] Ids(MusicPlaylists p, MusicPlaylist l, MusicLibrary lib) => p.Resolve(l, lib).Select(t => t.Id).ToArray();

        [Test]
        public void Create_PutsNewListsOnTop_WithNumberedNames()
        {
            var p = new MusicPlaylists();
            var a = p.Create();
            var b = p.Create("  夜  ");
            var c = p.Create("");
            Assert.That(p.Lists.Select(l => l.Name), Is.EqualTo(new[] { "リスト 2", "夜", "リスト 1" }));
            Assert.That(new[] { a.Id, b.Id, c.Id }.Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public void Add_KeepsEachSongOnce_AndToggleRemoves()
        {
            var lib = Lib(T("1", "x"), T("2", "y"));
            var p = new MusicPlaylists();
            var l = p.Create("l");
            Assert.That(p.Add(l, lib.Find("1")), Is.True);
            Assert.That(p.Add(l, lib.Find("1")), Is.False);
            Assert.That(p.Toggle(l, lib.Find("2")), Is.True);
            Assert.That(Ids(p, l, lib), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(p.Toggle(l, lib.Find("1")), Is.False);
            Assert.That(Ids(p, l, lib), Is.EqualTo(new[] { "2" }));
        }

        [Test]
        public void Rename_RefusesEmptyNames()
        {
            var p = new MusicPlaylists();
            var l = p.Create("a");
            Assert.That(p.Rename(l, "  "), Is.False);
            Assert.That(l.Name, Is.EqualTo("a"));
            Assert.That(p.Rename(l, "b\n"), Is.True);
            Assert.That(l.Name, Is.EqualTo("b"));
        }

        [Test]
        public void Resolve_SkipsMissingSongs_AndFindsMovedOnesByName()
        {
            var p = new MusicPlaylists();
            var l = p.Create("l");
            var before = Lib(T("1", "x"), T("2", "y"), T("3", "z"));
            p.Add(l, before.Find("1"));
            p.Add(l, before.Find("2"));
            p.Add(l, before.Find("3"));
            // 2 は無くなり、3 は別の Id で見つかる(名前のカタカナと全角も違う)
            var after = Lib(T("1", "x"), T("9", "Ｚ", "A", "B"));
            Assert.That(Ids(p, l, after), Is.EqualTo(new[] { "1", "9" }));
            Assert.That(p.Contains(l, after.Find("9")), Is.True);
            // 戻ってくれば、また見える
            Assert.That(Ids(p, l, before), Is.EqualTo(new[] { "1", "2", "3" }));
        }

        [Test]
        public void Move_ChangesTheOrderOfTheShownSongs_AroundMissingOnes()
        {
            var all = Lib(T("1", "a"), T("2", "b"), T("3", "c"), T("4", "d"));
            var p = new MusicPlaylists();
            var l = p.Create("l");
            foreach (var id in new[] { "1", "2", "3", "4" }) p.Add(l, all.Find(id));
            Assert.That(p.Move(l, all, 0, 2), Is.True);
            Assert.That(Ids(p, l, all), Is.EqualTo(new[] { "2", "3", "1", "4" }));
            Assert.That(p.Move(l, all, 3, 0), Is.True);
            Assert.That(Ids(p, l, all), Is.EqualTo(new[] { "4", "2", "3", "1" }));
            // 2 が見えないとき: 見えている 4, 3, 1 の中で動かす
            var some = Lib(T("1", "a"), T("3", "c"), T("4", "d"));
            Assert.That(p.Move(l, some, 0, 1), Is.True);
            Assert.That(Ids(p, l, some), Is.EqualTo(new[] { "3", "4", "1" }));
            Assert.That(Ids(p, l, all), Is.EqualTo(new[] { "2", "3", "4", "1" }));
            Assert.That(p.Move(l, some, 0, 5), Is.False);
            Assert.That(p.RemoveAt(l, some, 2), Is.True);
            Assert.That(Ids(p, l, all), Is.EqualTo(new[] { "2", "3", "4" }));
        }

        [Test]
        public void SaveAndLoad_KeepNamesAndSongs()
        {
            var lib = Lib(T("1", "x"), T("2", "y"));
            var p = new MusicPlaylists();
            var a = p.Create("朝 \"の\" 曲");
            p.Add(a, lib.Find("2"));
            p.Add(a, lib.Find("1"));
            p.Create("夜");
            var q = new MusicPlaylists();
            Assert.That(q.Load(p.Save()), Is.True);
            Assert.That(q.Lists.Select(l => l.Name), Is.EqualTo(new[] { "夜", "朝 \"の\" 曲" }));
            Assert.That(Ids(q, q.Lists[1], lib), Is.EqualTo(new[] { "2", "1" }));
            // 読んだあとに作るリストの Id は重ならない
            var c = q.Create();
            Assert.That(q.Lists.Count(l => l.Id == c.Id), Is.EqualTo(1));
            Assert.That(q.Load("{ broken"), Is.False);
            Assert.That(q.Count, Is.EqualTo(3));
        }

        [Test]
        public void Version_GrowsOnEveryChange()
        {
            var p = new MusicPlaylists();
            int v = p.Version;
            var l = p.Create();
            Assert.That(p.Version, Is.GreaterThan(v));
            v = p.Version;
            p.Rename(l, l.Name);
            Assert.That(p.Version, Is.EqualTo(v));
            p.Delete(l);
            Assert.That(p.Version, Is.GreaterThan(v));
        }
    }
}
