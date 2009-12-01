using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Data.Common;
using System.Data;
using System.Text.RegularExpressions;

namespace Squared.Task.Data {
    public class ConnectionDisposedException : Exception {
        public ConnectionDisposedException() :
            base("The connection was disposed.") {
        }
    }

    public class DbTaskIterator : TaskIterator<DbDataRecord> {
        class StartThunk : ISchedulable {
            DbTaskIterator _Iterator;

            public StartThunk (DbTaskIterator iterator) {
                _Iterator = iterator;
            }

            public void Schedule (TaskScheduler scheduler, IFuture future) {
                DbTaskIterator i = _Iterator;
                i._CompletionNotifier = i.Query.GetCompletionNotifier();
                i._QueryFuture = i.Query.ExecuteReader(i.Parameters);
                i._QueryFuture.RegisterOnComplete(
                    (f) => {
                        var e = f.Error;
                        if (e != null) {
                            future.SetResult(null, e);
                            return;
                        }

                        try {
                            var qdr = (QueryDataReader)f.Result;
                            var reader = qdr.Reader;
                            var enumerator = new DbEnumerator(reader, true);
                            var task = EnumeratorExtensionMethods.EnumerateViaThreadpool(enumerator);
                            i._Task = task;
                            i.Initialize(scheduler);
                            if (i.Future != null) {
                                i.Future.RegisterOnComplete((_) => {
                                    reader.Dispose();
                                });
                                i.Future.RegisterOnDispose((_) => {
                                    reader.Dispose();
                                });
                                future.Bind(i.MoveNext());
                            } else {
                                reader.Dispose();
                                future.Complete();
                            }
                        } catch (Exception ex) {
                            future.SetResult(null, ex);
                        }
                    }
                );
            }
        };

