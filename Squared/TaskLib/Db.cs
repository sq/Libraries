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
        static Regex 
            _NormalParameter = new Regex(@"(^|\s)\?($|\s)", RegexOptions.Compiled),
            _NamedParameter = new Regex(@"(^|\s)\@(?'name'[a-zA-Z0-9_]+)($|\s)", RegexOptions.Compiled);
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

            for (int i = 0; i <numParameters; i++) {
                var param = cmd.CreateParameter();
                param.ParameterName = parameterNames[i];
                cmd.Parameters.Add(param);
            }

            return new Query(this, cmd);
        }

        public void Dispose () {
            _Connection = null;
        }
    }
}