using System.Collections.Generic;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class SettingsStoreTests
    {
        static SettingsStore Store()
        {
            var s = new SettingsStore();
            s.Define(IoriSettings.Basics());
            return s;
        }

        static readonly List<AmbientDef> Layers = new List<AmbientDef>
        {
            new AmbientDef { Id = "fire", File = "a/fire.ogg", DefaultVolume = 0.7f },
            new AmbientDef { Id = "rain", File = "a/rain.ogg", DefaultVolume = 0.6f },
        };

        [Test]
        public void Defaults_AreUsedUntilSet()
        {
            var s = Store();
            Assert.That(s.GetNumber(IoriSettings.RadioVolume), Is.EqualTo(1.0));
            Assert.That(s.GetNumber(IoriSettings.UiVolume), Is.EqualTo(IoriSettings.UiVolumeDefault));
            Assert.That(s.IsDefault(IoriSettings.RadioVolume), Is.True);
        }

        [Test]
        public void SetNumber_ClampsToRange_AndTellsWatchersOnce()
        {
            var s = Store();
            var heard = new List<string>();
            s.Watch(IoriSettings.RadioVolume, k => heard.Add(k));
            Assert.That(s.SetNumber(IoriSettings.RadioVolume, 1.7), Is.False); // 既定の 1 のまま
            Assert.That(s.SetNumber(IoriSettings.RadioVolume, 0.4), Is.True);
            Assert.That(s.SetNumber(IoriSettings.RadioVolume, 0.4), Is.False);
            Assert.That(s.SetNumber(IoriSettings.RadioVolume, -3), Is.True);
            Assert.That(s.GetNumber(IoriSettings.RadioVolume), Is.EqualTo(0.0));
            Assert.That(heard, Is.EqualTo(new[] { IoriSettings.RadioVolume, IoriSettings.RadioVolume }));
        }

        [Test]
        public void Watch_ByPrefix_HearsEveryAmbientLayer_AndStopsWhenDisposed()
        {
            var s = Store();
            s.Define(IoriSettings.Ambient(Layers));
            var heard = new List<string>();
            var w = s.Watch(IoriSettings.AmbientPrefix, k => heard.Add(IoriSettings.AmbientIdOf(k)));
            s.SetNumber(IoriSettings.AmbientVolume("fire"), 0.2);
            s.SetNumber(IoriSettings.RadioVolume, 0.3);
            s.SetNumber(IoriSettings.AmbientVolume("rain"), 0.1);
            w.Dispose();
            s.SetNumber(IoriSettings.AmbientVolume("fire"), 0.9);
            Assert.That(heard, Is.EqualTo(new[] { "fire", "rain" }));
        }

        [Test]
        public void SaveAndLoad_RoundTrips_AndTellsWhatChanged()
        {
            var a = Store();
            a.Define(IoriSettings.Ambient(Layers));
            a.SetNumber(IoriSettings.PlayerVolume, 0.25);
            a.SetNumber(IoriSettings.AmbientVolume("fire"), 0);
            a.SetToggle("voice.on", false);
            a.SetText("talk.model", "x");

            var b = Store();
            b.Define(IoriSettings.Ambient(Layers));
            b.SetNumber(IoriSettings.RadioVolume, 0.5); // 読むと既定の 1 に戻る
            var heard = new List<string>();
            b.Changed += k => heard.Add(k);
            Assert.That(b.Load(a.Save()), Is.True);
            Assert.That(b.GetNumber(IoriSettings.PlayerVolume), Is.EqualTo(0.25));
            Assert.That(b.GetNumber(IoriSettings.AmbientVolume("fire")), Is.EqualTo(0.0));
            Assert.That(b.GetNumber(IoriSettings.RadioVolume), Is.EqualTo(1.0));
            Assert.That(b.GetToggle("voice.on"), Is.False);
            Assert.That(b.GetText("talk.model"), Is.EqualTo("x"));
            Assert.That(heard, Is.EquivalentTo(new[]
            {
                IoriSettings.PlayerVolume, IoriSettings.AmbientVolume("fire"), IoriSettings.RadioVolume, "voice.on", "talk.model",
            }));
        }

        [Test]
        public void Load_BeforeDefine_KeepsTheValue_ForTheLayerDefinedLater()
        {
            var s = Store();
            Assert.That(s.Load("{\"version\":1,\"values\":{\"volume.ambient.fire\":0.3,\"volume.future\":2}}"), Is.True);
            s.Define(IoriSettings.Ambient(Layers));
            Assert.That(s.GetNumber(IoriSettings.AmbientVolume("fire")), Is.EqualTo(0.3).Within(1e-9));
            Assert.That(s.GetNumber(IoriSettings.AmbientVolume("rain")), Is.EqualTo(0.6).Within(1e-6));
            // 決まりのない鍵も保存に残る
            Assert.That(s.Save(), Does.Contain("volume.future"));
        }

        [Test]
        public void Load_RejectsBrokenText_AndKeepsValues()
        {
            var s = Store();
            s.SetNumber(IoriSettings.VoiceVolume, 0.4);
            Assert.That(s.Load("{oops"), Is.False);
            Assert.That(s.Load("[1,2]"), Is.False);
            Assert.That(s.GetNumber(IoriSettings.VoiceVolume), Is.EqualTo(0.4));
        }

        [Test]
        public void Load_IgnoresValuesOfTheWrongKind()
        {
            var s = Store();
            Assert.That(s.Load("{\"values\":{\"volume.radio\":\"loud\"}}"), Is.True);
            Assert.That(s.GetNumber(IoriSettings.RadioVolume), Is.EqualTo(1.0));
        }

        [Test]
        public void Defaults_AreNotSaved_SoANewDefaultReachesUntouchedValues()
        {
            var s = Store();
            s.SetNumber(IoriSettings.RadioVolume, 0.5);
            s.SetNumber(IoriSettings.RadioVolume, 1);
            Assert.That(s.Save(), Does.Not.Contain(IoriSettings.RadioVolume));
            Assert.That(s.IsDefault(IoriSettings.RadioVolume), Is.True);
        }

        [Test]
        public void Redefine_ChangesTheDefault_AndTellsWhenTheValueMoves()
        {
            var s = Store();
            s.Define(SettingDef.Number(IoriSettings.AmbientVolume("fire"), 0.5, IoriSettings.VolumeGroup));
            var heard = new List<string>();
            s.Changed += k => heard.Add(k);
            s.Define(IoriSettings.Ambient(Layers));
            Assert.That(s.GetNumber(IoriSettings.AmbientVolume("fire")), Is.EqualTo(0.7).Within(1e-6));
            Assert.That(heard, Is.EqualTo(new[] { IoriSettings.AmbientVolume("fire") }));
        }

        [Test]
        public void Reset_GoesBackToTheDefault()
        {
            var s = Store();
            s.SetNumber(IoriSettings.UiVolume, 0.1);
            Assert.That(s.Reset(IoriSettings.UiVolume), Is.True);
            Assert.That(s.GetNumber(IoriSettings.UiVolume), Is.EqualTo(IoriSettings.UiVolumeDefault));
            Assert.That(s.Reset(IoriSettings.UiVolume), Is.False);
        }

        [Test]
        public void SettingTheWrongKind_Throws()
        {
            var s = Store();
            Assert.Throws<System.InvalidOperationException>(() => s.SetToggle(IoriSettings.RadioVolume, true));
        }

        [Test]
        public void ImportLegacy_MovesTheOldVolumes()
        {
            var s = Store();
            var mixer = new AmbientMixer(Layers);
            mixer.SetUserVolume("fire", 0.15f);
            mixer.SetMuted("rain", true);
            Assert.That(IoriSettings.ImportLegacy(s, 0.8, null, mixer.Save()), Is.True);
            s.Define(IoriSettings.Ambient(Layers));
            Assert.That(s.GetNumber(IoriSettings.RadioVolume), Is.EqualTo(0.8).Within(1e-6));
            Assert.That(s.GetNumber(IoriSettings.PlayerVolume), Is.EqualTo(0.8).Within(1e-6)); // 無ければラジオの曲から
            Assert.That(s.GetNumber(IoriSettings.AmbientVolume("fire")), Is.EqualTo(0.15).Within(1e-6));
            Assert.That(s.GetNumber(IoriSettings.AmbientVolume("rain")), Is.EqualTo(0.6).Within(1e-6));
        }

        [Test]
        public void ImportLegacy_WithNothing_ChangesNothing()
        {
            var s = Store();
            Assert.That(IoriSettings.ImportLegacy(s, null, null, "{broken"), Is.False);
            Assert.That(s.Version, Is.EqualTo(0));
        }
    }
}
