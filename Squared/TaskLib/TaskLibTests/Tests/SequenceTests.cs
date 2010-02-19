using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Collections;
using Squared.Util;

namespace Squared.Task {
    [TestFixture]
    public class TaskIteratorTests {
        TaskScheduler Scheduler;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler();
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        public IEnumerator<object> JustOne () {
            yield return new NextValue(1);
        }

        public IEnumerator<object> TwoTypes () {
            yield return new NextValue(1);
            yield return new NextValue("a");
        }

        public IEnumerator<object> CountToThree () {
            yield return new NextValue(1);
            yield return new NextValue(2);
            yield return new NextValue(3);
        }

        [Test]
        public void TestIterateSequence () {
            using (var iter = new TaskEnumerator<int>(CountToThree(), 1)) {
                for (int i = 1; i <= 3; i++) {
                    Assert.IsFalse(iter.Disposed);
                    Scheduler.WaitFor(iter.Fetch());

                    using (var e = iter.CurrentItems) {
                        Assert.IsTrue(e.MoveNext());
                        Assert.AreEqual(i, e.Current);
                        Assert.IsFalse(e.MoveNext());
                    }
                }
            }
        }

        [Test]
        public void TestTypeCoercion () {
            var iter = new TaskEnumerator<int>(TwoTypes(), 1);

            Assert.IsFalse(iter.Disposed);
            Scheduler.WaitFor(iter.Fetch());

            using (var e = iter.CurrentItems) {
                Assert.IsTrue(e.MoveNext());
                Assert.AreEqual(1, e.Current);
            }

            Assert.IsFalse(iter.Disposed);

            try {
                Scheduler.WaitFor(iter.Fetch());
                Assert.Fail("Fetch did not throw an InvalidCastException");
            } catch (FutureException fe) {
                Assert.IsInstanceOfType(typeof(InvalidCastException), fe.InnerException);
            }

            iter.Dispose();
        }

        [Test]
        public void IteratorFailsMoveNextOnceIterationIsComplete () {
            var iter = new TaskEnumerator<int>(JustOne(), 1);

            Assert.IsFalse(iter.Disposed);
            Scheduler.WaitFor(iter.Fetch());

            using (var e = iter.CurrentItems) {
                Assert.IsTrue(e.MoveNext());
                Assert.AreEqual(1, e.Current);
                Assert.IsFalse(e.MoveNext());
            }

            Assert.IsFalse(iter.Disposed);
            Scheduler.WaitFor(iter.Fetch());
            Assert.IsTrue(iter.Disposed);

            iter.Dispose();
        }

        [Test]
        public void IteratorRaisesIfCurrentIsAccessedBeforeInitialMoveNext () {
            var iter = new TaskEnumerator<int>(JustOne(), 1);

            Assert.IsFalse(iter.Disposed);
            Scheduler.WaitFor(iter.Fetch());

            try {
                using (var e = iter.CurrentItems) {
                    var _ = e.Current;
                    Assert.Fail("e.Current did not throw an InvalidOperationException");
                }
            } catch (InvalidOperationException) {
            }

            iter.Dispose();
        }

        [Test]
        public void IteratorRaisesOnceIteratorIsDisposed () {
            var iter = new TaskEnumerator<int>(JustOne(), 1);
            iter.Dispose();

            try {
                using (var e = iter.CurrentItems) {
                    var _ = e.Current;
                    Assert.Fail("e.Current did not throw an InvalidOperationException");
                }
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void IteratorDisposedIsTrueOnceDisposed () {
            var iter = new TaskEnumerator<int>(JustOne(), 1);
            iter.Dispose();

            Assert.IsTrue(iter.Disposed);
        }

        [Test]
        public void TestToArray () {
            var iter = new TaskEnumerator<int>(CountToThree(), 1);

            int[] result = (int[])Scheduler.WaitFor(iter.GetArray());

            Assert.AreEqual(new int[] { 1, 2, 3 }, result);
        }
    }

    [TestFixture]
    public class EnumeratorTests {
        TaskScheduler Scheduler;

        [SetUp]
        public void SetUp () {
            Scheduler = new TaskScheduler();
        }

        [TearDown]
        public void TearDown () {
            if (Scheduler != null)
                Scheduler.Dispose();
            Scheduler = null;
            GC.Collect();
        }

        IEnumerable<int> CountTo100 (Thread mainThread) {
            for (int i = 0; i < 100; i++) {
                Assert.AreNotEqual(mainThread, Thread.CurrentThread);
                yield return i;
            }
        }

        class TestEnumerator : IEnumerator, IDisposable {
            public bool Disposed = false;

            public void Dispose () {
                Disposed = true;
            }

            public object Current {
                get { return null; }
            }

            public bool MoveNext () {
                return false;
            }

            public void Reset () {
                throw new NotImplementedException();
            }
        }

        [Test]
        public void EnumeratorDisposal () {
            var e = new TestEnumerator();
            var iter = TaskEnumerator<object>.FromEnumerator(e, 1);
            Scheduler.WaitFor(iter.Fetch());
            iter.Dispose();
            Assert.IsTrue(e.Disposed);
        }

        IEnumerator<object> IterationTask (TaskEnumerator<int> iterator, List<int> output) {
            while (!iterator.Disposed) {
                yield return iterator.Fetch();

                foreach (var item in iterator)
                    output.Add(item);
            }
        }

        [Test]
        public void YieldStartGetTaskIterator () {
            var e = CountTo100(Thread.CurrentThread);
            var iter = TaskEnumerator<int>.FromEnumerable(e, 1);

            var output = new List<int>();
            var f = Scheduler.Start(IterationTask(iter, output));
            Scheduler.WaitFor(f);

            int[] expected = new int[100];
            for (int i = 0; i < 100; i++)
                expected[i] = i;

            Assert.AreEqual(output.ToArray(), expected);
        }

        [Test]
        public void TestToArray () {
            var e = CountTo100(Thread.CurrentThread);
            var iter = TaskEnumerator<int>.FromEnumerable(e, 1);

            int[] items = (int[])Scheduler.WaitFor(iter.GetArray());

            int[] expected = new int[100];
            for (int i = 0; i < 100; i++)
                expected[i] = i;

            Assert.AreEqual(expected, items);
        }

        [Test]
        public void TestToArrayOnEmptySequence () {
            var e = new TestEnumerator();
            var iter = TaskEnumerator<object>.FromEnumerator(e, 1);

            object[] items = (object[])Scheduler.WaitFor(iter.GetArray());
            object[] expected = new object[0];

            Assert.AreEqual(expected, items);
        }

        [Test]
        public void TestBufferingPerformance () {
            int[] buf = new int[1024 * 512], copy = null;
            for (int i = 0; i < buf.Length; i++)
                buf[i] = i;

            for (int bs = 8; bs <= 4096; bs *= 2) {
                long timeStart = Time.Ticks;

                using (var iter = TaskEnumerator<int>.FromEnumerator((buf as IEnumerable<int>).GetEnumerator(), bs)) {
                    copy = (int[])Scheduler.WaitFor(iter.GetArray());
                }

                long timeEnd = Time.Ticks;

                TimeSpan elapsed = new TimeSpan(timeEnd - timeStart);
                Console.WriteLine("Took {0:N2} secs with a buffer size of {1}.", elapsed.TotalSeconds, bs);
            }
        }
    }
}