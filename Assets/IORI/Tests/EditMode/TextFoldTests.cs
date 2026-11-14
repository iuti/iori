using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class TextFoldTests
    {
        [Test]
        public void Fold_IgnoresCaseWidthKanaAndSpaces()
        {
            Assert.That(TextFold.Fold("ＡＢＣ abc"), Is.EqualTo("abcabc"));
            Assert.That(TextFold.Fold("カタカナ"), Is.EqualTo("かたかな"));
            Assert.That(TextFold.Fold("ｶﾀｶﾅ"), Is.EqualTo("かたかな"));
            Assert.That(TextFold.Fold("ｶﾞｯｺｳ ﾊﾟﾝ"), Is.EqualTo("がっこうぱん"));
            Assert.That(TextFold.Fold("ﾂﾞﾄﾞｳﾞ"), Is.EqualTo("づどゔ"));
            Assert.That(TextFold.Fold("かなた　ゆづき"), Is.EqualTo("かなたゆづき"));
            Assert.That(TextFold.Fold("ラーメン"), Is.EqualTo("らーめん"));
            Assert.That(TextFold.Fold(null), Is.EqualTo(""));
        }

        [Test]
        public void Words_SplitOnSpaces()
        {
            Assert.That(TextFold.Words(" ミナト　トリオ "), Is.EqualTo(new[] { "みなと", "とりお" }));
            Assert.That(TextFold.Words("   "), Is.Empty);
        }
    }
}
