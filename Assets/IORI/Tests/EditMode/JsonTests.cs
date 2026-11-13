using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class JsonTests
    {
        [Test]
        public void ParsesNestedValues()
        {
            var o = (Dictionary<string, object>)Json.Parse(@"{ ""a"": [1, 2.5, -3e2], ""b"": { ""c"": ""x\nyあ"" }, ""d"": true, ""e"": null }");
            var a = (List<object>)o["a"];
            Assert.That(a, Is.EqualTo(new object[] { 1.0, 2.5, -300.0 }));
            Assert.That(((Dictionary<string, object>)o["b"])["c"], Is.EqualTo("x\nyあ"));
            Assert.That(o["d"], Is.EqualTo(true));
            Assert.That(o["e"], Is.Null);
        }

        [Test]
        public void WriteThenParse_RoundTrips()
        {
            var src = new Dictionary<string, object>
            {
                ["s"] = "焚き火 \"quote\" \\",
                ["n"] = 1763016000000L,
                ["f"] = 0.25,
                ["l"] = new List<object> { 1, "two", false },
            };
            var back = (Dictionary<string, object>)Json.Parse(Json.Write(src));
            Assert.That(back["s"], Is.EqualTo(src["s"]));
            Assert.That(back["n"], Is.EqualTo(1763016000000.0));
            Assert.That(back["f"], Is.EqualTo(0.25));
        }

        [Test]
        public void RejectsMalformed()
        {
            Assert.Throws<FormatException>(() => Json.Parse("{"));
            Assert.Throws<FormatException>(() => Json.Parse("[1,]"));
            Assert.Throws<FormatException>(() => Json.Parse("{} x"));
        }
    }
}
