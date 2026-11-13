using System.Collections.Generic;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class AmbientMixerTests
    {
        static AmbientMixer Mixer() => new AmbientMixer(new List<AmbientDef>
        {
            new AmbientDef { Id = "fire", File = "a/fire.ogg", DefaultVolume = 0.8f },
            new AmbientDef { Id = "rain", File = "a/rain.ogg", DefaultVolume = 0.6f, Weathers = new List<string> { WeatherIds.Rain } },
        });

        [Test]
        public void UsesDefaultVolumes()
        {
            var m = Mixer();
            Assert.That(m.EffectiveVolume("fire", WeatherIds.Clear), Is.EqualTo(0.8f).Within(1e-6));
        }

        [Test]
        public void WeatherGatedLayer_IsSilentInOtherWeather()
        {
            var m = Mixer();
            Assert.That(m.EffectiveVolume("rain", WeatherIds.Clear), Is.EqualTo(0f));
            Assert.That(m.EffectiveVolume("rain", WeatherIds.Rain), Is.EqualTo(0.6f).Within(1e-6));
        }

        [Test]
        public void MasterAndMute_Apply()
        {
            var m = Mixer();
            m.SetMaster(0.5f);
            Assert.That(m.EffectiveVolume("fire", WeatherIds.Clear), Is.EqualTo(0.4f).Within(1e-6));
            m.SetMuted("fire", true);
            Assert.That(m.EffectiveVolume("fire", WeatherIds.Clear), Is.EqualTo(0f));
            Assert.That(m.GetUserVolume("fire"), Is.EqualTo(0.8f).Within(1e-6), "ミュートしても音量は覚えている");
        }

        [Test]
        public void VolumesAreClamped()
        {
            var m = Mixer();
            m.SetUserVolume("fire", 3f);
            Assert.That(m.GetUserVolume("fire"), Is.EqualTo(1f));
            m.SetUserVolume("fire", -1f);
            Assert.That(m.GetUserVolume("fire"), Is.EqualTo(0f));
        }

        [Test]
        public void SaveAndLoad_RoundTrip()
        {
            var a = Mixer();
            a.SetUserVolume("fire", 0.3f);
            a.SetMuted("rain", true);
            a.SetMaster(0.9f);

            var b = Mixer();
            b.Load(a.Save());
            Assert.That(b.GetUserVolume("fire"), Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(b.IsMuted("rain"), Is.True);
            Assert.That(b.Master, Is.EqualTo(0.9f).Within(1e-6));
        }

        [Test]
        public void Load_IgnoresBrokenOrUnknownData()
        {
            var m = Mixer();
            m.Load("{ broken");
            m.Load(@"{ ""master"": 0.5, ""layers"": { ""unknown"": { ""volume"": 0.1 } } }");
            Assert.That(m.Master, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(m.GetUserVolume("fire"), Is.EqualTo(0.8f).Within(1e-6));
        }

        [Test]
        public void UnknownLayer_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => Mixer().SetUserVolume("nope", 0.5f));
        }
    }
}
