using System.Linq;
using NUnit.Framework;

namespace Iori.Audio.Tests
{
    public class PlayQueueTests
    {
        static readonly string[] Five = { "a", "b", "c", "d", "e" };

        static PlayQueue Queue(int start = 0, bool shuffle = false, int seed = 7)
        {
            var q = new PlayQueue();
            q.Set(Five, start, shuffle, seed);
            return q;
        }

        [Test]
        public void Set_StartsAtTheChosenSong_InTheGivenOrder()
        {
            var q = Queue(2);
            Assert.That(q.Order, Is.EqualTo(Five));
            Assert.That(q.Current, Is.EqualTo("c"));
        }

        [Test]
        public void EmptyQueue_HasNoCurrent_AndNothingMoves()
        {
            var q = new PlayQueue();
            Assert.That(q.Current, Is.Null);
            Assert.That(q.Next(true), Is.False);
            Assert.That(q.Previous(0), Is.False);
            q.Set(new string[0], 3, true, 1);
            Assert.That(q.Index, Is.EqualTo(-1));
        }

        [Test]
        public void RepeatOff_StopsAfterTheLastSong_ButTheNextButtonWrapsAround()
        {
            var q = Queue(3);
            Assert.That(q.Next(false), Is.True);
            Assert.That(q.Current, Is.EqualTo("e"));
            Assert.That(q.Next(false), Is.False, "最後の曲が終わったら止まる");
            Assert.That(q.Current, Is.EqualTo("e"));
            Assert.That(q.Next(true), Is.True);
            Assert.That(q.Current, Is.EqualTo("a"));
        }

        [Test]
        public void RepeatAll_WrapsAround()
        {
            var q = Queue(4);
            q.SetRepeat(RepeatMode.All);
            Assert.That(q.Next(false), Is.True);
            Assert.That(q.Current, Is.EqualTo("a"));
            Assert.That(q.Previous(0), Is.True);
            Assert.That(q.Current, Is.EqualTo("e"));
        }

        [Test]
        public void RepeatOne_RepeatsWhenTheSongEnds_ButTheNextButtonMovesOn()
        {
            var q = Queue(1);
            q.SetRepeat(RepeatMode.One);
            Assert.That(q.Next(false), Is.True);
            Assert.That(q.Current, Is.EqualTo("b"));
            Assert.That(q.Next(true), Is.True);
            Assert.That(q.Current, Is.EqualTo("c"));
        }

        [Test]
        public void Previous_RestartsTheSong_WhenItHasPlayedForAWhile_OrIsTheFirst()
        {
            var q = Queue(2);
            Assert.That(q.Previous(PlayQueue.RestartAfterMs + 1), Is.False);
            Assert.That(q.Current, Is.EqualTo("c"));
            Assert.That(q.Previous(1000), Is.True);
            Assert.That(q.Current, Is.EqualTo("b"));
            q.Previous(0);
            Assert.That(q.Previous(0), Is.False, "最初の曲では頭に戻るだけ");
            Assert.That(q.Current, Is.EqualTo("a"));
        }

        [Test]
        public void Shuffle_PutsTheChosenSongFirst_KeepsEverySong_AndIsStableForASeed()
        {
            var q = Queue(3, shuffle: true, seed: 42);
            Assert.That(q.Current, Is.EqualTo("d"));
            Assert.That(q.Index, Is.EqualTo(0));
            Assert.That(q.Order.OrderBy(x => x), Is.EqualTo(Five));
            Assert.That(Queue(3, shuffle: true, seed: 42).Order, Is.EqualTo(q.Order));
            // どれかの seed では、元の並びと違う順になる
            Assert.That(Enumerable.Range(0, 20).Any(s => !Queue(0, true, s).Order.SequenceEqual(Five)), Is.True);
        }

        [Test]
        public void SwitchingShuffle_KeepsTheCurrentSong()
        {
            var q = Queue(1);
            q.Next(true);
            q.SetShuffle(true, 5);
            Assert.That(q.Current, Is.EqualTo("c"));
            Assert.That(q.Index, Is.EqualTo(0));
            q.Next(true);
            var now = q.Current;
            q.SetShuffle(false, 5);
            Assert.That(q.Order, Is.EqualTo(Five));
            Assert.That(q.Current, Is.EqualTo(now));
        }

        [Test]
        public void JumpTo_And_SyncIndex()
        {
            var q = Queue();
            Assert.That(q.JumpTo("d"), Is.True);
            Assert.That(q.Index, Is.EqualTo(3));
            Assert.That(q.JumpTo("zzz"), Is.False);
            q.SyncIndex(1);
            Assert.That(q.Current, Is.EqualTo("b"));
            q.SyncIndex(99);
            Assert.That(q.Current, Is.EqualTo("b"));
        }

        [Test]
        public void Remove_DropsMissingSongs_AndKeepsOrMovesTheCurrent()
        {
            var q = Queue(2);
            q.Remove(id => id == "a" || id == "e");
            Assert.That(q.Order, Is.EqualTo(new[] { "b", "c", "d" }));
            Assert.That(q.Current, Is.EqualTo("c"));
            q.Remove(id => id == "c");
            Assert.That(q.Current, Is.EqualTo("d"), "いまの曲が消えたら次の曲");
            q.Remove(id => true);
            Assert.That(q.Current, Is.Null);
        }

        [Test]
        public void SaveAndLoad_RestoreTheQueue()
        {
            var q = Queue(1, shuffle: true, seed: 9);
            q.SetRepeat(RepeatMode.All);
            q.Next(true);
            var copy = new PlayQueue();
            copy.Load(q.Save());
            Assert.That(copy.Order, Is.EqualTo(q.Order));
            Assert.That(copy.Current, Is.EqualTo(q.Current));
            Assert.That(copy.Shuffle, Is.True);
            Assert.That(copy.Repeat, Is.EqualTo(RepeatMode.All));
            // まぜるのをやめると元の並びに戻る(元の並びも覚えている)
            copy.SetShuffle(false, 0);
            Assert.That(copy.Order, Is.EqualTo(Five));
        }

        [Test]
        public void Load_OfBrokenText_GivesAnEmptyQueue()
        {
            var q = Queue();
            q.Load("{ not json");
            Assert.That(q.Count, Is.EqualTo(0));
            Assert.That(q.Current, Is.Null);
            q.Load("");
            Assert.That(q.Count, Is.EqualTo(0));
        }
    }
}
