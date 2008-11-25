using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Squared.Util;
using System.Threading;

namespace Squared.Util {
    [TestFixture]
    public class AtomicQueueTests {
        [Test]
        public void BasicTest () {
            var q = new AtomicQueue<int>();

            for (int i = 0; i < 10; i++)
                q.Enqueue(i);

            for (int i = 0; i < 10; i++) {
                int r;
                Assert.IsTrue(q.Dequeue(out r));
                Assert.AreEqual(i, r);
            }
        }

        [Test]
        public void ThreadedTest () {
            int iterations = 500000;

            var q = new AtomicQueue<int>();
            var signal = new ManualResetEvent(false);
            Thread a, b;

            a = new Thread(() => {
                signal.WaitOne();

                for (int i = 0; i < iterations; i++)
                    q.Enqueue(i);

                q.Enqueue(iterations + 1);
            });

            b = new Thread(() => {
                signal.WaitOne();

                bool ok = true;
                for (int i = 0; i < iterations; i++) {
                    int r;

                    while (!q.Dequeue(out r)) {
                        Thread.Sleep(0);
                    }

                    ok &= (r == i);
                }

                Assert.IsTrue(ok);
            });

            a.Start();
            b.Start();

            while (
                (a.ThreadState != ThreadState.WaitSleepJoin) ||
                (b.ThreadState != ThreadState.WaitSleepJoin)
            ) {
                Thread.Sleep(1);
            }

            signal.Set();

            a.Join();
            b.Join();

            {
                int r;
                Assert.IsTrue(q.Dequeue(out r));
                Assert.AreEqual(iterations + 1, r);
            }
        }
    }
}
