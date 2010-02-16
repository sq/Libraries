using System;
using System.Collections.Generic;
using Squared.Task;
using NUnit.Framework;

namespace Squared.Task {
    [TestFixture]
    public class FutureTests {
        [Test]
        public void FuturesNotCompleteByDefault () {
            var f = new Future();
            Assert.IsFalse(f.Completed);
        }

        [Test]
        public void CanCompleteFuture () {
            var f = new Future();
            f.Complete();
            Assert.IsTrue(f.Completed);
        }

        [Test]
        public void CanGetResult () {
            var f = new Future();
            f.Complete(5);
            Assert.AreEqual(5, f.Result);
        }

        [Test]
        public void GettingResultThrowsExceptionIfFutureValueIsException () {
            var f = new Future();
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
            var f = new Future();
            f.Complete(5);
            Assert.IsFalse(f.Failed);
        }

        [Test]
        public void FailedIsTrueIfFutureValueIsException () {
            var f = new Future();
            f.Fail(new Exception("test"));
            Assert.IsTrue(f.Failed);
        }

        [Test]
        public void TestGetResultMethodNeverThrows () {
            var f = new Future();
            object result;
            Exception error;

            Assert.IsFalse(f.GetResult(out result, out error));

            f.SetResult(5, null);
            Assert.IsTrue(f.GetResult(out result, out error));
            Assert.AreEqual(5, result);

            f = new Future();

            f.SetResult(null, new Exception("earth-shattering kaboom"));
            Assert.IsTrue(f.GetResult(out result, out error));
            Assert.IsTrue(error is Exception);
        }

        [Test]
        public void InvokesOnCompletesWhenCompleted () {
            var f = new Future();
            object completeResult = null;
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            f.Complete(5);
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void InvokesOnCompletesWhenFailed () {
            var f = new Future();
            object completeResult = null;
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            f.Fail(new Exception("test"));
            Assert.AreEqual("test", (completeResult as Exception).Message);
        }

        [Test]
        public void ThrowsIfCompletedTwice () {
            var f = new Future();
            try {
                f.Complete(5);
                f.Complete(10);
                Assert.Fail();
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void IfOnCompleteRegisteredAfterAlreadyCompletedCalledAnyway () {
            var f = new Future();
            object completeResult = null;
            f.Complete(5);
            f.RegisterOnComplete((_) => { completeResult = _.Error ?? _.Result; });
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void ThrowsIfResultAccessedWhileIncomplete () {
            var f = new Future();
            try {
                var _ = f.Result;
                Assert.Fail();
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void CanCompleteWithNull () {
            var f = new Future();
            f.Complete();
            Assert.AreEqual(null, f.Result);
        }

        [Test]
        public void CanBindFutureToOtherFuture () {
            var a = new Future();
            var b = new Future();
            b.Bind(a);
            a.Complete(5);
            Assert.AreEqual(5, b.Result);
        }

        [Test]
        public void CannotBeCompletedIfDisposedFirst () {
            var f = new Future();
            f.Dispose();
            Assert.IsTrue(f.Disposed);
            f.Complete(5);
            Assert.IsTrue(f.Disposed);
            Assert.IsFalse(f.Completed);
        }

        [Test]
        public void IfCompletedDisposeHasNoEffect () {
            var f = new Future();
            f.Complete(5);
            f.Dispose();
            Assert.AreEqual(5, f.Result);
            Assert.IsFalse(f.Disposed);
        }

        [Test]
        public void DisposingFutureInvokesOnDisposeHandlers () {
            bool[] invoked = new bool[1];
            
            var f = new Future();
            f.RegisterOnDispose((_) => {
                invoked[0] = true;
            });

            f.Dispose();
            Assert.IsTrue(invoked[0]);
        }

        [Test]
        public void CollectingFutureDoesNotInvokeOnDisposeHandlers () {
            bool[] invoked = new bool[1];

            var f = new Future();
            f.RegisterOnDispose((_) => {
                invoked[0] = true;
            });

            f = null;
            GC.Collect();

            Assert.IsFalse(invoked[0]);
        }

        [Test]
        public void FutureWrapsExceptionIfOnCompleteHandlerThrows () {
            var f = new Future();
            f.RegisterOnComplete((_) => {
                throw new Exception("pancakes");
            });

            try {
                f.Complete(1);
                Assert.Fail("Exception was swallowed");
            } catch (FutureHandlerException fhe) {
                Assert.IsInstanceOfType(typeof(Exception), fhe.InnerException);
                Assert.AreEqual("pancakes", fhe.InnerException.Message);
            }
        }

        [Test]
        public void FutureWrapsExceptionIfOnDisposeHandlerThrows () {
            var f = new Future();
            f.RegisterOnDispose((_) => {
                throw new Exception("pancakes");
            });

            try {
                f.Dispose();
                Assert.Fail("Exception was swallowed");
            } catch (FutureHandlerException fhe) {
                Assert.IsInstanceOfType(typeof(Exception), fhe.InnerException);
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
    }
}
