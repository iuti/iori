using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class MediaStoreJsonTests
    {
        [Test]
        public void Parse_ReadsTheFieldsOfEachSong()
        {
            var tracks = MediaStoreJson.Parse(
                "[{\"uri\":\"content://media/external/audio/media/12\",\"title\":\"雨やどりのワルツ\",\"artist\":\"小舟ラジオ楽団\"," +
                "\"album\":\"港の朝\",\"albumArtist\":\"\",\"disc\":2,\"number\":3,\"durationMs\":215000,\"albumId\":7}]");
            Assert.That(tracks.Count, Is.EqualTo(1));
            var t = tracks[0];
            Assert.That(t.Id, Is.EqualTo("content://media/external/audio/media/12"));
            Assert.That(t.Uri, Is.EqualTo(t.Id));
            Assert.That(t.Title, Is.EqualTo("雨やどりのワルツ"));
            Assert.That(t.Artist, Is.EqualTo("小舟ラジオ楽団"));
            Assert.That(t.Album, Is.EqualTo("港の朝"));
            Assert.That(t.Disc, Is.EqualTo(2));
            Assert.That(t.Number, Is.EqualTo(3));
            Assert.That(t.DurationMs, Is.EqualTo(215000));
            Assert.That(t.ArtUri, Is.EqualTo("7"));
        }

        [Test]
        public void Parse_SkipsSongsItCannotRead_AndLeavesTheJacketEmptyWithoutAnAlbum()
        {
            var tracks = MediaStoreJson.Parse(
                "[{\"title\":\"場所が無い\"},{\"uri\":\"content://a/1\",\"title\":5},{\"uri\":\"content://a/2\",\"title\":\"ok\",\"albumId\":0},7]");
            Assert.That(tracks.Select(t => t.Id), Is.EqualTo(new[] { "content://a/2" }));
            Assert.That(tracks[0].ArtUri, Is.EqualTo(""));
            Assert.That(tracks[0].Artist, Is.EqualTo(""));
        }

        [Test]
        public void Parse_GivesAnEmptyListForBrokenText()
        {
            Assert.That(MediaStoreJson.Parse("[{\"uri\":").Count, Is.EqualTo(0));
            Assert.That(MediaStoreJson.Parse("{}").Count, Is.EqualTo(0));
            Assert.That(MediaStoreJson.Parse(null).Count, Is.EqualTo(0));
        }
    }
}
