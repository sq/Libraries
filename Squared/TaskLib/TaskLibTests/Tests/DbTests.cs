using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.Data;
using System.Data.SQLite;
using System.Data.Common;

namespace Squared.Task.Data {
    [TestFixture]
    public class MemoryDbTests {
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

            cmd.Dispose();
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

            cmd.Dispose();
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
            cmd.Dispose();
        }

        [Test]
        public void TestConnectionWrapper () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var q = qm.BuildQuery("SELECT COUNT(value) FROM Test WHERE value = ?");

                var f = q.ExecuteScalar(5);
                var result = scheduler.WaitFor(f);

                Assert.AreEqual(result, 1);

                q = qm.BuildQuery("SELECT @p0 - @p1");

                f = q.ExecuteScalar(2, 3);                
                result = scheduler.WaitFor(f);

                Assert.AreEqual(result, -1);

                f = q.ExecuteScalar(new NamedParam { N = "p0", V = 4 }, new NamedParam { N = "p1", V = 3 });
                result = scheduler.WaitFor(f);

                Assert.AreEqual(result, 1);

                f = q.ExecuteScalar(5, new NamedParam { N = "p1", V = 3 });
                result = scheduler.WaitFor(f);

                Assert.AreEqual(result, 2);

                q = qm.BuildQuery("SELECT @parm1 - @parm2");

                f = q.ExecuteScalar(new NamedParam { N = "parm1", V = 1 }, new NamedParam { N = "parm2", V = 2 });
                result = scheduler.WaitFor(f);

