using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Squared.Util.Event {
    [TestFixture]
    public class EventTests {
        EventBus Bus;

        [SetUp]
        public void SetUp () {
            Bus = new EventBus();
        }

        [Test]
        public void TestSubscribePrecise () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe(sender, "Test", (e) => trace.Add(e.Type));

            Bus.Broadcast(sender, "Test", null);
            Assert.AreEqual(new string[] { "Test" }, trace.ToArray());

            Bus.Broadcast(sender, "Baz", null);
            Bus.Broadcast("Bar", "Test", null);
            Bus.Broadcast("Bar", "Baz", null);
            Assert.AreEqual(new string[] { "Test" }, trace.ToArray());
        }

        [Test]
        public void TestSubscribeGeneral () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe(null, "Test", (e) => trace.Add(String.Format("{0}.{1}", e.Source, e.Type)));
            Bus.Subscribe(sender, null, (e) => trace.Add(String.Format("{0}.{1}", e.Source, e.Type)));

            Bus.Broadcast(sender, "Test", null);
            Bus.Broadcast(sender, "Baz", null);
            Bus.Broadcast("Bar", "Test", null);
            Bus.Broadcast("Bar", "Baz", null);

            Assert.AreEqual(new string[] { 
                "Foo.Test",
                "Foo.Test", // Event wasn't consumed so we get it twice
                "Foo.Baz",
                "Bar.Test",
                }, trace.ToArray()
            );
        }

        [Test]
        public void TestConsumePrecise () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe(sender, "Test", (e) => {
                trace.Add(String.Format("a:{0}", e.Type));
            });
            Bus.Subscribe(sender, "Test", (e) => {
                trace.Add(String.Format("b:{0}", e.Type));
                e.Consume();
            });
            Bus.Subscribe(sender, "Test", (e) => {
                trace.Add(String.Format("c:{0}", e.Type));
            });

            Bus.Broadcast(sender, "Test", null);

            Assert.AreEqual(new string[] { 
                "c:Test",
                "b:Test"
                }, trace.ToArray()
            );
        }

        [Test]
        public void TestConsumeGeneral () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe(sender, "Test", (e) => {
                trace.Add(String.Format("a:{0}", e.Type));
            });
            Bus.Subscribe(sender, "Test", (e) => {
                trace.Add(String.Format("b:{0}", e.Type));
                e.Consume();
            });

            Bus.Subscribe(null, "Test", (e) => {
                trace.Add(String.Format("c:{0}", e.Type));
                e.Consume();
            });
            Bus.Subscribe(null, "Test", (e) => {
                trace.Add(String.Format("d:{0}", e.Type));
            });

            Bus.Broadcast(sender, "Test", null);

            Assert.AreEqual(new string[] { 
                "d:Test",
                "c:Test"
                }, trace.ToArray()
            );
        }

        [Test]
        public void TestUnsubscribe () {
            var trace = new List<string>();
            var sender = "Foo";

            var handlers = new EventSubscriber[] {
                (e) => trace.Add("a"),
                (e) => trace.Add("b"),
                (e) => trace.Add("c")
            };

            foreach (var handler in handlers)
                Bus.Subscribe(sender, "Test", handler);

            Bus.Unsubscribe(sender, "Test", handlers[1]);

            Bus.Broadcast(sender, "Test", null);
            Assert.AreEqual(new string[] { "c", "a" }, trace.ToArray());
        }

        [Test]
        public void TestDisposeSubscription () {
            var trace = new List<string>();
            var sender = "Foo";

            var handlers = new EventSubscriber[] {
                (e) => trace.Add("a"),
                (e) => trace.Add("b"),
                (e) => trace.Add("c")
            };

            var subscriptions = (from handler in handlers select Bus.Subscribe(sender, "Test", handler)).ToArray();

            subscriptions[1].Dispose();

            Bus.Broadcast(sender, "Test", null);
            Assert.AreEqual(new string[] { "c", "a" }, trace.ToArray());
        }

        [Test]
        public void TestEventThunk () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe(sender, "Test", (e) => trace.Add(e.Type));

            var thunk = Bus.GetThunk(sender, "Test");
            thunk.Broadcast();

            Assert.AreEqual(new string[] { "Test" }, trace.ToArray());
        }

        [Test]
        public void TestTypedSubscription () {
            var trace = new List<string>();
            var sender = "Foo";

            Bus.Subscribe<string>(sender, "Test", (e, text) => trace.Add(text));

            Bus.Broadcast(sender, "Test", "pancakes");
            Bus.Broadcast(sender, "Test", 5);

            Assert.AreEqual(new string[] { "pancakes" }, trace.ToArray());
        }

        class EventTracer {
            public List<String> Trace = new List<string>();

            public void EventHandler (EventInfo e) {
                Trace.Add(e.Arguments.ToString());
            }
        }

        [Test]
        public void TestWeakSubscriber () {
            var tracer = new EventTracer();
            var sender = "Foo";

            {
                EventSubscriber subscriber = tracer.EventHandler;

                Bus.Subscribe(sender, "Foo", subscriber);

                Bus.Broadcast(sender, "Foo", "Bar");
            }

            GC.Collect();

            Bus.Broadcast(sender, "Foo", "Baz");

            Assert.AreEqual(
                new string[] { "Bar" }, tracer.Trace.ToArray()
            );
        }

        [Test]
        public void TestWeakSource () {
            var trace = new List<string>();
            var sender = new object();

            Bus.Subscribe(sender, "Foo", (e) => trace.Add(e.Arguments as string));

            Bus.Broadcast(sender, "Foo", "Bar");

            var senderWr = new WeakReference(sender);
            sender = null;

            GC.Collect();

            Assert.IsFalse(senderWr.IsAlive);

            Assert.AreEqual(1, Bus.Compact());
        }

        [TearDown]
        public void TearDown () {
            Bus.Dispose();
            Bus = null;
        }
    }
}
