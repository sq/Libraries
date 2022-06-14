using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Util {
    public delegate void Subtract<T> (ref T lhs, ref T rhs, out T result);

    public static class Pair {
        public static Pair<T> New<T> (T first, T second)
             where T : IComparable<T> {
            return new Pair<T>(first, second);
        }
    }

    public struct Pair<T> : IComparable<Pair<T>>, IEquatable<Pair<T>> 
        where T : IComparable<T> {

        public sealed class Comparer : IEqualityComparer<Pair<T>> {
            public static readonly Comparer Instance = new Comparer();

            public bool Equals (Pair<T> x, Pair<T> y) {
                return x == y;
            }

            public int GetHashCode (Pair<T> obj) {
                return obj.GetHashCode();
            }
        }

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
            if (First.CompareTo(other.First) != 0)
                return false;

            if (Second.CompareTo(other.Second) != 0)
                return false;

            return true;
        }

        public override int GetHashCode () {
            return First.GetHashCode() ^ Second.GetHashCode();
        }

        public override string ToString () {
            return String.Format("{{{0}, {1}}}", First, Second);
        }

        public static bool operator == (Pair<T> lhs, Pair<T> rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Pair<T> lhs, Pair<T> rhs) {
            return !lhs.Equals(rhs);
        }
    }

    public struct Interval : IEquatable<Interval> {
        public float Min, Max;

        public Interval (float a, float b) {
            if (a <= b) {
                Min = a;
                Max = b;
            } else {
                Min = b;
                Max = a;
            }
        }

        public bool Intersects (Interval other) {
            if (Min >= other.Max) return false;
            if (Max <= other.Min) return false;
            return true;
        }

        public bool Intersects (Interval other, float epsilon) {
            return Intersects(other);

            /*
            float a = other.Max - Min;
            float b = Max - other.Min;
            if (a <= -epsilon)
                return false;
            if (b >= epsilon)
                return false;
            return true;
             */
        }

        public float GetDistance (Interval other) {
            if (Min < other.Min) {
                return other.Min - Max;
            } else {
                return Min - other.Max;
            }
        }

        public void GetUnion (Interval other, out Interval result) {
            result = new Interval(Math.Min(Min, other.Min), Math.Max(Max, other.Max));
        }

        public bool GetIntersection (Interval other, out Interval result) {
            int a = Min.CompareTo(other.Min);
            int b = Max.CompareTo(other.Max);
            int c = Min.CompareTo(other.Max);
            int d = Max.CompareTo(other.Min);

            result = new Interval(
                (a >= 0) ? Min : other.Min,
                (b <= 0) ? Max : other.Max
            );

            return Math.Sign(c) != Math.Sign(d);
        }

        public static bool operator == (Interval lhs, Interval rhs) {
            return lhs.Equals(rhs);
        }

        public static bool operator != (Interval lhs, Interval rhs) {
            return !lhs.Equals(rhs);
        }

        public bool Equals (Interval other) {
            if (Min != other.Min)
                return false;
            if (Max != other.Max)
                return false;
            return true;
        }

        public override bool Equals (object obj) {
            if (obj is Interval)
                return Equals((Interval)obj);
            else
                return false;
        }

        public float[] ToArray () {
            return new float[] { Min, Max };
        }

        public override int GetHashCode () {
            return Min.GetHashCode() ^ Max.GetHashCode();
        }

        public override string ToString () {
            return String.Format("({0}, {1})", Min, Max);
        }
    }
}
