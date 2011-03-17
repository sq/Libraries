using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Data;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Squared.Task.Data.Mapper {
    public class MapperAttribute : Attribute {
        public bool Explicit = false;

        public MapperAttribute () {
        }
    }

    public class ColumnAttribute : Attribute {
        public readonly string Name;
        public readonly int? Index;

        public ColumnAttribute () {
        }

        public ColumnAttribute (string name) {
            Name = name;
        }

        public ColumnAttribute (int index) {
            Index = index;
        }
    }

    struct BoundColumn {
        public string Name;
        public int? Index;
        public PropertyInfo Property;
        public FieldInfo Field;
        public Type Type;
    }

    public static class DataRecordHelper {
        class NullableHelper<T>
            where T : struct {

            readonly Func<IDataReader, int, T> Getter;

            public NullableHelper (Delegate getter) {
                Getter = (Func<IDataReader, int, T>)getter;
            }

            public T? Get (IDataReader reader, int ordinal) {
                if (reader.IsDBNull(ordinal))
                    return null;
                else
                    return Getter(reader, ordinal);
            }
        }

        class ReferenceHelper<T>
            where T : class {

            readonly Func<IDataReader, int, T> Getter;

            public ReferenceHelper (Delegate getter) {
                Getter = (Func<IDataReader, int, T>)getter;
            }

            public T Get (IDataReader reader, int ordinal) {
                if (reader.IsDBNull(ordinal))
                    return null;
                else
                    return Getter(reader, ordinal);
            }
        }

        static Dictionary<Type, Delegate> Getters = new Dictionary<Type, Delegate>();

        static DataRecordHelper () {
            var t = typeof(IDataRecord);
            var baseType = typeof(Func<,,>);
            var baseNullableHelperType = typeof(NullableHelper<>);
            var baseReferenceHelperType = typeof(ReferenceHelper<>);
            var baseNullableType = typeof(Nullable<>);
            var constructorTypes = new Type[] { typeof(Delegate) };

            foreach (var method in t.GetMethods()) {
                var rt = method.ReturnType;
                var expectedName = String.Format("Get{0}", rt.Name);

                if (method.Name != expectedName)
                    continue;

                var getterType = baseType.MakeGenericType(typeof(IDataReader), typeof(int), rt);
                var getter = Delegate.CreateDelegate(getterType, method);

                if (rt.IsValueType) {
                    Getters[rt] = getter;

                    var helperType = baseNullableHelperType.MakeGenericType(rt);
                    var helperConstructor = helperType.GetConstructor(constructorTypes);
                    var helper = helperConstructor.Invoke(new object[] { getter });
                    var helperMethod = helper.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);

                    var nullableType = baseNullableType.MakeGenericType(rt);
                    var nullableGetterType = baseType.MakeGenericType(typeof(IDataReader), typeof(int), nullableType);
                    var nullableGetter = Delegate.CreateDelegate(nullableGetterType, helper, helperMethod);

                    Getters[nullableType] = nullableGetter;
                } else {
                    var helperType = baseReferenceHelperType.MakeGenericType(rt);
                    var helperConstructor = helperType.GetConstructor(constructorTypes);
                    var helper = helperConstructor.Invoke(new object[] { getter });
                    var helperMethod = helper.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);

                    getter = Delegate.CreateDelegate(getterType, helper, helperMethod);

                    Getters[rt] = getter;
                }
            }

            Getters[typeof(object)] = (Func<IDataReader, int, object>)GetObject;
        }

        private static object GetObject (IDataReader reader, int ordinal) {
            return reader.GetValue(ordinal);
        }

        public static Func<IDataReader, int, T> GetReadMethod<T> () {
            var t = typeof(T);

            Delegate result;
            if (Getters.TryGetValue(t, out result))
                return (Func<IDataReader, int, T>)result;
            else
                return null;
        }
    }

    public class Mapper<T> 
        where T : new() {

        protected delegate void Setter<U> (T target, U newValue);

        protected interface ISetHelper {
            void Set (T item);
        }

        protected class SetHelper<U> : ISetHelper {
            public readonly IDataReader Reader;
            public readonly int Ordinal;
            public readonly Func<IDataReader, int, U> Getter;
            public readonly Setter<U> Setter;

            public SetHelper (IDataReader reader, int ordinal, Delegate setter) {
                Reader = reader;
                Ordinal = ordinal;
                Setter = (Setter<U>)setter;
                Getter = DataRecordHelper.GetReadMethod<U>();

                if (Getter == null) {
                    if (typeof(U).IsEnum)
                        Getter = EnumGetter;
                    else
                        Getter = DefaultGetter;
                }
            }

            static Setter<U> MakeFieldSetter (FieldInfo field) {
                return Squared.Util.Bind.BoundMember.MakeFieldSetter<Setter<U>>(null, field);
            }

            static U ObjectGetter (IDataReader reader, int ordinal) {
                return (U)reader.GetValue(ordinal);
            }

            static U EnumGetter (IDataReader reader, int ordinal) {
                return (U)Enum.ToObject(typeof(U), reader.GetValue(ordinal));
            }

            static U DefaultGetter (IDataReader reader, int ordinal) {
                object value = reader.GetValue(ordinal);
                if (value is DBNull)
                    value = null;

                Type type = typeof(U);
                if ((value != null) && (value.GetType() == type))
                    return (U)value;
                else
                    return (U)Convert.ChangeType(value, type);
            }

            public void Set (T item) {
                Setter(item, Getter(Reader, Ordinal));
            }
        }

        static Func<T> _Constructor;
        static object _Lock = new object();
        static List<BoundColumn> _Columns = new List<BoundColumn>();

        public readonly IDataReader Reader;

        protected ISetHelper[] Setters = null;

        static Mapper () {
            Expression<Func<T>> expr = (() => new T());
            _Constructor = expr.Compile();

            InitializeBindings();
        }

        static void InitializeBindings () {
            var t = typeof(T);

            var ca = t.GetCustomAttributes(typeof(MapperAttribute), false);
            bool xplicit = false;
            if ((ca != null) && (ca.Length > 0))
                xplicit = ((MapperAttribute)ca[0]).Explicit;

            foreach (var member in t.GetMembers()) {
                ColumnAttribute attr = null;
                ca = member.GetCustomAttributes(typeof(ColumnAttribute), false);
                if ((ca == null) || (ca.Length == 0)) {
                    if (xplicit)
                        continue;
                } else {
                    attr = ca[0] as ColumnAttribute;
                }

                string name = member.Name;
                int? index = null;
                if (attr != null) {
                    name = attr.Name ?? name;
                    index = attr.Index;
                }

                if ((name == null) && (index.HasValue == false))
                    throw new ArgumentException("A column attribute must specify either a name or an index");

                if (member.MemberType == MemberTypes.Field) {
                    var field = (FieldInfo)member;

                    _Columns.Add(new BoundColumn {
                        Name = name,
                        Index = index,
                        Field = field,
                        Type = field.FieldType,
                    });
                } else if (member.MemberType == MemberTypes.Property) {
                    var prop = (PropertyInfo)member;

                    _Columns.Add(new BoundColumn {
                        Name = name,
                        Index = index,
                        Property = prop,
                        Type = prop.PropertyType,
                    });
                }
            }
        }

        public Mapper (IDataReader reader) {
            Reader = reader;

            var t = typeof(T);
            var fieldCount = reader.FieldCount;
            var setterBaseType = typeof(Setter<>);
            var helperBaseType = typeof(SetHelper<>);
            var helperConstructorTypes = new Type[] { 
                typeof(IDataReader), typeof(int), typeof(Delegate)
            };
            var setters = new List<ISetHelper>();

            foreach (var column in _Columns) {
                string name = column.Name;
                int ordinal = -1;

                if (column.Index.HasValue) {
                    ordinal = column.Index.Value;
                    name = reader.GetName(ordinal);
                } else {
                    ordinal = reader.GetOrdinal(name);
                }

                var memberType = column.Type;
                var setterType = setterBaseType.MakeGenericType(t, memberType);
                var helperType = helperBaseType.MakeGenericType(t, memberType);
                var helperConstructor = helperType.GetConstructor(helperConstructorTypes);
                Delegate setterDelegate;

                if (column.Property != null) {
                    var setterMethod = column.Property.GetSetMethod(true);
                    setterDelegate = Delegate.CreateDelegate(setterType, setterMethod, true);
                } else {
                    setterDelegate = helperType.GetMethod("MakeFieldSetter", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { column.Field }) as System.Delegate;
                }

                var helper = (ISetHelper)helperConstructor.Invoke(new object[] { Reader, ordinal, setterDelegate });

                setters.Add(helper);
            }

            Setters = setters.ToArray();
        }

        public static string[] ColumnNames {
            get {
                return (from col in _Columns select col.Name).ToArray();
            }
        }

        public static int GetColumnValues (object instance, object[] result) {
            if (result == null)
                throw new ArgumentNullException("result");
            if (result.Length < _Columns.Count)
                throw new ArgumentException("Provided array too small to hold column values.", "result");

            for (int i = 0; i < _Columns.Count; i++) {
                var col = _Columns[i];
                if (col.Property != null)
                    result[i] = col.Property.GetValue(instance, null);
                else if (col.Field != null)
                    result[i] = col.Field.GetValue(instance);
            }

            return _Columns.Count;
        }

        public static object[] GetColumnValues (object instance) {
            object[] result = new object[_Columns.Count];

            GetColumnValues(instance, result);

            return result;
        }

        public bool Read (out T result) {
            if (!Reader.Read()) {
                result = default(T);
                return false;
            }

            result = _Constructor();

            foreach (var setter in Setters)
                setter.Set(result);

            return true;
        }

        public IEnumerator<T> ReadSequence () {
            using (Reader) {
                T item;

                while (Read(out item))
                    yield return item;
            }
        }

        public class PreparedInsert : IDisposable {
            public readonly ConnectionWrapper Connection;
            public readonly Query Query;
            public readonly string Verb;
            public readonly string Table;

            public readonly string[] Columns;

            internal PreparedInsert (ConnectionWrapper connection, string tableName, string verb, string[] extraColumns) {
                Connection = connection;
                Verb = verb;
                Table = tableName;

                var typeColumns = ColumnNames;
                int numExtraColumns = 0;
                if (extraColumns != null)
                    numExtraColumns = extraColumns.Length;

                Columns = new string[typeColumns.Length + numExtraColumns];

                Array.Copy(typeColumns, Columns, typeColumns.Length);
                if (numExtraColumns > 0)
                    Array.Copy(extraColumns, 0, Columns, typeColumns.Length, extraColumns.Length);

                var sql = BuildSQL();
                Query = Connection.BuildQuery(sql);
            }

            protected string BuildSQL () {
                var columnNames = String.Join(", ", Columns);
                var questionMarks = String.Join(", ", Enumerable.Repeat("?", Columns.Length).ToArray());

                return String.Format(
                    "{0} INTO {1} ({2}) VALUES ({3})",
                    Verb, Table, columnNames, questionMarks
                );
            }

            public Future<int> Insert (object instance, params object[] extraValues) {
                object[] arguments = new object[Columns.Length];

                int i = GetColumnValues(instance, arguments);

                if ((extraValues != null) && (extraValues.Length > 0))
                    Array.Copy(extraValues, 0, arguments, i, extraValues.Length);

                return Query.ExecuteNonQuery(arguments);
            }

            public void Dispose () {
                Query.Dispose();
            }
        }

        public static PreparedInsert PrepareInsert (ConnectionWrapper connection, string tableName, string verb = "INSERT", string[] extraColumns = null) {
            return new PreparedInsert(
                connection, tableName, verb, extraColumns
            );
        }
    }
}