        Action<IFuture> _CompletionNotifier = null;
        IFuture _QueryFuture = null;
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
                            f.Fail(e);
                        }
                    }
                )
            );
            return f;
        }

        public static Future<int> AsyncExecuteNonQuery (this IDbCommand cmd) {
            var f = new Future<int>();
            ThreadPool.QueueUserWorkItem(
                (WaitCallback)(
                    (state) => {
                        try {
                            int result;
                            result = cmd.ExecuteNonQuery();
                            f.SetResult(result, null);
                        } catch (Exception e) {
                            f.Fail(e);
                        }
                    }
                )
            );
            return f;
        }

        public static Future<IDataReader> AsyncExecuteReader (this IDbCommand cmd) {
            var f = new Future<IDataReader>();
            ThreadPool.QueueUserWorkItem(
                (WaitCallback)(
                    (state) => {
                        try {
                            IDataReader result;
                            result = cmd.ExecuteReader();
                            f.SetResult(result, null);
                        } catch (Exception e) {
                            f.Fail(e);
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

    public class QueryDataReader : IDisposable {
        public readonly Query Query;
        public readonly IDataReader Reader;
        public readonly IFuture Future;
        private readonly Action<IFuture> CompletionNotifier;

        public QueryDataReader (Query query, IDataReader reader, IFuture future) {
            Query = query;
            Reader = reader;
            CompletionNotifier = Query.GetCompletionNotifier();
            Future = future;
        }

        public void Dispose () {
            CompletionNotifier(Future);
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

        private Action GetExecuteFunc<T> (object[] parameters, Func<IFuture, T> queryFunc, Future<T> future) {
            WaitCallback wrapper = (_) => {
                try {
                    BindParameters(parameters);
                    T result = queryFunc(future);
                    future.SetResult(result, null);
                } catch (Exception e) {
                    future.Fail(e);
                }
            };
            Action ef = () => {
                ThreadPool.QueueUserWorkItem(wrapper);
            };
            return ef;
        }

        private Future<T> InternalExecuteQuery<T> (object[] parameters, Func<IFuture, T> queryFunc, bool suspendCompletion) {
            if (_Manager.Closed)
                return null;

            ValidateParameters(parameters);
            var f = new Future<T>();
            Action ef = GetExecuteFunc(parameters, queryFunc, f);
            if (!suspendCompletion) {
                OnComplete oc = (_) => {
                    _Manager.NotifyQueryCompleted(f);
                };
                f.RegisterOnComplete(oc);
            }
            _Manager.EnqueueQuery(f, ef);
            return f;
        }

        internal Action<IFuture> GetCompletionNotifier() {
            Action<IFuture> cn = (f) => _Manager.NotifyQueryCompleted(f);
            return cn;
        }

        public Future<int> ExecuteNonQuery (params object[] parameters) {
            Func<IFuture, int> queryFunc = (f) => {
                _Manager.SetActiveQueryObject(this);
                return _Command.ExecuteNonQuery();
            };
            return InternalExecuteQuery(parameters, queryFunc, false);
        }

        public Future<object> ExecuteScalar (params object[] parameters) {
            Func<IFuture, object> queryFunc = (f) => {
                _Manager.SetActiveQueryObject(this);
                return _Command.ExecuteScalar();
            };
            return InternalExecuteQuery(parameters, queryFunc, false);
        }

        public Future<T> ExecuteScalar<T> (params object[] parameters) {
            Func<IFuture, T> queryFunc = (f) => {
                _Manager.SetActiveQueryObject(this);
                return (T)_Command.ExecuteScalar();
            };
            return InternalExecuteQuery(parameters, queryFunc, false);
        }

        public Future<QueryDataReader> ExecuteReader (params object[] parameters) {
            Func<IFuture, QueryDataReader> queryFunc = (f) => {
                _Manager.SetActiveQueryObject(this);
                return new QueryDataReader(this, _Command.ExecuteReader(), f);
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

    public class Transaction : IDisposable, ISchedulable {
        private ConnectionWrapper _Wrapper;
        private IFuture _Future;
        private bool _Active;

        public Transaction(ConnectionWrapper wrapper) {
            _Wrapper = wrapper;
            _Future = _Wrapper.BeginTransaction();
            _Active = true;
        }

        public IFuture Future {
            get {
                return _Future;
            }
        }

        public IFuture Commit () {
            if (!_Active)
                return null;

            _Active = false;
            return _Wrapper.CommitTransaction();
        }

        public IFuture Rollback () {
            if (!_Active)
                return null;

            _Active = false;
            return _Wrapper.RollbackTransaction();
        }

        public void Dispose () {
            if (_Active)
                _Wrapper.RollbackTransaction();
        }

        void ISchedulable.Schedule(TaskScheduler scheduler, IFuture future) {
            future.Bind(this._Future);
        }
    }

    public class ConnectionWrapper : IDisposable {
        struct WaitingQuery {
            public IFuture Future;
            public Action ExecuteFunc;
        }

        static Regex
            _NormalParameter = new Regex(@"(^|\s|[\(,=+-><])\?($|\s|[\),=+-><])", RegexOptions.Compiled),
            _NamedParameter = new Regex(@"(^|\s|[\(,=+-><])\@(?'name'[a-zA-Z0-9_]+)($|\s|[\),=+-><])", RegexOptions.Compiled);

        IDbConnection _Connection;
        TaskScheduler _Scheduler;

        bool _Closing = false;
        bool _OwnsConnection = false;
        IFuture _ActiveQuery = null;
        Query _ActiveQueryObject = null;
        Query _BeginTransaction, _CommitTransaction, _RollbackTransaction;
        int _TransactionDepth = 0;
        bool _TransactionFailed = false;
        Queue<WaitingQuery> _WaitingQueries = new Queue<WaitingQuery>();

        public ConnectionWrapper (TaskScheduler scheduler, IDbConnection connection)
            : this(scheduler, connection, false) {
        }

        public ConnectionWrapper (TaskScheduler scheduler, IDbConnection connection, bool ownsConnection) {
            _Scheduler = scheduler;
            _Connection = connection;
            _OwnsConnection = ownsConnection;

            _BeginTransaction = BuildQuery("BEGIN");
            _CommitTransaction = BuildQuery("COMMIT");
            _RollbackTransaction = BuildQuery("ROLLBACK");
        }

        public Transaction CreateTransaction () {
            return new Transaction(this);
        }

        internal bool Closed {
            get {
                lock (this)
                    return (_Closing) || (_Connection == null) || (_Connection.State == ConnectionState.Closed);
            }
        }

        internal IFuture BeginTransaction () {
            lock (this) {
                _TransactionDepth += 1;

                if (_TransactionDepth == 1) {
                    _TransactionFailed = false;
                    return _BeginTransaction.ExecuteNonQuery();
                } else
                    return new Future(null);
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
                    return new Future(null);
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
                    return new Future(null);
                } else {
                    _TransactionDepth = 0;
                    throw new InvalidOperationException("No transaction active");
                }
            }
        }

        internal void EnqueueQuery (IFuture future, Action executeFunc) {
            if (_Closing)
                return;

            var wq = new WaitingQuery { Future = future, ExecuteFunc = executeFunc };
            lock (this) {
                if (_ActiveQuery == null)
                    IssueQuery(wq);
                else
                    _WaitingQueries.Enqueue(wq);
            }
        }

        internal void SetActiveQueryObject (Query obj) {
            _ActiveQueryObject = obj;
        }

        private void IssueQuery (WaitingQuery waitingQuery) {
            if (_ActiveQuery != null)
                throw new InvalidOperationException("IssueQuery invoked while a query was still active");

            _ActiveQueryObject = null;
            _ActiveQuery = waitingQuery.Future;
            waitingQuery.ExecuteFunc();
        }

        internal void NotifyQueryCompleted (IFuture future) {
            lock (this) {
                if (_ActiveQuery != future) {
                    if (!_Closing)
                        throw new InvalidOperationException("NotifyQueryCompleted invoked by a query that was not active");
                } else {
                    _ActiveQuery = null;
                    _ActiveQueryObject = null;
                }

                if (_WaitingQueries.Count > 0) {
                    var wq = _WaitingQueries.Dequeue();
                    IssueQuery(wq);
                }
            }
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
            lock (this) {
                _Closing = true;

                if (_Connection != null) {
                    while (_ActiveQuery != null) {
                        if (_ActiveQuery.Completed) {
                            break;
                        } else {
                            System.Diagnostics.Debug.WriteLine(String.Format("ConnectionWrapper waiting for enqueued query {0}", _ActiveQuery));
                        }
                        Monitor.Exit(this);
                        yield return new Yield();
                        Monitor.Enter(this);
                    }

                    while (_TransactionDepth > 0) {
                        System.Diagnostics.Debug.WriteLine("ConnectionWrapper.Dispose rolling back active transaction");
                        Monitor.Exit(this);
                        yield return RollbackTransaction();
                        Monitor.Enter(this);
                    }

                    while (_WaitingQueries.Count > 0) {
                        try {
                            _WaitingQueries.Dequeue().Future.SetResult(null, new ConnectionDisposedException());
                        } catch (Exception ex) {
                            System.Diagnostics.Debug.WriteLine(String.Format("Unhandled exception in connection teardown: {0}", ex));
                        }
                    }

                    if (_OwnsConnection)
                        _Connection.Close();

                    _Connection = null;
                }

                _Scheduler = null;
            }
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

        public IFuture Clone () {
            return Clone(null);
        }

        public IFuture Clone (string extraConnectionParameters) {
            var newConnectionString = _Connection.ConnectionString;
            if (extraConnectionParameters != null)
                newConnectionString = newConnectionString + ";" + extraConnectionParameters;

            var newConnection = _Connection.GetType().GetConstructor(System.Type.EmptyTypes).Invoke(null);
            newConnection.GetType().GetProperty("ConnectionString").SetValue(newConnection, newConnectionString, null);
            var openMethod = newConnection.GetType().GetMethod("Open");

            var f = new Future();
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
