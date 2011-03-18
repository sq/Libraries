using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Data;
using System.Threading;
using System.Linq.Expressions;
using Squared.Util;

namespace Squared.Task.Data {
    public class ConnectionDisposedException : Exception {
        public ConnectionDisposedException () :
            base("The connection was disposed.") {
        }
    }

    public class ConnectionWrapper : IDisposable {
        protected struct PendingQuery {
            public readonly Action ExecuteFunc;
            public readonly IFuture Future;

            public PendingQuery (Action executeFunc, IFuture future) {
                Future = future;
                ExecuteFunc = executeFunc;
            }
        }

        static readonly Regex
            _NormalParameter = new Regex(@"(^|\s|[\(,=+-><])\?($|\s|[\),=+-><])", RegexOptions.Compiled),
            _NamedParameter = new Regex(@"(^|\s|[\(,=+-><])\@(?'name'[a-zA-Z0-9_]+)($|\s|[\),=+-><])", RegexOptions.Compiled);

        IDbConnection _Connection;
        TaskScheduler _Scheduler;

        bool _Closing = false;
        readonly bool _OwnsConnection = false;
        IFuture _ActiveQuery = null;
        Query _ActiveQueryObject = null;
        protected readonly Query _BeginTransaction, _CommitTransaction, 
            _RollbackTransaction, _BeginTransactionExclusive;
        int _TransactionDepth = 0;
        bool _TransactionFailed = false;

        protected readonly AtomicQueue<PendingQuery> _QueryQueue = new AtomicQueue<PendingQuery>();
        protected Internal.WorkerThread<AtomicQueue<PendingQuery>> _QueryThread;

        public ConnectionWrapper (TaskScheduler scheduler, IDbConnection connection)
            : this(scheduler, connection, false) {
        }

        public ConnectionWrapper (TaskScheduler scheduler, IDbConnection connection, bool ownsConnection) {
            _Scheduler = scheduler;
            _Connection = connection;
            _OwnsConnection = ownsConnection;

            _BeginTransaction = BuildQuery("BEGIN");
            _BeginTransactionExclusive = BuildQuery("BEGIN EXCLUSIVE");
            _CommitTransaction = BuildQuery("COMMIT");
            _RollbackTransaction = BuildQuery("ROLLBACK");
        }

        public Transaction CreateTransaction () {
            return new Transaction(this);
        }

        public Transaction CreateTransaction (bool exclusive) {
            return new Transaction(this, exclusive);
        }

        public TaskScheduler Scheduler {
            get {
                return _Scheduler;
            }
        }

        internal bool Closed {
            get {
                lock (this)
                    return (_Closing) || (_Connection == null) || (_Connection.State == ConnectionState.Closed);
            }
        }

        internal IFuture BeginTransaction () {
            return BeginTransaction(false);
        }

        internal IFuture BeginTransaction (bool exclusive) {
            lock (this) {
                _TransactionDepth += 1;

                if (_TransactionDepth == 1) {
                    _TransactionFailed = false;
                    if (exclusive)
                        return _BeginTransactionExclusive.ExecuteNonQuery();
                    else
                        return _BeginTransaction.ExecuteNonQuery();
                } else
                    return Future<object>.Default;
            }
        }

        internal IFuture CommitTransaction () {
            lock (this) {
                _TransactionDepth -= 1;

                if (_TransactionDepth == 0) {
                    if (_TransactionFailed)
                        return _RollbackTransaction.ExecuteNonQuery();
                    else
                        return _CommitTransaction.ExecuteNonQuery();
                } else if (_TransactionDepth > 0)
                    return Future<object>.Default;
                else {
                    _TransactionDepth = 0;
                    throw new InvalidOperationException("No transaction active");
                }
            }
        }

        internal IFuture RollbackTransaction () {
            lock (this) {
                _TransactionDepth -= 1;

                if (_TransactionDepth == 0)
                    return _RollbackTransaction.ExecuteNonQuery();
                else if (_TransactionDepth > 0) {
                    _TransactionFailed = true;
                    return Future<object>.Default;
                } else {
                    _TransactionDepth = 0;
                    throw new InvalidOperationException("No transaction active");
                }
            }
        }

        internal void EnqueueQuery (IFuture future, Action executeFunc) {
            lock (this)
            if (!_Closing) {
                _QueryQueue.Enqueue(new PendingQuery(executeFunc, future));

                WakeQueryThread();

                return;
            }

            future.SetResult(null, new ConnectionDisposedException());
        }

        protected void QueryThreadFunc (AtomicQueue<PendingQuery> workItems, ManualResetEvent newWorkItemEvent) {
            while (true) {
                PendingQuery item;
                while ((_ActiveQuery == null) && _QueryQueue.Dequeue(out item)) {
                    NotifyQueryBegan(item.Future);
                    try {
                        lock (this)
                            item.ExecuteFunc();
                    } catch (Exception ex) {
                        item.Future.SetResult(null, ex);
                    }
                }

                newWorkItemEvent.WaitOne();
                newWorkItemEvent.Reset();
            }
        }

