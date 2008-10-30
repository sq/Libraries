using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Data.Common;
using System.Data;
using System.Text.RegularExpressions;

namespace Squared.Task {
    public class DbTaskIterator : TaskIterator<DbDataRecord> {
        struct StartThunk : ISchedulable {
            DbTaskIterator _Iterator;

            public StartThunk (DbTaskIterator iterator) {
                _Iterator = iterator;
            }

            public void Schedule (TaskScheduler scheduler, Future future) {
                DbTaskIterator i = _Iterator;
                i._CompletionNotifier = i.Query.GetCompletionNotifier();
                i._QueryFuture = i.Query.ExecuteReader(i.Parameters);
                i._QueryFuture.RegisterOnComplete(
                    (f, r, e) => {
                        if (e != null) {
                            future.SetResult(null, e);
                            return;
                        }

                        try {
                            var reader = (IDataReader)r;
                            var enumerator = new DbEnumerator(reader, true);
                            var task = EnumeratorExtensionMethods.EnumerateViaThreadpool(enumerator);
                            i._Task = task;
                            i.Initialize(scheduler);
                            i.Future.RegisterOnComplete((_f, _r, _e) => {
                                reader.Dispose();
                            });
                            i.Future.RegisterOnDispose((_) => {
                                reader.Dispose();
                            });
                            future.Bind(i.MoveNext());
                        } catch (Exception ex) {
                            future.SetResult(null, ex);
                        }
                    }
                );
            }
        };

        Action<Future> _CompletionNotifier = null;
        Future _QueryFuture = null;
        Query _Query;
        object[] _Parameters;

        public DbTaskIterator (Query query, params object[] parameters)
            : base() {
            _Query = query;
            _Parameters = parameters;
        }

        public Query Query {
            get {
                return _Query;
            }
        }

        public object[] Parameters {
            get {
                return _Parameters;
            }
        }

        protected override void OnDispose () {
            if ((_CompletionNotifier != null) && (_QueryFuture != null)) {
                try {
                    _QueryFuture.Dispose();
                } catch (FutureDisposedException) {
                }
                _CompletionNotifier(_QueryFuture);
            }

            _QueryFuture = null;
            _CompletionNotifier = null;

            base.OnDispose();
        }

        protected override ISchedulable GetStartThunk () {
            return new StartThunk(this);
        }
    }

    public static class DbExtensionMethods {
        public static Future AsyncExecuteScalar (this IDbCommand cmd) {
            var f = new Future();
            ThreadPool.QueueUserWorkItem(
                (WaitCallback)(
                    (state) => {
                        try {
                            object result;
                            result = cmd.ExecuteScalar();
                            f.SetResult(result, null);
                        } catch (Exception e) {
                            f.SetResult(null, e);
                        }
                    }
                )
            );
            return f;
        }

        public static Future AsyncExecuteNonQuery (this IDbCommand cmd) {
            var f = new Future();
            ThreadPool.QueueUserWorkItem(
                (WaitCallback)(
                    (state) => {
                        try {
                            int result;
                            result = cmd.ExecuteNonQuery();
                            f.SetResult(result, null);
                        } catch (Exception e) {
                            f.SetResult(null, e);
                        }
                    }
                )
            );
            return f;
        }

        public static Future AsyncExecuteReader (this IDbCommand cmd) {
            var f = new Future();
            ThreadPool.QueueUserWorkItem(
                (WaitCallback)(
                    (state) => {
                        try {
                            IDataReader result;
                            result = cmd.ExecuteReader();
                            f.SetResult(result, null);
                        } catch (Exception e) {
                            f.SetResult(null, e);
                        }
                    }
                )
            );
            return f;
        }
    }

    public struct NamedParam {
        public string Name;
        public object Value;

        public string N {
            set {
                Name = value;
            }
        }

        public object V {
            set {
                Value = value;
            }
        }
    }

    public class Query : IDisposable {
        ConnectionWrapper _Manager;
        IDbCommand _Command;

