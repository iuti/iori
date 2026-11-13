using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class PlaceholderAudioTests
    {
        [Test]
        public void GeneratesEveryFileReferencedByTheDefaultProgram_WithoutClipping()
        {
            var assets = PlaceholderAudio.All().ToList();
            Assert.That(assets.Select(a => a.File), Is.Unique);
            foreach (var a in assets)
            {
                Assert.That(a.Samples.Length, Is.GreaterThan(PlaceholderAudio.SampleRate), a.File);
                var peak = a.Samples.Max(s => System.Math.Abs(s));
                Assert.That(peak, Is.GreaterThan(0.01f).And.LessThanOrEqualTo(1f), a.File);
            }
        }
    }
}
