using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class MusicLibraryTests
    {
        static MusicTrack T(string id, string title, string artist, string album, int number = 0, int disc = 0, string albumArtist = "") =>
            new MusicTrack { Id = id, Uri = id, Title = title, Artist = artist, Album = album, Number = number, Disc = disc, AlbumArtist = albumArtist };

        static MusicLibrary Library() => new MusicLibrary(new List<MusicTrack>
        {
            T("3", "朝のコーヒーは二杯", "小舟ラジオ楽団", "港の朝", 2),
            T("1", "banana", "Trio", "Fruits", 2),
            T("2", "Apple", "trio", "Fruits", 1),
            T("4", "雨やどりのワルツ", "小舟ラジオ楽団", "港の朝", 1),
            T("5", "cherry", "Guest", "Fruits", 1, 2, "Trio"),
            T("6", "タグなし", "", ""),
            T("1", "同じ名前の曲は 1 つだけ", "x", "y"),
            null,
        });

        [Test]
        public void Songs_AreSortedByTitle_IgnoringCase_AndDuplicatesAreDropped()
        {
            var lib = Library();
            Assert.That(lib.Count, Is.EqualTo(6));
            Assert.That(lib.Songs.Select(s => s.Title).Take(3), Is.EqualTo(new[] { "Apple", "banana", "cherry" }));
            Assert.That(lib.Find("4").Title, Is.EqualTo("雨やどりのワルツ"));
            Assert.That(lib.Find("nope"), Is.Null);
        }

        [Test]
        public void Albums_GroupByAlbumArtistAndTitle_AndOrderByDiscThenNumber()
        {
            var lib = Library();
            // 演奏者の大文字と小文字の違い、アルバム全体の演奏者が書いてあるゲストの曲も、同じアルバムにまとまる
            var fruits = lib.Albums.Single(a => a.Title == "Fruits");
            Assert.That(fruits.Tracks.Select(t => t.Id), Is.EqualTo(new[] { "2", "1", "5" }));
            var harbor = lib.Albums.Single(a => a.Title == "港の朝");
            Assert.That(harbor.Artist, Is.EqualTo("小舟ラジオ楽団"));
            Assert.That(harbor.Tracks.Select(t => t.Id), Is.EqualTo(new[] { "4", "3" }));
            Assert.That(lib.Albums.Any(a => a.Title == ""), Is.False, "アルバム名の無い曲はアルバムの一覧に出さない");
        }

        [Test]
        public void Album_WithAlbumArtist_KeepsGuestTracksTogether()
        {
            var lib = new MusicLibrary(new[]
            {
                T("a", "one", "Trio", "Fruits", 1, 1, "Trio"),
                T("b", "two", "Guest", "Fruits", 2, 1, "Trio"),
                T("c", "three", "Trio", "Fruits", 1, 2, "Trio"),
            });
            Assert.That(lib.Albums.Count, Is.EqualTo(1));
            Assert.That(lib.Albums[0].Artist, Is.EqualTo("Trio"));
            Assert.That(lib.Albums[0].Tracks.Select(t => t.Id), Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void Artists_MergeCase_AndOrderTracksByAlbumThenNumber()
        {
            var lib = Library();
            var trio = lib.Artists.Single(a => a.Name.ToLowerInvariant() == "trio");
            Assert.That(trio.Tracks.Select(t => t.Id), Is.EqualTo(new[] { "2", "1" }));
            Assert.That(lib.Artists.Any(a => a.Name == ""), Is.False);
        }

        [Test]
        public void Search_LooksInTitleArtistAndAlbum()
        {
            var lib = Library();
            Assert.That(lib.Search("ワルツ").Select(t => t.Id), Is.EqualTo(new[] { "4" }));
            Assert.That(lib.Search("FRUITS").Count, Is.EqualTo(3));
            Assert.That(lib.Search("小舟").Count, Is.EqualTo(2));
            Assert.That(lib.Search("  ").Count, Is.EqualTo(6));
            Assert.That(lib.Search("zzz"), Is.Empty);
        }

        [Test]
        public void FormatTime_ShowsMinutesAndHours()
        {
            Assert.That(MusicLibrary.FormatTime(0), Is.EqualTo("0:00"));
            Assert.That(MusicLibrary.FormatTime(228_900), Is.EqualTo("3:48"));
            Assert.That(MusicLibrary.FormatTime(3_727_000), Is.EqualTo("1:02:07"));
            Assert.That(MusicLibrary.FormatTime(-5), Is.EqualTo("0:00"));
        }
    }
}
