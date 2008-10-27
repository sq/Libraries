using System;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using System.Data.Common;
using System.Data;

namespace Squared.Task {
    public class DbTaskIterator : TaskIterator<DbDataRecord> {
        struct StartThunk : ISchedulable {
            DbTaskIterator _Iterator;

            public StartThunk (DbTaskIterator iterator) {
                _Iterator = iterator;
            }

            public void Schedule (TaskScheduler scheduler, Future future) {
                DbTaskIterator i = _Iterator;
                var er = i.Query.ExecuteReader(i.Parameters);
                er.RegisterOnComplete(
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
                            i.Future.RegisterOnComplete((_f,_r,_e) => {
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

        /// <summary>
        /// Yield the result of this function from within a task to initialize the DbTaskIterator and execute the query. The iterator will automatically be advanced to the first item.
        /// </summary>
        public override ISchedulable Start () {
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
                            lock (cmd.Connection)
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
                            lock (cmd.Connection)
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
                            lock (cmd.Connection)
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

        public static TaskIterator<DbDataRecord> AsyncEnumerateRows (this IDataReader reader, TaskScheduler scheduler) {
            var enumerator = new DbEnumerator(reader, true);
            var task = EnumeratorExtensionMethods.EnumerateViaThreadpool(enumerator);
            var iterator = new TaskIterator<DbDataRecord>(scheduler, task);
            iterator.Future.RegisterOnDispose(
                (f) => {
                    reader.Dispose();
                }
            );
            return iterator;
        }
    }

    public class Query : IDisposable {
        QueryManager _Manager;
        IDbCommand _Command;

        internal Query (QueryManager manager, IDbCommand command) {
            _Manager = manager;
            _Command = command;
        }

        internal void BindParameters (object[] parameters) {
            if (parameters.Length != _Command.Parameters.Count) {
                string errorString = String.Format("Got {0} parameter(s), expected {1}.", parameters.Length, _Command.Parameters.Count);
                throw new InvalidOperationException(errorString);
            }
            for (int i = 0; i < parameters.Length; i++) {
                var parameter = (IDbDataParameter)_Command.Parameters[i];
                parameter.Value = parameters[i];
            }
        }

        public Future ExecuteNonQuery (params object[] parameters) {
            BindParameters(parameters);
            return _Command.AsyncExecuteNonQuery();
        }

        public Future ExecuteScalar (params object[] parameters) {
            BindParameters(parameters);
            return _Command.AsyncExecuteScalar();
        }

        public Future ExecuteReader (params object[] parameters) {
            BindParameters(parameters);
            return _Command.AsyncExecuteReader();
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

    public class QueryManager : IDisposable {
        IDbConnection _Connection;

        public QueryManager (IDbConnection connection) {
            _Connection = connection;
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

            int numParameters = 0;
            int pos = 0;
            do {
                pos = sql.IndexOf('?', pos + 1);
                if (pos < 0)
                    break;

                numParameters += 1;
            } while (pos < (sql.Length - 1));

            for (int i = 0; i < numParameters; i++) {
                var param = cmd.CreateParameter();
                param.ParameterName = String.Format("p{0}", i);
                cmd.Parameters.Add(param);
            }

            return new Query(this, cmd);
        }

        public void Dispose () {
            _Connection = null;
        }
    }
}