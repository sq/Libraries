using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
#if !XBOX
using System.Linq.Expressions;
#endif

namespace Squared.Util.Bind {
    public interface IBoundMember {
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
#if !XBOX
        private static object ResolveTarget (Expression expr) {
            switch (expr.NodeType) {
                case ExpressionType.Convert:
                case ExpressionType.ConvertChecked:
                    Console.WriteLine("Attempting conversion {0}", expr);
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

        public static BoundMember<T> New<T> (Expression<Func<T>> target) {
            var member = target.Body as MemberExpression;

            if (member == null)
                throw new ArgumentException("Target must be an expression that points to a field or property", "target");

            var obj = ResolveTarget(member.Expression);

            switch (member.Member.MemberType) {
                case MemberTypes.Property:
                    return new BoundMember<T>(obj, (PropertyInfo)member.Member);
                case MemberTypes.Field:
                    return new BoundMember<T>(obj, (FieldInfo)member.Member);
                default:
                    throw new ArgumentException("Target member must be a field or property", "target");
            }
        }
#endif
    }

    public class BoundMember<T> : IBoundMember {
        public readonly Type Type;
        public readonly object Target;

        protected readonly Func<T> Get;
        protected readonly Action<T> Set;

        protected BoundMember (object target) {
            if (target.GetType().IsValueType)
                throw new InvalidOperationException("Cannot bind to a member of a value type");

            Target = target;
            Type = typeof(T);
        }

        public BoundMember (object target, FieldInfo field) 
            : this (target) {

            if (field.IsStatic || field.IsInitOnly)
                throw new InvalidOperationException("Cannot bind to a static or initonly field");

            if (field.FieldType.IsPrimitive) {
                // :(

                Get = () => {
                    return (T)field.GetValue(this.Target);
                };

                if (field.IsLiteral)
                    Set = null;
                else
                    Set = (v) => {
                        field.SetValue(this.Target, v);
                    };
            } else {

                var fieldInfos = new FieldInfo[] { field };

                Get = () => {
                    var tr = TypedReference.MakeTypedReference(this.Target, fieldInfos);
                    return __refvalue( tr,T);
                };

                if (field.IsLiteral)
                    Set = null;
                else
                    Set = (v) => {
                        var tr = TypedReference.MakeTypedReference(this.Target, fieldInfos);
                        __refvalue(tr, T) = v;
                    };
            }
        }

        public BoundMember (object target, PropertyInfo property) 
            : this (target) {
            
            if (!property.CanRead)
                Get = null;
            else
                Get = (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), target, property.GetGetMethod(true), true);

            if (!property.CanWrite)
                Set = null;
            else
                Set = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), target, property.GetSetMethod(true), true);
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

        object IBoundMember.Value {
            get {
                return (object)this.Value;
            }
            set {
                this.Value = (T)value;
            }
        }

        Type IBoundMember.Type {
            get {
                return Type;
            }
        }
    }
}
