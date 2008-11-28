using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public delegate void Subtract<T> (ref T lhs, ref T rhs, out T result);

    public struct Pair<T> : IComparable<Pair<T>>, IEquatable<Pair<T>> 
        where T : struct, IComparable<T> {

        public T First, Second;

        public Pair (T first, T second) {
            First = first;
            Second = second;
        }

        public T[] ToArray () {
            return new T[] { First, Second };
        }

        public int CompareTo (Pair<T> other) {
            int result = First.CompareTo(other.First);
            if (result != 0)
                return result;

            result = Second.CompareTo(other.Second);

            return result;
        }

        public bool Equals (Pair<T> other) {
            if (!First.Equals(other.First))
                return false;

            if (!Second.Equals(other.Second))
                return false;

            return true;
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}}}", First, Second);
        }
    }

    public struct Interval<T> : IEquatable<Interval<T>>
        where T : struct, IComparable<T>, IEquatable<T> {

        public T Min, Max;

        public Interval (T a, T b) {
            if (a.CompareTo(b) == -1) {
                Min = a;
                Max = b;
            } else {
                Min = b;
                Max = a;
            }
        }

        public bool Intersects (Interval<T> other) {
            if (Min.CompareTo(other.Max) >= 0) return false;
            if (Max.CompareTo(other.Min) <= 0) return false;
            return true;
        }

        public T GetDistance (Interval<T> other, Subtract<T> subtractor) {
            T result;
            if (Min.CompareTo(other.Min) <= -1) {
                subtractor(ref other.Min, ref Max, out result);
            } else {
                subtractor(ref Min, ref other.Max, out result);
            }
            return result;
        }

        public bool GetIntersection (Interval<T> other, out Interval<T> result) {
            int a = Min.CompareTo(other.Min);
            int b = Max.CompareTo(other.Max);
            int c = Min.CompareTo(other.Max);
            int d = Max.CompareTo(other.Min);

            result = new Interval<T>(
                (a >= 0) ? Min : other.Min,
                (b <= 0) ? Max : other.Max
            );

            return Math.Sign(c) != Math.Sign(d);
        }

        public bool Equals (Interval<T> other) {
            if (!Min.Equals(other.Min))
                return false;

            if (!Max.Equals(other.Max))
                return false;

            return true;
        }

        public T[] ToArray () {
            return new T[] { Min, Max };
        }

        public override string ToString () {
            return String.Format("({0}, {1})", Min, Max);
        }
    }

    public struct Triplet<T> : IComparable<Triplet<T>>, IEquatable<Triplet<T>>
        where T : struct, IComparable<T>, IEquatable<T> {

        public T First, Second, Third;

        public Triplet (T first, T second, T third) {
            First = first;
            Second = second;
            Third = third;
        }

        public T[] ToArray () {
            return new T[] { First, Second, Third };
        }

        public int CompareTo (Triplet<T> other) {
            int result = First.CompareTo(other.First);
            if (result != 0)
                return result;

            result = Second.CompareTo(other.Second);
            if (result != 0)
                return result;

            result = Third.CompareTo(other.Third);

            return result;
        }

        public bool Equals (Triplet<T> other) {
            if (!First.Equals(other.First))
                return false;

            if (!Second.Equals(other.Second))
                return false;

            if (!Third.Equals(other.Third))
                return false;

            return true;
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}, {2}}}", First, Second, Third);
        }
    }
}