                Assert.AreEqual(result, -1);
            }
        }

        [Test]
        public void TestDbTaskIterator () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var q = qm.BuildQuery("SELECT value FROM Test WHERE value = ?");

                using (var iterator = new DbTaskIterator(q, 5)) {
                    scheduler.WaitFor(scheduler.Start(iterator.Start()));

                    Assert.AreEqual(iterator.Current.GetInt32(0), 5);
                }
            }
        }

        [Test]
        public void TestQueryPipelining () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");
            for (int i = 0; i < 100; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var q1 = qm.BuildQuery("SELECT value FROM test");
                var q2 = qm.BuildQuery("INSERT INTO test (value) VALUES (?)");

                var iterator = new DbTaskIterator(q1);
                var f1 = scheduler.Start(iterator.Start());
                var f2 = q2.ExecuteNonQuery(200);

                f1.RegisterOnComplete((f, r, e) => {
                    Assert.IsNull(e);
                    Assert.AreEqual(f1, f);
                    Assert.AreEqual(true, r);
                    Assert.IsTrue(f1.Completed);
                    Assert.IsFalse(f2.Completed);
                });

                f2.RegisterOnComplete((f, r, e) => {
                    Assert.IsNull(e);
                    Assert.AreEqual(f2, f);
                    Assert.IsTrue(f1.Completed);
                    Assert.IsTrue(f2.Completed);
                });

                scheduler.WaitFor(f1);

                scheduler.WaitFor(scheduler.Start(new Sleep(1.0)));
                Assert.IsFalse(f2.Completed);

                iterator.Dispose();

                scheduler.WaitFor(f2);
            }
        }

        [Test]
        public void TestTransactionPipelining () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var getNumValues = qm.BuildQuery("SELECT COUNT(value) FROM test");

                var addValue = qm.BuildQuery("INSERT INTO test (value) VALUES (?)");

                var t = qm.CreateTransaction();
                var fq = addValue.ExecuteNonQuery(1);
                var fr = t.Rollback();

                scheduler.WaitFor(Future.WaitForAll(t, fq, fr));

                var fgnv = getNumValues.ExecuteScalar();
                long numValues = Convert.ToInt64(
                    scheduler.WaitFor(fgnv)
                );
                Assert.AreEqual(0, numValues);

                t = qm.CreateTransaction();
                fq = addValue.ExecuteNonQuery(1);
                var fc = t.Commit();

                scheduler.WaitFor(Future.WaitForAll(t, fq, fc));

                fgnv = getNumValues.ExecuteScalar();
                numValues = Convert.ToInt64(
                    scheduler.WaitFor(fgnv)
                );
                Assert.AreEqual(1, numValues);
            }
        }

        IEnumerator<object> CrashyTransactionTask (ConnectionWrapper cw, Query addValue) {
            using (var trans = cw.CreateTransaction()) {
                yield return addValue.ExecuteNonQuery(1);
                yield return addValue.ExecuteNonQuery();
                yield return trans.Commit();
            }
        }

        [Test]
        public void TestTransactionAutoRollback () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var getNumValues = qm.BuildQuery("SELECT COUNT(value) FROM test");

                var addValue = qm.BuildQuery("INSERT INTO test (value) VALUES (?)");

                var f = scheduler.Start(CrashyTransactionTask(qm, addValue));
                try {
                    scheduler.WaitFor(f);
                    Assert.Fail("Did not throw");
                } catch (FutureException fe) {
                    Exception inner = fe.InnerException;
                    Assert.IsInstanceOfType(typeof(InvalidOperationException), inner);
                }

                var fgnv = getNumValues.ExecuteScalar();
                long numValues = Convert.ToInt64(
                    scheduler.WaitFor(fgnv)
                );
                Assert.AreEqual(0, numValues);
            }
        }

        [Test]
        public void TestNestedTransactions () {
            DoQuery("CREATE TEMPORARY TABLE Test (value int)");

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var getNumValues = qm.BuildQuery("SELECT COUNT(value) FROM test");

                var addValue = qm.BuildQuery("INSERT INTO test (value) VALUES (?)");

                var t = qm.CreateTransaction();
                var fq = addValue.ExecuteNonQuery(1);
                var t2 = qm.CreateTransaction();
                var fq2 = addValue.ExecuteNonQuery(2);
                var fr = t2.Rollback();
                var fc = t.Commit();

                scheduler.WaitFor(Future.WaitForAll(t, fq, t2, fq2, fr, fc));

                var fgnv = getNumValues.ExecuteScalar();
                long numValues = Convert.ToInt64(
                    scheduler.WaitFor(fgnv)
                );
                Assert.AreEqual(0, numValues);

                t = qm.CreateTransaction();
                fq = addValue.ExecuteNonQuery(1);
                t2 = qm.CreateTransaction();
                fq2 = addValue.ExecuteNonQuery(2);
                var fc2 = t2.Commit();
                fc = t.Commit();

                scheduler.WaitFor(Future.WaitForAll(t, fq, t2, fq2, fc2, fc));

                fgnv = getNumValues.ExecuteScalar();
                numValues = Convert.ToInt64(
                    scheduler.WaitFor(fgnv)
                );
                Assert.AreEqual(2, numValues);
            }
        }
    }

    [TestFixture]
    public class DiskDbTests {
        SQLiteConnection Connection;

        [SetUp]
        public void SetUp () {
            string filename = System.IO.Path.GetTempFileName();
            Connection = new SQLiteConnection(String.Format("Data Source={0}", filename));
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
        public void TestCloneConnectionWrapper () {
            DoQuery("DROP TABLE IF EXISTS Test");
            DoQuery("CREATE TABLE Test (value int)");
            for (int i = 0; i < 10; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var f = qm.Clone();
                using (var dupe = (ConnectionWrapper)scheduler.WaitFor(f)) {
                    var q = dupe.BuildQuery("SELECT COUNT(value) FROM Test WHERE value = ?");
                    f = q.ExecuteScalar(5);
                    var result = scheduler.WaitFor(f);
                    Assert.AreEqual(result, 1);
                }
            }
        }

        [Test]
        public void TestClonePipelining () {
            DoQuery("DROP TABLE IF EXISTS Test");
            DoQuery("CREATE TABLE Test (value int)");
            for (int i = 0; i < 10; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var q = qm.BuildQuery("SELECT * FROM Test");
                var iter = new DbTaskIterator(q);
                var iterF = scheduler.Start(iter.Start());
                var f = qm.Clone();

                Assert.IsFalse(f.Completed);

                iter.Dispose();
                iterF.Dispose();
                scheduler.WaitFor(f);
                using (var dupe = (ConnectionWrapper)f.Result) {
                    q = dupe.BuildQuery("SELECT COUNT(value) FROM Test WHERE value = ?");
                    f = q.ExecuteScalar(5);
                    var result = scheduler.WaitFor(f);
                    Assert.AreEqual(result, 1);
                }
            }
        }

        [Test]
        public void TestDisposal () {
            DoQuery("DROP TABLE IF EXISTS Test");
            DoQuery("CREATE TABLE Test (value int)");
            for (int i = 0; i < 10; i++)
                DoQuery(String.Format("INSERT INTO Test (value) VALUES ({0})", i));

            DbTaskIterator iter;
            Future f;

            using (var scheduler = new TaskScheduler())
            using (var qm = new ConnectionWrapper(scheduler, Connection)) {
                var q = qm.BuildQuery("SELECT * FROM Test");
                var q2 = qm.BuildQuery("SELECT COUNT(*) FROM Test");
                iter = new DbTaskIterator(q);
                scheduler.Start(iter.Start());
                f = q2.ExecuteScalar();
            }

            iter.Dispose();

            try {
                int count = (int)f.Result;
                Assert.Fail("Future's result was not a ConnectionDisposedException");
            } catch (FutureException fe) {
                Assert.IsInstanceOfType(typeof(ConnectionDisposedException), fe.InnerException);
            }
        }
    }
}
