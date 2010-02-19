using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Threading;
using System.Linq.Expressions;

namespace Squared.Task.Data {
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
            try {
                Reader.Close();
            } catch {
            }
            try {
                Reader.Dispose();
            } catch {
            }
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
            if (_Manager.Closed || _Manager == null)
                return null;

            ValidateParameters(parameters);
            var f = new Future<T>();
            var m = _Manager;

            OnDispose od = (_) => {
                m.NotifyQueryCompleted(f);
            };
            f.RegisterOnDispose(od);

            if (!suspendCompletion) {
                OnComplete oc = (_) => {
                    m.NotifyQueryCompleted(f);
                };

                f.RegisterOnComplete(oc);
            }

            Action ef = GetExecuteFunc(parameters, queryFunc, f);
            m.EnqueueQuery(f, ef);

            return f;
        }

        internal Action<IFuture> GetCompletionNotifier () {
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

        public Future<T> ExecuteScalar<T> (Expression<Func<T>> target, params object[] parameters) {
            var f = ExecuteScalar<T>(parameters);
            f.Bind<T>(target);
            return f;
        }

        public Future<QueryDataReader> ExecuteReader (params object[] parameters) {
            Func<IFuture, QueryDataReader> queryFunc = (f) => {
                _Manager.SetActiveQueryObject(this);
                return new QueryDataReader(this, _Command.ExecuteReader(), f);
            };
            return InternalExecuteQuery(parameters, queryFunc, true);
        }

        public TaskEnumerator<IDataRecord> Execute (params object[] parameters) {
            var fReader = this.ExecuteReader(parameters);

            var e = new TaskEnumerator<IDataRecord>(ExecuteTask(fReader), 1);
            e.OnEarlyDispose = () => {
                fReader.Dispose();

                if (fReader.Completed)
                    fReader.Result.Dispose();
            };

            return e;
        }

        protected IEnumerator<object> ExecuteTask (Future<QueryDataReader> fReader) {
            yield return fReader;

            using (var reader = fReader.Result) {
                Func<bool> moveNext = () =>
                    reader.Reader.Read();
                var nv = new NextValue(reader.Reader);

                while (true) {
                    var f = Future.RunInThread(moveNext);
                    yield return f;

                    if (f.Result == false)
                        break;
                    else
                        yield return nv;
                }
            }
        }

        public TaskEnumerator<T> Execute<T> (params object[] parameters)
            where T : class, new() {
            var fReader = this.ExecuteReader(parameters);

            var e = new TaskEnumerator<T>(ExecuteTask<T>(fReader));
            e.OnEarlyDispose = () => {
                fReader.Dispose();

                if (fReader.Completed)
                    fReader.Result.Dispose();
            };

            return e;
        }

        protected IEnumerator<object> ExecuteTask<T> (Future<QueryDataReader> fReader)
            where T : class, new() {
            yield return fReader;

            using (var reader = fReader.Result) {
                var mapper = new Mapper.Mapper<T>(reader.Reader);

                using (var e = EnumeratorExtensionMethods.EnumerateViaThreadpool(
                    mapper.ReadSequence(), TaskEnumerator<T>.DefaultBufferSize
                ))
                while (e.MoveNext()) {
                    var v = e.Current;

                    yield return v;
                }
            }
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
}
