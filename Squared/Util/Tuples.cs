using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public delegate T Subtract<T> (T lhs, T rhs);

    public struct Pair<T> : IComparable<Pair<T>>, IEquatable<Pair<T>> 
        where T : IComparable<T> {

        public T First, Second;

        public Pair (T first, T second) {
            First = first;
            Second = second;
        }

        public T[] ToArray () {
            return new T[] { First, Second };
        }

        public int CompareTo (Pair<T> other) {
            int result = ((IComparable<T>)First).CompareTo(other.First);
            if (result != 0)
                return result;

            result = ((IComparable<T>)Second).CompareTo(other.Second);

            return result;
        }

        public bool Equals (Pair<T> other) {
            if (!((IEquatable<T>)First).Equals(other.First))
                return false;

            if (!((IEquatable<T>)Second).Equals(other.Second))
                return false;

            return true;
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}}}", First, Second);
        }
    }

    public struct Interval<T> : IEquatable<Interval<T>>
        where T : IComparable<T>, IEquatable<T> {

        public T Min, Max;

        public Interval (T a, T b) {
            if (((IComparable<T>)a).CompareTo(b) == -1) {
                Min = a;
                Max = b;
            } else {
                Min = b;
                Max = a;
            }
        }

        public bool Intersects (Interval<T> other) {
            if (((IComparable<T>)Min).CompareTo(other.Max) >= 0) return false;
            if (((IComparable<T>)Max).CompareTo(other.Min) <= 0) return false;
            return true;
        }

        public T GetDistance (Interval<T> other, Subtract<T> subtractor) {
            if (((IComparable<T>)Min).CompareTo(other.Min) <= -1) {
                return subtractor(other.Min, Max);
            } else {
                return subtractor(Min, other.Max);
            }
        }

        public bool GetIntersection (Interval<T> other, out Interval<T> result) {
            int a = ((IComparable<T>)Min).CompareTo(other.Min);
            int b = ((IComparable<T>)Max).CompareTo(other.Max);
            int c = ((IComparable<T>)Min).CompareTo(other.Max);
            int d = ((IComparable<T>)Max).CompareTo(other.Min);

            result = new Interval<T>(
                (a >= 0) ? Min : other.Min,
                (b <= 0) ? Max : other.Max
            );

            // Console.WriteLine("{0} i {1} == {2} | a={3}, b={4}, c={5}, d={6}", this, other, result, a, b, c, d);

            return Math.Sign(c) != Math.Sign(d);
        }

        public bool Equals (Interval<T> other) {
            if (!((IEquatable<T>)Min).Equals(other.Min))
                return false;

            if (!((IEquatable<T>)Max).Equals(other.Max))
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
        where T : IComparable<T>, IEquatable<T> {

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
            int result = ((IComparable<T>)First).CompareTo(other.First);
            if (result != 0)
                return result;

            result = ((IComparable<T>)Second).CompareTo(other.Second);
            if (result != 0)
                return result;

            result = ((IComparable<T>)Third).CompareTo(other.Third);

            return result;
        }

        public bool Equals (Triplet<T> other) {
            if (!((IEquatable<T>)First).Equals(other.First))
                return false;

            if (!((IEquatable<T>)Second).Equals(other.Second))
                return false;

            if (!((IEquatable<T>)Third).Equals(other.Third))
                return false;

            return true;
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}, {2}}}", First, Second, Third);
        }
    }
}
