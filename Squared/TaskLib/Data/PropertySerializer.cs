using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Squared.Task.Data.Mapper;
using Squared.Util.Bind;
using System.Linq.Expressions;
using System.Data;

namespace Squared.Task.Data {
    public delegate IEnumerator<object> BoundMemberAdapterFunc (string name, IBoundMember member);

    public class BoundMemberAdapter<T> {
        public readonly Func<string, BoundMember<T>, IEnumerator<object>> Method;

        public BoundMemberAdapter (
            Func<string, BoundMember<T>, IEnumerator<object>> method
        ) {
            Method = method;
        }

        public IEnumerator<object> Invoke (string name, IBoundMember member) {
            return Method(name, (BoundMember<T>)member);
        }
    }

    public struct BoundMemberAdapters {
        public readonly BoundMemberAdapterFunc Load, Save;

        public BoundMemberAdapters (BoundMemberAdapterFunc load, BoundMemberAdapterFunc save) {
            Load = load;
            Save = save;
        }
    }

    public abstract class PropertySerializerBase : IDisposable {
        protected readonly Dictionary<Type, BoundMemberAdapters> AdapterCache = new Dictionary<Type, BoundMemberAdapters>();

        public readonly List<IBoundMember> Bindings = new List<IBoundMember>();

        public Func<IBoundMember, string> GetMemberName;

        public PropertySerializerBase (
            Func<IBoundMember, string> getMemberName
        ) {
            GetMemberName = getMemberName;
        }

        public void Bind<T> (Expression<Func<T>> target) {
            Bindings.Add(BoundMember.New(target));
        }

        public static string GetDefaultMemberName (IBoundMember member) {
            return member.Name;
        }

        public IEnumerator<object> Save () {
            foreach (var member in Bindings) {
                var name = GetMemberName(member);
                var adapters = GetAdapters(member.Type);
                yield return adapters.Save(name, member);
            }
        }

        public IEnumerator<object> Load () {
            foreach (var member in Bindings) {
                var name = GetMemberName(member);
                var adapters = GetAdapters(member.Type);
                yield return adapters.Load(name, member);
            }
        }

        protected BoundMemberAdapters GetAdapters (Type type) {
            BoundMemberAdapters result;
            if (AdapterCache.TryGetValue(type, out result))
                return result;

            var bmaFuncType = typeof(BoundMemberAdapterFunc);

            var adapterType = typeof(BoundMemberAdapter<>)
                .MakeGenericType(type);

            Func<string, Delegate> getHelperMethod = (string name) => {
                var method = GetType().GetMethod(
                    name, BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.FlattenHierarchy
                ).MakeGenericMethod(type);

                var delegateType = typeof(Func<,,>)
                    .MakeGenericType(
                        typeof(string), 
                        typeof(BoundMember<>)
                            .MakeGenericType(type), 
                        typeof(IEnumerator<object>)
                    );

                return Delegate.CreateDelegate(delegateType, this, method, true);
            };
            
            var loadAdapter = adapterType.GetConstructors()[0]
                .Invoke(new object[] { getHelperMethod("LoadBinding") });

            var saveAdapter = adapterType.GetConstructors()[0]
                .Invoke(new object[] { getHelperMethod("SaveBinding") });

            var invokeMethod = adapterType
                .GetMethod("Invoke", 
                    BindingFlags.Public | BindingFlags.Instance
                );

            result = new BoundMemberAdapters(
                (BoundMemberAdapterFunc)Delegate.CreateDelegate(bmaFuncType, loadAdapter, invokeMethod, true),
                (BoundMemberAdapterFunc)Delegate.CreateDelegate(bmaFuncType, saveAdapter, invokeMethod, true)
            );
            AdapterCache[type] = result;

            return result;
        }

        protected abstract IEnumerator<object> LoadBinding<T> (string name, BoundMember<T> member);

        protected abstract IEnumerator<object> SaveBinding<T> (string name, BoundMember<T> member);

        public virtual void Dispose () {
            Bindings.Clear();
        }
    }

    public class DatabasePropertySerializer : PropertySerializerBase {
        public readonly Query WriteValue;
        public readonly Query ReadValue;

        public DatabasePropertySerializer (
            ConnectionWrapper database, string tableName
        ) : this (
            database, tableName,
            "name", "value"
        ) {
        }

        public DatabasePropertySerializer (
            ConnectionWrapper database, string tableName, 
            string nameColumn, string valueColumn
        ) : this (
            database, tableName,
            nameColumn, valueColumn,
            GetDefaultMemberName
        ) {
        }

        public DatabasePropertySerializer (
            ConnectionWrapper database, string tableName, 
            string nameColumn, string valueColumn, 
            Func<IBoundMember, string> getMemberName
        ) : this (
            database.BuildQuery(String.Format("REPLACE INTO {0} ({1}, {2}) VALUES (?, ?)", tableName, nameColumn, valueColumn)),
            database.BuildQuery(String.Format("SELECT {2} FROM {0} WHERE {1} = ? LIMIT 1", tableName, nameColumn, valueColumn)),
            getMemberName
        ) {
        }

        public DatabasePropertySerializer (
            Query writeValue, Query readValue
        ) : this (
            writeValue, readValue, GetDefaultMemberName
        ) {
        }

        public DatabasePropertySerializer (
            Query writeValue, Query readValue, 
            Func<IBoundMember, string> getMemberName
        ) : base (getMemberName) {
            WriteValue = writeValue;
            writeValue.Parameters[0].DbType = DbType.String;
            writeValue.Parameters[1].DbType = DbType.Object;
            ReadValue = readValue;
            readValue.Parameters[0].DbType = DbType.String;
        }

        protected override IEnumerator<object> SaveBinding<T> (string name, BoundMember<T> member) {
            yield return WriteValue.ExecuteNonQuery(name, member.Value);
        }

        protected override IEnumerator<object> LoadBinding<T> (string name, BoundMember<T> member) {
            var fReader = ReadValue.ExecuteReader(name);
            yield return fReader;

            using (var reader = fReader.Result) {
                if (reader.Reader.Read())
                    member.Value = DataRecordHelper.GetReadMethod<T>()
                        (reader.Reader, 0);
            }
        }

        override public void Dispose () {
            WriteValue.Dispose();
            ReadValue.Dispose();
        }
    }
}
