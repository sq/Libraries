using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Data;
using System.Linq.Expressions;

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

        static Dictionary<Type, Delegate> Getters = new Dictionary<Type, Delegate>();

        static DataRecordHelper () {
            var t = typeof(IDataRecord);
            var baseType = typeof(Func<,,>);
            var baseHelperType = typeof(NullableHelper<>);
            var baseNullableType = typeof(Nullable<>);
            var constructorTypes = new Type[] { typeof(Delegate) };

            foreach (var method in t.GetMethods()) {
                var rt = method.ReturnType;
                var expectedName = String.Format("Get{0}", rt.Name);

                if (method.Name != expectedName)
                    continue;

                var getterType = baseType.MakeGenericType(typeof(IDataReader), typeof(int), rt);
                var getter = Delegate.CreateDelegate(getterType, method);

                Getters[rt] = getter;

                if (rt.IsValueType) {
                    var helperType = baseHelperType.MakeGenericType(rt);
                    var helperConstructor = helperType.GetConstructor(constructorTypes);
                    var helper = helperConstructor.Invoke(new object[] { getter });
                    var helperMethod = helper.GetType().GetMethod("Get", BindingFlags.Instance | BindingFlags.Public);

                    var nullableType = baseNullableType.MakeGenericType(rt);
                    var nullableGetterType = baseType.MakeGenericType(typeof(IDataReader), typeof(int), nullableType);
                    var nullableGetter = Delegate.CreateDelegate(nullableGetterType, helper, helperMethod);

                    Getters[nullableType] = nullableGetter;
                }
            }
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

        protected delegate void Setter<U> (ref T target, U newValue);

        protected interface ISetHelper {
            void Set (ref T item);
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

                if (Getter == null)
                    Getter = DefaultGetter;
            }

            static void StructPropertySetter (PropertyInfo property, ref T item, U value) {
                object boxed = item;
                property.SetValue(boxed, value, null);
                item = (T)boxed;
            }

            static void StructFieldSetter (FieldInfo field, ref T item, U value) {
                var tr = __makeref(item);
                field.SetValueDirect(tr, value);
            }

            static void PropertySetter (Action<T, U> setMethod, ref T item, U value) {
                setMethod(item, value);
            }

            static void FieldSetter (FieldInfo field, ref T item, U value) {
                field.SetValue(item, value);
            }

            static U DefaultGetter (IDataReader reader, int ordinal) {
                return (U)reader.GetValue(ordinal);
            }

            public void Set (ref T item) {
                Setter(ref item, Getter(Reader, Ordinal));
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
                        Type = field.FieldType
                    });
                } else if (member.MemberType == MemberTypes.Property) {
                    var prop = (PropertyInfo)member;

                    _Columns.Add(new BoundColumn {
                        Name = name,
                        Index = index,
                        Property = prop,
                        Type = prop.PropertyType
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
                    if (t.IsValueType) {
                        var helperMethod = helperType.GetMethod("StructPropertySetter", BindingFlags.Static | BindingFlags.NonPublic);
                        setterDelegate = Delegate.CreateDelegate(setterType, column.Property, helperMethod, true);
                    } else {
                        var setterMethod = column.Property.GetSetMethod(true);
                        var innerSetterType = typeof(Action<,>).MakeGenericType(t, memberType);
                        var innerSetterDelegate = Delegate.CreateDelegate(innerSetterType, setterMethod, true);
                        var helperMethod = helperType.GetMethod("PropertySetter", BindingFlags.Static | BindingFlags.NonPublic);
                        setterDelegate = Delegate.CreateDelegate(setterType, innerSetterDelegate, helperMethod, true);
                    }
                } else {
                    if (t.IsValueType) {
                        var helperMethod = helperType.GetMethod("StructFieldSetter", BindingFlags.Static | BindingFlags.NonPublic);
                        setterDelegate = Delegate.CreateDelegate(setterType, column.Field, helperMethod, true);
                    } else {
                        var helperMethod = helperType.GetMethod("FieldSetter", BindingFlags.Static | BindingFlags.NonPublic);
                        setterDelegate = Delegate.CreateDelegate(setterType, column.Field, helperMethod, true);
                    }
                }

                var helper = (ISetHelper)helperConstructor.Invoke(new object[] { Reader, ordinal, setterDelegate });

                setters.Add(helper);
            }

            Setters = setters.ToArray();
        }

        public bool Read (out T result) {
            if (!Reader.Read()) {
                result = default(T);
                return false;
            }

            result = _Constructor();

            foreach (var setter in Setters)
                setter.Set(ref result);

            return true;
        }

        public IEnumerator<T> ReadSequence () {
            using (Reader) {
                T item;

                while (Read(out item))
                    yield return item;
            }
        }
    }
}
