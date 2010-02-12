using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Collections;

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
            var iter = new TaskIterator<int>(CountToThree());

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Assert.AreEqual(iter.Current, 1);
            Scheduler.WaitFor(iter.MoveNext());
            Assert.AreEqual(iter.Current, 2);
            Scheduler.WaitFor(iter.MoveNext());
            Assert.AreEqual(iter.Current, 3);

            iter.Dispose();
        }

        [Test]
        public void TestTypeCoercion () {
            var iter = new TaskIterator<int>(TwoTypes());

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Assert.AreEqual(iter.Current, 1);
            try {
                Scheduler.WaitFor(iter.MoveNext());
                Assert.Fail("MoveNext did not throw an InvalidCastException");
            } catch (FutureException fe) {
                Assert.IsInstanceOfType(typeof(InvalidCastException), fe.InnerException);
            }

            iter.Dispose();
        }

        [Test]
        public void IteratorRaisesOnceIterationIsComplete () {
            var iter = new TaskIterator<int>(JustOne());

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Scheduler.WaitFor(iter.MoveNext());

            try {
                var _ = iter.Current;
                Assert.Fail("iter.Current did not throw an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
            try {
                var f = iter.MoveNext();
                Assert.Fail("iter.MoveNext did not throw an InvalidOperationException");
            } catch (InvalidOperationException) {
            }

            iter.Dispose();
        }

        [Test]
        public void IteratorRaisesIfCurrentIsAccessedBeforeInitialMoveNext () {
            var iter = new TaskIterator<int>(JustOne());

            try {
                var _ = iter.Current;
                Assert.Fail("iter.Current did not throw an InvalidOperationException");
            } catch (InvalidOperationException) {
            }

            iter.Dispose();
        }

        [Test]
        public void IteratorRaisesOnceIteratorIsDisposed () {
            var iter = new TaskIterator<int>(JustOne());
            iter.Dispose();

            try {
                var _ = iter.Current;
                Assert.Fail("iter.Current did not throw an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
            try {
                var f = iter.MoveNext();
                Assert.Fail("iter.MoveNext did not throw an InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void IteratorDisposedIsTrueOnceDisposed () {
            var iter = new TaskIterator<int>(JustOne());
            iter.Dispose();

            Assert.IsTrue(iter.Disposed);
        }

        [Test]
        public void IteratorDisposedIsTrueOnceIterationCompletesButNotBefore () {
            var iter = new TaskIterator<int>(CountToThree());
            Assert.IsFalse(iter.Disposed);

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Assert.IsFalse(iter.Disposed);

            Scheduler.WaitFor(iter.MoveNext());
            Scheduler.WaitFor(iter.MoveNext());
            Scheduler.WaitFor(iter.MoveNext());
            Assert.IsTrue(iter.Disposed);
        }

        [Test]
        public void TestToArray () {
            var iter = new TaskIterator<int>(CountToThree());

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));

            var f = iter.ToArray();
            int[] result = (int[])Scheduler.WaitFor(f);

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
        public void GetTaskIterator () {
            var e = CountTo100(Thread.CurrentThread);
            var iter = TaskIterator<int>.FromEnumerable(e);

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Assert.AreEqual(iter.Current, 0);

            Scheduler.WaitFor(iter.MoveNext());
            Assert.AreEqual(iter.Current, 1);
        }

        [Test]
        public void EnumeratorDisposal () {
            var e = new TestEnumerator();
            var iter = TaskIterator<object>.FromEnumerator(e);
            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            Assert.IsTrue(e.Disposed);
            Assert.IsTrue(iter.Disposed);
        }

        IEnumerator<object> IterationTask (TaskIterator<int> iterator, List<int> output) {
            yield return iterator.Start();

            while (!iterator.Disposed) {
                output.Add(iterator.Current);

                yield return iterator.MoveNext();
            }
        }

        [Test]
        public void YieldStartGetTaskIterator () {
            var e = CountTo100(Thread.CurrentThread);
            var iter = TaskIterator<int>.FromEnumerable(e);

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
            var iter = TaskIterator<int>.FromEnumerable(e);

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            int[] items = (int[])Scheduler.WaitFor(iter.ToArray());

            int[] expected = new int[100];
            for (int i = 0; i < 100; i++)
                expected[i] = i;

            Assert.AreEqual(expected, items);
        }

        [Test]
        public void TestToArrayOnEmptySequence () {
            var e = new TestEnumerator();
            var iter = TaskIterator<object>.FromEnumerator(e);

            Scheduler.WaitFor(Scheduler.Start(iter.Start()));
            object[] items = (object[])Scheduler.WaitFor(iter.ToArray());
            object[] expected = new object[0];

            Assert.AreEqual(expected, items);
        }
    }
}