        internal Query (ConnectionWrapper manager, IDbCommand command) {
            _Manager = manager;
            _Command = command;
        }

        internal void ValidateParameters (object[] parameters) {
            if (parameters.Length != _Command.Parameters.Count) {
                string errorString = String.Format("Got {0} parameter(s), expected {1}.", parameters.Length, _Command.Parameters.Count);
                throw new InvalidOperationException(errorString);
            }

            for (int i = 0; i < parameters.Length; i++) {
                var value = parameters[i];
                if (value is NamedParam) {
                    var namedParam = (NamedParam)value;
                    var parameter = (IDbDataParameter)_Command.Parameters[namedParam.Name];
                }
            }
        }

        internal void BindParameters (object[] parameters) {
            for (int i = 0; i < parameters.Length; i++) {
                var value = parameters[i];
                if (value is NamedParam) {
                    var namedParam = (NamedParam)value;
                    var parameter = (IDbDataParameter)_Command.Parameters[namedParam.Name];
                    parameter.Value = namedParam.Value;
                } else {
                    var parameter = (IDbDataParameter)_Command.Parameters[i];
                    parameter.Value = value;
                }
            }
        }

        private Action GetExecuteFunc (object[] parameters, Func<object> queryFunc, Future future) {
            WaitCallback wrapper = (_) => {
                try {
                    BindParameters(parameters);
                    object result = queryFunc();
                    future.SetResult(result, null);
                } catch (Exception e) {
                    future.SetResult(null, e);
                }
            };
            Action ef = () => {
                ThreadPool.QueueUserWorkItem(wrapper);
            };
            return ef;
        }

        private Future InternalExecuteQuery (object[] parameters, Func<object> queryFunc, bool suspendCompletion) {
            ValidateParameters(parameters);
            var f = new Future();
            Action ef = GetExecuteFunc(parameters, queryFunc, f);
            if (!suspendCompletion) {
                OnComplete oc = (_, r, e) => {
                    _Manager.NotifyQueryCompleted(f);
                };
                f.RegisterOnComplete(oc);
            }
            _Manager.EnqueueQuery(f, ef);
            return f;
        }

        public Future ExecuteNonQuery (params object[] parameters) {
            Func<object> queryFunc = () => {
                return _Command.ExecuteNonQuery();
            };
            return InternalExecuteQuery(parameters, queryFunc, false);
        }

        public Future ExecuteScalar (params object[] parameters) {
            Func<object> queryFunc = () => {
                return _Command.ExecuteScalar();
            };
            return InternalExecuteQuery(parameters, queryFunc, false);
        }

        internal Action<Future> GetCompletionNotifier () {
            Action<Future> cn = (f) => {
                _Manager.NotifyQueryCompleted(f);
            };
            return cn;
        }

        internal Future ExecuteReader (params object[] parameters) {
            Func<object> queryFunc = () => {
                return _Command.ExecuteReader();
            };
            return InternalExecuteQuery(parameters, queryFunc, true);
        }

        public IDbCommand Command {
            get {
                return _Command;
            }
        }

        public void Dispose () {
            if (_Command != null) {
                _Command.Dispose();
                _Command = null;
            }
            _Manager = null;
        }
    }

    public class ConnectionWrapper : IDisposable {
        struct WaitingQuery {
            public Future Future;
            public Action ExecuteFunc;
        }

        static Regex
            _NormalParameter = new Regex(@"(^|\s|[\(,=+-><])\?($|\s|[\),=+-><])", RegexOptions.Compiled),
            _NamedParameter = new Regex(@"(^|\s|[\(,=+-><])\@(?'name'[a-zA-Z0-9_]+)($|\s|[\),=+-><])", RegexOptions.Compiled);

        IDbConnection _Connection;
        TaskScheduler _Scheduler;

        IDbTransaction _Transaction = null;
        object _QueryLock = new object();
        Future _ActiveQuery = null;
        Queue<WaitingQuery> _WaitingQueries = new Queue<WaitingQuery>();

