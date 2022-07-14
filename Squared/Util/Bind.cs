using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Linq.Expressions;
using System.Reflection.Emit;

namespace Squared.Util.Bind {
    public interface IBoundMember {
        object Target {
            get;
        }
        string Name {
            get;
        }
        object Value {
            get;
            set;
        }
        Type Type {
            get;
        }
    }

    public interface MemberThunk<T> {
        T Value {
            get;
            set;
        }
    }

    public static class BoundMember {
        private static object ResolveTarget (Expression expr) {
            switch (expr.NodeType) {
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    var ue = (UnaryExpression)expr;
                    if (ue.Method.IsStatic)
                        return ue.Method.Invoke(null, new object[] { ResolveTarget(ue.Operand) });
                    else
                        return ue.Method.Invoke(ResolveTarget(ue.Operand), null);

                case ExpressionType.Constant:
                    return ((ConstantExpression)expr).Value;

                case ExpressionType.MemberAccess:
                    var me = ((MemberExpression)expr);
                    var obj = ResolveTarget(me.Expression);

                    switch (me.Member.MemberType) {
                        case MemberTypes.Property:
                            var prop = (PropertyInfo)me.Member;
                            return prop.GetValue(obj, null);

                        case MemberTypes.Field:
                            var field = (FieldInfo)me.Member;
                            return field.GetValue(obj);

                        default:
                            throw new ArgumentException("Expression is not constant");
                    }

                default:
                    throw new ArgumentException("Expression is not constant");
            } 
        }

        private static object NewInner (object obj, MemberInfo member) {
            Type type;
            var prop = member as PropertyInfo;
            var field = member as FieldInfo;
            var evt = member as EventInfo;

            if (prop != null) {
                type = prop.PropertyType;
            } else if (field != null) {
                type = field.FieldType;
            } else if (evt != null) {
                type = evt.EventHandlerType;
            } else {
                throw new ArgumentException("Must specify a property, field or event.", "member");
            }

            var boundType = typeof(BoundMember<>).MakeGenericType(type);
            return Activator.CreateInstance(
                boundType, obj,
                (object)prop ?? (object)evt ?? (object)field
            );
        }

        public static BoundMember<T> New<T> (object obj, MemberInfo member) {
            return NewInner(obj, member) as BoundMember<T>;
        }

        public static IBoundMember New (object obj, MemberInfo member) {
            return NewInner(obj, member) as IBoundMember;
        }

        const BindingFlags GetAllFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

        public static IBoundMember New (Type t, string memberName, BindingFlags? bindingAttr = null) {
            return NewInner(null, t.GetMember(memberName, bindingAttr.GetValueOrDefault(GetAllFlags)).First()) as IBoundMember;
        }

        public static BoundMember<T> New<T> (Type t, string memberName, BindingFlags? bindingAttr = null) {
            return NewInner(null, t.GetMember(memberName, bindingAttr.GetValueOrDefault(GetAllFlags)).First()) as BoundMember<T>;
        }

        public static BoundMember<T> New<T> (Expression<Func<T>> target) {
            var member = target.Body as MemberExpression;

            if (member == null)
                throw new ArgumentException("Target must be an expression that points to a field, property or event", "target");

            var obj = ResolveTarget(member.Expression);

            switch (member.Member.MemberType) {
                case MemberTypes.Property:
                    return new BoundMember<T>(obj, (PropertyInfo)member.Member);
                case MemberTypes.Field:
                    return new BoundMember<T>(obj, (FieldInfo)member.Member);
                case MemberTypes.Event:
                    return new BoundMember<T>(obj, (EventInfo)member.Member);
                default:
                    throw new ArgumentException("Target member must be a field, property or event", "target");
            }
        }

        public static BoundMember<T> New<T> (Expression<Action<T>> target) {
            var be = target.Body as System.Linq.Expressions.BinaryExpression;

            if (be == null)
                throw new ArgumentException("Target must be a binary mutation expression on a field, property or event", "target");

            var member = be.Left as MemberExpression;

            if (member == null)
                throw new ArgumentException("Target must be a binary mutation expression on a field, property or event", "target");

            var obj = ResolveTarget(member.Expression);

            switch (member.Member.MemberType) {
                case MemberTypes.Property:
                    return new BoundMember<T>(obj, (PropertyInfo)member.Member);
                case MemberTypes.Field:
                    return new BoundMember<T>(obj, (FieldInfo)member.Member);
                case MemberTypes.Event:
                    return new BoundMember<T>(obj, (EventInfo)member.Member);
                default:
                    throw new ArgumentException("Target member must be a field, property or event", "target");
            }
        }

        // FIXME: I haven't tested this
        class ReflectiveFieldThunk<TSelf, TValue> {
            public FieldInfo Field;

            public ReflectiveFieldThunk (FieldInfo field) {
                Field = field;
            }

            public TValue GetValue (TSelf target) => (TValue)Field.GetValue(target);
            public void SetValue (TSelf target, TValue value) => Field.SetValue(target, value);
        }

