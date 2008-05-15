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
            } catch (Exception e) {
                Assert.AreEqual("test", e.Message);
            }
        }

        [Test]
        public void IsFailedIsFalseIfFutureHasValue () {
            var f = new Future();
            f.Complete(5);
            Assert.IsFalse(f.Failed);
        }

        [Test]
        public void IsFailedIsTrueIfFutureValueIsException () {
            var f = new Future();
            f.Fail(new Exception("test"));
            Assert.IsTrue(f.Failed);
        }

        [Test]
        public void InvokesOnCompletesWhenCompleted () {
            var f = new Future();
            object completeResult = null;
            f.RegisterOnComplete((result, error) => { completeResult = error ?? (object)result; });
            f.Complete(5);
            Assert.AreEqual(5, completeResult);
        }

        [Test]
        public void InvokesOnCompletesWhenFailed () {
            var f = new Future();
            object completeResult = null;
            f.RegisterOnComplete((result, error) => { completeResult = error ?? (object)result; });
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
            f.RegisterOnComplete((result, error) => { completeResult = error ?? (object)result; });
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
    }
}