        public ConnectionWrapper (TaskScheduler scheduler, IDbConnection connection) {
            _Scheduler = scheduler;
            _Connection = connection;
        }

        public Future BeginTransaction () {
            return BeginTransaction(IsolationLevel.Unspecified);
        }

        public Future BeginTransaction (IsolationLevel il) {
            var f = new Future();
            WaitCallback wc = (state) => {
                var qm = (ConnectionWrapper)state;
                Exception error = null;
                lock (qm) {
                    if (qm._Transaction == null) {
                        qm._Transaction = qm._Connection.BeginTransaction(il);
                    } else {
                        error = new Exception("A transaction is already in progress");
                    }
                }
                f.SetResult(null, error);
            };
            f.RegisterOnComplete((_, r, e) => {
                NotifyQueryCompleted(f);
            });
            Action ef = () => {
                ThreadPool.QueueUserWorkItem(wc, this);
            };
            EnqueueQuery(f, ef);
            return f;
        }

        public Future CommitTransaction () {
            var f = new Future();
            WaitCallback wc = (state) => {
                var qm = (ConnectionWrapper)state;
                Exception error = null;
                lock (qm) {
                    if (qm._Transaction != null) {
                        qm._Transaction.Commit();
                        qm._Transaction.Dispose();
                        qm._Transaction = null;
                    } else {
                        error = new Exception("No transaction is in progress");
                    }
                }
                f.SetResult(null, error);
            };
            f.RegisterOnComplete((_, r, e) => {
                NotifyQueryCompleted(f);
            });
            Action ef = () => {
                ThreadPool.QueueUserWorkItem(wc, this);
            };
            EnqueueQuery(f, ef);
            return f;
        }

        public Future RollbackTransaction () {
            var f = new Future();
            WaitCallback wc = (state) => {
                var qm = (ConnectionWrapper)state;
                Exception error = null;
                lock (qm) {
                    if (qm._Transaction != null) {
                        qm._Transaction.Rollback();
                        qm._Transaction.Dispose();
                        qm._Transaction = null;
                    } else {
                        error = new Exception("No transaction is in progress");
                    }
                }
                f.SetResult(null, error);
            };
            f.RegisterOnComplete((_, r, e) => {
                NotifyQueryCompleted(f);
            });
            Action ef = () => {
                ThreadPool.QueueUserWorkItem(wc, this);
            };
            EnqueueQuery(f, ef);
            return f;
        }

        internal void EnqueueQuery (Future future, Action executeFunc) {
            var wq = new WaitingQuery { Future = future, ExecuteFunc = executeFunc };
            lock (_QueryLock) {
                if (_ActiveQuery == null)
                    IssueQuery(wq);
                else
                    _WaitingQueries.Enqueue(wq);
            }
        }

        private void IssueQuery (WaitingQuery waitingQuery) {
            if (_ActiveQuery != null)
                throw new InvalidOperationException("IssueQuery invoked while a query was still active");

            _ActiveQuery = waitingQuery.Future;
            waitingQuery.ExecuteFunc();
        }

        internal void NotifyQueryCompleted (Future future) {
            lock (_QueryLock) {
                if (_ActiveQuery != future)
                    throw new InvalidOperationException("NotifyQueryCompleted invoked by a query that was not active");
                else
                    _ActiveQuery = null;

                if (_WaitingQueries.Count > 0) {
                    var wq = _WaitingQueries.Dequeue();
                    IssueQuery(wq);
                }
            }
        }

        public Future ExecuteSQL (string sql, params object[] parameters) {
            var cmd = BuildQuery(sql);
            var f = cmd.ExecuteNonQuery(parameters);
            f.RegisterOnDispose((_) => cmd.Dispose());
            return f;
        }

        public Query BuildQuery (string sql) {
            IDbCommand cmd;

            lock (_Connection)
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

        public void Dispose () {
            if (_Transaction != null) {
                _Transaction.Dispose();
                _Transaction = null;
            }

            _Connection = null;
            _Scheduler = null;
        }
    }
}