        public static TDelegate MakeFieldSetter<TDelegate> (object target, FieldInfo field)
            where TDelegate : class {

#if !NODYNAMICMETHOD
            DynamicMethod m = new DynamicMethod(
                String.Format("{0}.set{1}", field.DeclaringType.Name, field.Name), typeof(void),
                new Type[] { field.DeclaringType, field.FieldType }, field.DeclaringType, true
            );
            ILGenerator cg = m.GetILGenerator();

            cg.Emit(OpCodes.Ldarg_0);
            cg.Emit(OpCodes.Ldarg_1);
            cg.Emit(OpCodes.Stfld, field);
            cg.Emit(OpCodes.Ret);

            if (target != null)
                return m.CreateDelegate(typeof(TDelegate), target) as TDelegate;
            else
                return m.CreateDelegate(typeof(TDelegate)) as TDelegate;
#else
            var tThunk = typeof(ReflectiveFieldThunk<,>).MakeGenericType(field.DeclaringType, field.FieldType);
            var thunk = Activator.CreateInstance(tThunk, field);
            return (TDelegate)(object)Delegate.CreateDelegate(typeof(TDelegate), thunk, tThunk.GetMethod("SetValue"));
#endif
        }

        public static TDelegate MakeFieldGetter<TDelegate> (object target, FieldInfo field)
            where TDelegate : class {

#if !NODYNAMICMETHOD
            DynamicMethod m = new DynamicMethod(
                String.Format("{0}.get{1}", field.DeclaringType.Name, field.Name), field.FieldType,
                new Type[] { field.DeclaringType }, field.DeclaringType, true
            );
            ILGenerator cg = m.GetILGenerator();

            cg.Emit(OpCodes.Ldarg_0);
            cg.Emit(OpCodes.Ldfld, field);
            cg.Emit(OpCodes.Ret);

            if (target != null)
                return m.CreateDelegate(typeof(TDelegate), target) as TDelegate;
            else
                return m.CreateDelegate(typeof(TDelegate)) as TDelegate;
#else
            var tThunk = typeof(ReflectiveFieldThunk<,>).MakeGenericType(field.DeclaringType, field.FieldType);
            var thunk = Activator.CreateInstance(tThunk, field);
            return (TDelegate)(object)Delegate.CreateDelegate(typeof(TDelegate), thunk, tThunk.GetMethod("GetValue"));
#endif
        }
    }

    public sealed class BoundMember<T> : IBoundMember, IComparable<BoundMember<T>> {
        public readonly Type Type;
        public readonly object Target;
        public readonly string Name;

        protected readonly Func<T> Get;
        protected readonly Action<T> Set;
        public readonly Action<T> Add;
        public readonly Action<T> Remove;

        protected BoundMember (object target, string name) {
            if ((target != null) && target.GetType().IsValueType)
                throw new InvalidOperationException("Cannot bind to a member of a value type");

            Target = target;
            Name = name;
            Type = typeof(T);
        }

        public BoundMember (object target, FieldInfo field) 
            : this (target, field.Name) {

            if (field.IsStatic || field.IsInitOnly)
                throw new InvalidOperationException("Cannot bind to a static or initonly field");

            Get = BoundMember.MakeFieldGetter<Func<T>>(target, field);

            if (field.IsLiteral)
                Set = null;
            else
                Set = BoundMember.MakeFieldSetter<Action<T>>(target, field);

            Add = Remove = null;
        }

        public BoundMember (object target, EventInfo evt)
            : this(target, evt.Name) {

            Get = null; 
            Set = null;

            Add = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, evt.GetAddMethod(true), true);
            Remove = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, evt.GetRemoveMethod(true), true);
        }

        public BoundMember (object target, PropertyInfo property) 
            : this (target, property.Name) {
            
            if (!property.CanRead)
                Get = null;
            else
                Get = (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), target, property.GetGetMethod(true), true);

            if (!property.CanWrite)
                Set = null;
            else
                Set = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, property.GetSetMethod(true), true);

            Add = Remove = null;
        }

        public T Value {
            get {
                if (Get != null)
                    return Get();
                else
                    throw new InvalidOperationException("Member is write-only");
            }
            set {
                if (Set != null)
                    Set(value);
                else
                    throw new InvalidOperationException("Member is read-only");
            }
        }

        string IBoundMember.Name {
            get {
                return this.Name;
            }
        }

        object IBoundMember.Target {
            get {
                return this.Target;
            }
        }

        object IBoundMember.Value {
            get {
                return this.Value;
            }
            set {
                if (value is T)
                    this.Value = (T)value;
                else
                    this.Value = (T)Convert.ChangeType(value, Type);
            }
        }

        Type IBoundMember.Type {
            get {
                return Type;
            }
        }

        public int CompareTo (BoundMember<T> other) {
            if ((Type == other.Type) && (Target == other.Target) && (Name == other.Name))
                return 0;

            int result = Type.GetHashCode().CompareTo(other.Type.GetHashCode());
            if (result == 0)
                result = Target.GetHashCode().CompareTo(other.Target.GetHashCode());
            if (result == 0)
                result = Name.GetHashCode().CompareTo(other.Name.GetHashCode());
            if (result == 0)
                result = GetHashCode().CompareTo(other.GetHashCode());

            return result;
        }
    }
}
