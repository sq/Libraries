using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util.Bind;
using NUnit.Framework;
using System.Linq.Expressions;

namespace Squared.Util {
    [TestFixture]
    public class BindTests {
        struct TestStruct {
            public int Field;
            public int Property {
                get;
                set;
            }
        }

        class TestClass {
            public int Field;
            public int Property {
                get;
                set;
            }
        }

        [Test]
        public void BindToClassField () {
            var tc = new TestClass();

            var classField = new BoundMember<int>(tc, tc.GetType().GetField("Field"));

            tc.Field = 5;
            Assert.AreEqual(5, classField.Value);
            classField.Value = 10;
            Assert.AreEqual(10, tc.Field);
        }

        [Test]
        public void BindToClassProperty () {
            var tc = new TestClass();

            var classProp = new BoundMember<int>(tc, tc.GetType().GetProperty("Property"));

            tc.Property = 5;
            Assert.AreEqual(5, classProp.Value);
            classProp.Value = 10;
            Assert.AreEqual(10, tc.Property);
        }

        [Test]
        public void BindToStructField () {
            var ts = new TestStruct();

            try {
                var structField = new BoundMember<int>(ts, ts.GetType().GetField("Field"));
                Assert.Fail("Did not throw InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }

        [Test]
        public void BindToStructProperty () {
            var ts = new TestStruct();

            try {
                var structProp = new BoundMember<int>(ts, ts.GetType().GetProperty("Property"));
                Assert.Fail("Did not throw InvalidOperationException");
            } catch (InvalidOperationException) {
            }
        }
    }
}
