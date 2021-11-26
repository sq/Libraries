#pragma warning disable 0612

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Squared.Threading;

namespace Squared.Threading {
    [TestFixture]
    public class FutureTests {
        [Test]
        public void FuturesNotCompleteByDefault () {
            var f = new Future<object>();
            Assert.IsFalse(f.Completed);
        }

        [Test]
        public void CanCompleteFuture () {
            var f = new Future<object>();
            f.Complete();
            Assert.IsTrue(f.Completed);
        }

        [Test]
        public void CanGetResult () {
            var f = new Future<object>();
            f.Complete(5);
            Assert.AreEqual(5, f.Result);
        }

        [Test]
        public void GettingResultThrowsExceptionIfFutureValueIsException () {
            var f = new Future<object>();
            f.Fail(new Exception("test"));
            try {
                var _ = f.Result;
                Assert.Fail();
            } catch (FutureException e) {
                Assert.AreEqual("test", e.InnerException.Message);
            }
        }

        [Test]
        public void FailedIsFalseIfFutureHasValue () {
            var f = new Future<object>();
            f.Complete(5);
            Assert.IsFalse(f.Failed);
        }

        [Test]
        public void FailedIsTrueIfFutureValueIsException () {
            var f = new Future<object>();
            f.Fail(new Exception("test"));
            Assert.IsTrue(f.Failed);
        }

        [Test]
        public void TestGetResultMethodNeverThrows () {
            var f = new Future<object>();
            object result;
            Exception error;

            Assert.IsFalse(f.GetResult(out result, out error));

            f.SetResult(5, null);
            Assert.IsTrue(f.GetResult(out result, out error));
            Assert.AreEqual(5, result);

            f = new Future<object>();

            f.SetResult(null, new Exception("earth-shattering kaboom"));
            Assert.IsTrue(f.GetResult(out result, out error));
            Assert.IsTrue(error is Exception);
        }