        internal void WakeQueryThread () {
            if (_QueryThread == null)
                _QueryThread = new Internal.WorkerThread<AtomicQueue<PendingQuery>>(QueryThreadFunc, ThreadPriority.Normal);

            _QueryThread.Wake();
        }

        internal void SetActiveQueryObject (Query obj) {
            _ActiveQueryObject = obj;
        }

        internal void NotifyQueryBegan (IFuture future) {
            var previousQuery = Interlocked.Exchange(ref _ActiveQuery, future);
            if (previousQuery != null)
                throw new InvalidOperationException("NotifyQueryBegan invoked while a query was still active");
        }

        internal void NotifyQueryCompleted (IFuture future) {
            var aq = Interlocked.Exchange(ref _ActiveQuery, null);
            if (aq != future) {
                if (!_Closing)
                    throw new InvalidOperationException("NotifyQueryCompleted invoked by a query that was not active");
            }

            _ActiveQueryObject = null;
            WakeQueryThread();
        }

        public Future<object> ExecuteScalar (string sql, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteScalar(parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Future<T> ExecuteScalar<T> (string sql, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteScalar<T>(parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Future<T> ExecuteScalar<T> (string sql, Expression<Func<T>> target, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteScalar<T>(target, parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Future<T[]> ExecuteArray<T> (string sql, params object[] parameters)
            where T : class, new() {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteArray<T>(parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Future<T[]> ExecutePrimitiveArray<T> (string sql, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecutePrimitiveArray<T>(parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Future<int> ExecuteSQL (string sql, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteNonQuery(parameters);
            f.RegisterOnComplete((_) => cmd.Dispose());
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Query BuildQuery (string sql) {
            IDbCommand cmd;

            lock (this)
                cmd = _Connection.CreateCommand();

            cmd.CommandText = sql;

            int numParameters = _NormalParameter.Matches(sql).Count;
            var parameterNames = new List<string>();
            for (int i = 0; i < numParameters; i++) {
                parameterNames.Add(String.Format("p{0}", i));
            }
            foreach (Match match in _NamedParameter.Matches(sql)) {
                string name = match.Groups["name"].Value;
                if (!parameterNames.Contains(name)) {
                    numParameters += 1;
                    parameterNames.Add(name);
                }
            }

            for (int i = 0; i < numParameters; i++) {
                var param = cmd.CreateParameter();
                param.ParameterName = parameterNames[i];
                cmd.Parameters.Add(param);
            }

            return new Query(this, cmd);
        }

        public IEnumerator<object> Dispose () {
            lock (this)
                _Closing = true;

            if (_Connection != null) {
                var exc = new ConnectionDisposedException();
                PendingQuery query;
                while (_QueryQueue.Dequeue(out query)) {
                    try {
                        query.Future.SetResult(null, exc);
                    } catch (Exception ex) {
                        System.Diagnostics.Debug.WriteLine(
                            String.Format("Unhandled exception in connection teardown: {0}", ex)
                        );
                    }
                }

                while (_TransactionDepth > 0) {
                    System.Diagnostics.Debug.WriteLine("ConnectionWrapper.Dispose rolling back active transaction");

                    yield return RollbackTransaction();
                }

                lock (this) {
                    if (_OwnsConnection)
                        _Connection.Close();

                    _Connection = null;
                }
            }

            _QueryThread.Dispose();
            _Scheduler = null;
        }

        void IDisposable.Dispose () {
            var s = _Scheduler;
            var f = s.Start(Dispose(), TaskExecutionPolicy.RunAsBackgroundTask);

            try {
                s.WaitFor(f);
            } catch (Exception ex) {
                System.Windows.Forms.MessageBox.Show(ex.ToString(), "Disposal error");
            }
        }

        public Future<ConnectionWrapper> Clone () {
            return Clone(null);
        }

        public virtual Future<ConnectionWrapper> Clone (string extraConnectionParameters) {
            var newConnectionString = _Connection.ConnectionString;
            if (extraConnectionParameters != null)
                newConnectionString = newConnectionString + ";" + extraConnectionParameters;

            var newConnection = _Connection.GetType().GetConstructor(System.Type.EmptyTypes).Invoke(null);
            newConnection.GetType().GetProperty("ConnectionString").SetValue(newConnection, newConnectionString, null);
            var openMethod = newConnection.GetType().GetMethod("Open");

            var f = new Future<ConnectionWrapper>();
            f.RegisterOnComplete((_) => {
                NotifyQueryCompleted(f);
            });

            Action openAndReturn = () => {
                try {
                    openMethod.Invoke(newConnection, null);
                    var result = new ConnectionWrapper(_Scheduler, (IDbConnection)newConnection, true);
                    f.SetResult(result, null);
                } catch (Exception e) {
                    f.SetResult(null, e);
                }
            };

            EnqueueQuery(f, openAndReturn);
            return f;
        }
    }
}
