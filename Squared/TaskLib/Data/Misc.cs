using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Data;

namespace Squared.Task.Data {
    namespace Extensions {
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
    }

    public class Transaction : IDisposable, ISchedulable {
        private ConnectionWrapper _Wrapper;
        private IFuture _Future;
        private bool _Active;

        public Transaction (ConnectionWrapper wrapper) {
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

        void ISchedulable.Schedule (TaskScheduler scheduler, IFuture future) {
            future.Bind(this._Future);
        }
    }
}
