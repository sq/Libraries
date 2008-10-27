using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.Data;
using System.Data.SQLite;
using System.Data.Common;

namespace Squared.Task {
    [TestFixture]
    public class DbTests {
        SQLiteConnection Connection;

        [SetUp]
        public void SetUp () {
            Connection = new SQLiteConnection("Data Source=:memory:");
            Connection.Open();
        }

        internal void DoQuery (string sql) {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            cmd.Dispose();
        }

        [TearDown]
        public void TearDown () {
            Connection.Dispose();
            Connection = null;
        }

        [Test]
        public void TestAsyncExecuteScalar () {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            var f = cmd.AsyncExecuteScalar();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(1, f.Result);
        }

        [Test]
        public void TestAsyncExecuteNonQuery () {
            var cmd = Connection.CreateCommand();
            cmd.CommandText = "CREATE TEMPORARY TABLE Test (value int); INSERT INTO Test (value) VALUES (1)";
            var f = cmd.AsyncExecuteNonQuery();
            f.GetCompletionEvent().WaitOne();

            cmd.CommandText = "SELECT value FROM Test LIMIT 1";
            f = cmd.AsyncExecuteScalar();
            f.GetCompletionEvent().WaitOne();
            Assert.AreEqual(1, f.Result);
        }

        [Test]
        public void TestAsyncExecuteReader () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM Test";
            var f = cmd.AsyncExecuteReader();
            f.GetCompletionEvent().WaitOne();

            var reader = (DbDataReader)f.Result;
            Assert.IsTrue(reader.Read());
            Assert.AreEqual(0, reader.GetInt32(0));
            Assert.IsTrue(reader.Read());
            Assert.AreEqual(1, reader.GetInt32(0));

            reader.Dispose();
        }

        [Test]
        public void TestAsyncEnumerateRows () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            var cmd = Connection.CreateCommand();
            cmd.CommandText = "SELECT value FROM Test";
            var reader = cmd.ExecuteReader();

            using (var scheduler = new TaskScheduler(JobQueue.MultiThreaded)) {
                var iterator = reader.AsyncEnumerateRows(scheduler);

                scheduler.WaitFor(iterator.MoveNext());
                Assert.AreEqual(iterator.Current.GetInt32(0), 0);

                scheduler.WaitFor(iterator.MoveNext());
                Assert.AreEqual(iterator.Current.GetInt32(0), 1);

                iterator.Dispose();
            }

            Assert.IsTrue(reader.IsClosed);
        }

        [Test]
        public void TestQueryManager () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            var qm = new QueryManager(Connection);
            var q = qm.BuildQuery("SELECT value FROM Test WHERE value = ?");

            using (var scheduler = new TaskScheduler(JobQueue.MultiThreaded)) {
                var f = q.ExecuteReader(5);

                var reader = (IDataReader)scheduler.WaitFor(f);
                var iterator = reader.AsyncEnumerateRows(scheduler);

                scheduler.WaitFor(iterator.MoveNext());
                Assert.AreEqual(iterator.Current.GetInt32(0), 5);

                iterator.Dispose();
            }
        }
    }
}