        [Test]
        public void InvokesOnCompletesWhenCompleted () {
            var f = new Future<object>();
            object completeResult = null;
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            f.Complete(5);
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void InvokesOnCompletesWhenFailed () {
            var f = new Future<object>();
            object completeResult = null;
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            f.Fail(new Exception("test"));
            Assert.AreEqual("test", (completeResult as Exception).Message);
        }

        [Test]
        public void InvokesTypedOnCompletesWhenCompleted () {
            var f = new Future<int>();
            object completeResult = null;
            f.RegisterOnComplete2((_) => { completeResult = _.Error ?? (object)_.Result; });
            f.Complete(5);
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void InvokesOnResolvedWhenCompleted () {
            var f1 = new Future<int>();
            var f2 = new Future<int>();
            var f3 = new Future<int>();
            object completeResult = null;
            var handler = (OnFutureResolved<int>)(
                (f) => {
                    completeResult =
                        (f.Disposed)
                            ? "disposed"
                            : (f.Failed
                                ? f.Error
                                : (object)f.Result
                            );
                }
            );
            f1.RegisterOnResolved(handler);
            f1.Complete(5);
            Assert.AreEqual(5, completeResult);
            f2.RegisterOnResolved(handler);
            f2.Dispose();
            Assert.AreEqual("disposed", completeResult);
            f3.RegisterOnResolved(handler);
            var exc = new Exception("test");
            f3.SetResult(0, exc);
            Assert.AreEqual(exc, completeResult);
        }

        [Test]
        public void ThrowsIfCompletedTwice () {
            var f = new Future<object>();
            try {
                f.Complete(5);
                f.Complete(10);
                Assert.Fail();
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void IfOnCompleteRegisteredAfterAlreadyCompletedCalledAnyway () {
            var f = new Future<object>();
            object completeResult = null;
            f.Complete(5);
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void ThrowsIfResultAccessedWhileIncomplete () {
            var f = new Future<object>();
            try {
                var _ = f.Result;
                Assert.Fail();
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void CanCompleteWithNull () {
            var f = new Future<object>();
            f.Complete();
            Assert.AreEqual(null, f.Result);
        }

        [Test]
        public void CanBindFutureToOtherFuture () {
            var a = new Future<object>();
            var b = new Future<object>();
            b.Bind(a);
            a.Complete(5);
            Assert.AreEqual(5, b.Result);
        }

        [Test]
        public void CannotBeCompletedIfDisposedFirst () {
            var f = new Future<object>();
            f.Dispose();
            Assert.IsTrue(f.Disposed);
            f.Complete(5);
            Assert.IsTrue(f.Disposed);
            Assert.IsFalse(f.Completed);
        }

        [Test]
        public void IfCompletedDisposeHasNoEffect () {
            var f = new Future<object>();
            f.Complete(5);
            f.Dispose();
            Assert.AreEqual(5, f.Result);
            Assert.IsFalse(f.Disposed);
        }

        [Test]
        public void DisposingFutureInvokesOnDisposeHandlers () {
            bool[] invoked = new bool[1];
            
            var f = new Future<object>();
            f.RegisterOnDispose((_) => {
                invoked[0] = true;
            });

            f.Dispose();
            Assert.IsTrue(invoked[0]);
        }

        [Test]
        public void RegisteringHandlersOnDisposedFutureWorks () {
            bool[] invoked = new bool[4];
            
            var f = new Future<object>();
            f.RegisterHandlers(
                (_) => invoked[0] = true,
                (_) => invoked[1] = true
            );
            f.Dispose();
            f.RegisterHandlers(
                (_) => invoked[2] = true,
                (_) => invoked[3] = true
            );

            Assert.IsFalse(invoked[0]);
            Assert.IsTrue(invoked[1]);
            Assert.IsFalse(invoked[2]);
            Assert.IsTrue(invoked[3]);
        }

        [Test]
        public void CollectingFutureDoesNotInvokeOnDisposeHandlers () {
            bool[] invoked = new bool[1];

            var f = new Future<object>();
            f.RegisterOnDispose((_) => {
                invoked[0] = true;
            });

            f = null;
            GC.Collect();

            Assert.IsFalse(invoked[0]);
        }

        [Test]
        public void FutureWrapsExceptionIfOnCompleteHandlerThrows () {
            var f = new Future<object>();
            f.RegisterOnComplete((_) => {
                throw new Exception("pancakes");
            });

            try {
                f.Complete(1);
                Assert.Fail("Exception was swallowed");
            } catch (FutureHandlerException fhe) {
                Assert.IsInstanceOf<Exception>(fhe.InnerException);
                Assert.AreEqual("pancakes", fhe.InnerException.Message);
            }
        }

        [Test]
        public void FutureWrapsExceptionIfOnDisposeHandlerThrows () {
            var f = new Future<object>();
            f.RegisterOnDispose((_) => {
                throw new Exception("pancakes");
            });

            try {
                f.Dispose();
                Assert.Fail("Exception was swallowed");
            } catch (FutureHandlerException fhe) {
                Assert.IsInstanceOf<Exception>(fhe.InnerException);
                Assert.AreEqual("pancakes", fhe.InnerException.Message);
            }
        }

        class TestClass {
            public int Field;
            public int Property {
                get;
                set;
            }
        }

        struct TestStruct {
            public int Field;
            public int Property {
                get;
                set;
            }
        }

        [Test]
        public void BindToField () {
            var tc = new TestClass();
            var ts = new TestStruct();

            var f = new Future<int>();

            f.Bind(() => tc.Field);

            try {
                f.Bind(() => ts.Field);
                Assert.Fail("Did not throw InvalidOperationException");
            } catch (InvalidOperationException) {
            }

            f.Complete(5);

            Assert.AreEqual(5, tc.Field);
            Assert.AreNotEqual(5, ts.Field);
        }

        [Test]
        public void BindToProperty () {
            var tc = new TestClass();
            var ts = new TestStruct();

            var f = new Future<int>();

            f.Bind(() => tc.Property);

            try {
                f.Bind(() => ts.Property);
                Assert.Fail("Did not throw InvalidOperationException");
            } catch (InvalidOperationException) {
            }

            f.Complete(5);

            Assert.AreEqual(5, tc.Property);
            Assert.AreNotEqual(5, ts.Property);
        }

        [Test]
        public void WaitForFirst () {
            // Basic test for behaviors - ignores nulls, handles duplicates
            var futures = new IFuture[] {
                null,
                new SignalFuture(),
                new Future<int>(),
                new Future<bool>(),
            };
            var wfa = Future.WaitForFirst(futures);
            Assert.False(wfa.Completed);
            futures[2].Complete();
            futures[3].Complete();
            Assert.AreEqual(futures[2], wfa.Result);
        }

        [Test]
        public void WaitForAll () {
            // Basic test for behaviors - ignores nulls, handles duplicates
            var futures = new IFuture[] {
                null,
                new SignalFuture(),
                new Future<int>(),
                new Future<bool>(),
                null // We put a duplicate here
            };
            futures[4] = futures[1];
            var wfa = Future.WaitForAll(futures);
            Assert.False(wfa.Completed);
            futures[1].Complete();
            Assert.False(wfa.Completed);
            futures[2].Dispose();
            Assert.False(wfa.Completed);
            futures[3].Complete();
            Assert.True(wfa.Completed);
        }

        [Test]
        public void WaitForAllWillDisposeIfAllFuturesAreDisposed () {
            var futures = new IFuture[] {
                new SignalFuture(),
                new Future<int>(),
            };
            var wfa = Future.WaitForAll(futures);
            foreach (var f in futures)
                f.Dispose();
            Assert.True(wfa.Disposed);
        }

        [Test]
        public void WaitForAllAndWaitForFirstInstantlyCompleteWithEmptyList () {
            Assert.True(Future.WaitForAll().Completed);
            Assert.True(Future.WaitForFirst().Completed);
        }
    }
}
