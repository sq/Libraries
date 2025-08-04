﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Linq.Expressions;

namespace Squared.Util.Containers {
    public delegate void ExtentEstimator (object value, ref object min, ref object max);
    public delegate void ExtentEstimator<T> (ref T value, ref T min, ref T max);

    public interface ICurve {
        Type ValueType { get; }
        Type DataType { get; }
        int Count { get; }
        float Start { get; }
        float End { get; }
        bool FindNearestPoint (float position, out int resultIndex, out float resultPosition);
        int GetLowerIndexForPosition (float position);
        void Clear ();
        bool CreateNewPointAtPosition (float position, float epsilon);
        bool TryGetPositionAtIndex (int index, out float result);
        bool GetValueAtPosition (float position, out object result);
        object GetValueAtIndex (int index);
        bool RemoveAtIndex (int index);
        bool RemoveAtPosition (float position, float epsilon);
        void SetValueAtPosition (float position, object value);
        IEnumerable<CurvePoint<object>> Points { get; }
        bool EstimateExtents (IComparer comparer, out object minimum, out object maximum, int detail, ExtentEstimator estimator = null);
    }

    public interface ICurve<TValue> : ICurve
        where TValue : struct 
    {
        bool GetValueAtPosition (float position, out TValue result);
        TValue this[float position] {
            get;
        }
        new IEnumerable<CurvePoint<TValue>> Points {
            get;
        }
        bool EstimateExtents<TComparer> (TComparer comparer, out TValue minimum, out TValue maximum, int detail = 10, Delegate estimator = null)
            where TComparer : IComparer<TValue>;
    }

    public readonly struct CurvePoint<TValue> {
        public readonly float Position;
        public readonly TValue Value;

        public CurvePoint (float position, in TValue value) {
            Position = position;
            Value = value;
        }
    }

    public abstract class CurveBase<TValue, TData> : IEnumerable<CurveBase<TValue, TData>.Point>, ICurve<TValue> 
        where TValue : struct
        where TData : struct 
    {
        public const float DefaultEpsilon = (float)(1.0 / 10000);

        public readonly struct Window {
            public readonly CurveBase<TValue, TData> Curve;
            public readonly int FirstIndex, LastIndex;
            public readonly float Start, End;

            public TValue this[float position] {
                get {
                    if (position < Start)
                        position = Start;
                    else if (position > End)
                        position = End;

                    Curve.GetValueAtPosition(position, FirstIndex, LastIndex, out var result);
                    return result;
                }
            }

            internal Window (CurveBase<TValue, TData> curve, int firstIndex, int lastIndex) {
                Curve = curve;
                FirstIndex = firstIndex;
                LastIndex = lastIndex;
                Start = Curve.GetPositionAtIndex(firstIndex);
                End = Curve.GetPositionAtIndex(lastIndex);
            }
        }

        public event EventHandler Changed;

        protected readonly UnorderedList<Point> _Items = new UnorderedList<Point>();
        protected Interpolator<TValue> _DefaultInterpolator;

        private PointPositionComparer _PositionComparer = new PointPositionComparer();
        protected int _Version;

        public int Version => _Version;

        Type ICurve.ValueType => typeof(TValue);
        Type ICurve.DataType => typeof(TData);

        public struct Point {
            public float Position;
            public TValue Value;
            public TData Data;
        }

        public sealed class PointPositionComparer : IComparer<Point> {
            public int Compare (Point x, Point y) {
                return x.Position.CompareTo(y.Position);
            }
        }

        public float Start {
            get {
                if (_Items.Count == 0)
                    return 0;
                ref var item = ref _Items.DangerousItem(0);
                return item.Position;
            }
        }

        public float End {
            get {
                if (_Items.Count == 0)
                    return 0;
                ref var item = ref _Items.DangerousItem(_Items.Count - 1);
                return item.Position;
            }
        }

        public int Count {
            get {
                return _Items.Count;
            }
        }

        public int GetLowerIndexForPosition (float position) {
            return GetLowerIndexForPosition(position, 0, _Items.Count - 1);
        }

        public bool FindNearestPoint (float position, out int resultIndex, out float resultPosition) {
            resultPosition = float.NaN;
            resultIndex = -1;

            int lower = GetLowerIndexForPosition(position), higher = lower + 1;
            if ((lower < 0) || (lower >= Count))
                return false;
            if (higher >= Count)
                higher = lower;

            float position1 = GetPositionAtIndex(lower),
                position2 = GetPositionAtIndex(higher),
                distance1 = Math.Abs(position - position1),
                distance2 = Math.Abs(position - position2);
            if (distance1 > distance2) {
                resultPosition = position2;
                resultIndex = higher;
            } else {
                resultPosition = position1;
                resultIndex = lower;
            }
            return true;
        }

        protected int GetLowerIndexForPosition (float position, int firstIndex, int lastIndex) {
            int count = _Items.Count, max = count - 1;

            if (firstIndex < 0)
                firstIndex = 0;
            if (lastIndex >= count)
                lastIndex = max;

            if (firstIndex >= lastIndex)
                return firstIndex;

            int low = firstIndex;
            int high = lastIndex;
            int index;
            int nextIndex;

            if (count < 1)
                return firstIndex;

            if (_Items.DangerousItem(lastIndex).Position < position)
                return lastIndex;
            else if (_Items.DangerousItem(firstIndex).Position > position)
                return firstIndex;

            while (low <= high) {
                if (low == high)
                    return low;

                index = (low + high) / 2;
                nextIndex = (index >= max) ? max : index + 1;

                ref var indexItem = ref _Items.DangerousItem(index);

                if (indexItem.Position < position) {
                    ref var nextItem = ref _Items.DangerousItem(nextIndex);
                    if (nextItem.Position > position) {
                        return index;
                    } else {
                        low = index + 1;
                    }
                } else if (indexItem.Position == position) {
                    return index;
                } else {
                    high = index - 1;
                }
            }

            return count - 1;
        }

        public bool MoveControlPointFromPosition (float oldPosition, float newPosition, float epsilon = DefaultEpsilon) {
            var index = GetLowerIndexForPosition(oldPosition);
            var distance = (GetPositionAtIndex(index) - oldPosition);
            if (Math.Abs(distance) > epsilon)
                return false;

            return MoveControlPointFromIndex(index, newPosition, epsilon);
        }

        public bool MoveControlPointFromIndex (int oldIndex, float newPosition, float epsilon = DefaultEpsilon) {
            if (!TryGetItemAtIndex(oldIndex, out var oldPosition, out var value, out var data))
                return false;

            if (FindNearestPoint(newPosition, out _, out var newNearestPosition)) {
                if (Math.Abs(newNearestPosition - newPosition) <= epsilon)
                    return false;
            }

            var removed = RemoveAtPosition(oldPosition, epsilon);
            SetValueAtPositionInternal(newPosition, value, data, !removed);
            if (!removed)
                throw new Exception("Failed to remove old control point");
            return true;
        }

        void ICurve.SetValueAtPosition (float position, object value) {
            TData data = default;
            var index = GetLowerIndexForPosition(position);
            if ((index >= 0) && (index < _Items.Count))
                data = GetDataAtIndex(index);

            SetValueAtPositionInternal(position, (TValue)value, in data, true);
        }

        public bool CreateNewPointAtPosition (float position, float epsilon = DefaultEpsilon) {
            if (
                FindNearestPoint(position, out int index, out float existingPosition) &&
                (Math.Abs(existingPosition - position) < epsilon)
            )
                return false;

            if (!GetValueAtPosition(position, out TValue value))
                ; // FIXME return false;

            TryGetDataAtIndex(index, out TData data);
            SetValueAtPositionInternal(position, in value, in data, true);
            return true;
        }

        public bool TryGetPositionAtIndex (int index, out float result) {
            result = float.NaN;

            if (index < 0)
                return false;
            else if (index >= _Items.Count)
                return false;

            result = GetPositionAtIndex(index);
            return true;
        }
        
        public ref readonly float GetPositionAtIndex (int index) {
            if (_Items.Count == 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            var max = _Items.Count - 1;
            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            ref var item = ref _Items.DangerousItem(index);
            return ref item.Position;
        }

        public bool TryGetDataAtIndex (int index, out TData result) {
            result = default;
            if (_Items.Count <= 0)
                return false;

            result = GetDataAtIndex(index);
            return true;
        }

        public bool TryGetItemAtIndex (int index, out float position, out TValue value, out TData data) {
            var max = _Items.Count - 1;
            if ((max < 0) || (index < 0) || (index > max)) {
                position = float.NaN;
                value = default;
                data = default;
                return false;
            }

            ref var item = ref _Items.DangerousItem(index);
            position = item.Position;
            value = item.Value;
            data = item.Data;
            return true;
        }

        public ref readonly TData GetDataAtIndex (int index) {
            var max = _Items.Count - 1;
            if (max < 0)
                throw new ArgumentOutOfRangeException(nameof(index));

            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            ref var item = ref _Items.DangerousItem(index);
            return ref item.Data;
        }

        object ICurve.GetValueAtIndex (int index) => GetValueAtIndex(index);

        public ref readonly TValue GetValueAtIndex (int index) {
            var max = _Items.Count - 1;
            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            ref var item = ref _Items.DangerousItem(index);
            return ref item.Value;
        }

        bool ICurve.GetValueAtPosition (float position, out object result) {
            var ok = GetValueAtPosition(position, 0, _Items.Count - 1, out var temp);
            result = temp;
            return ok;
        }

        public bool GetValueAtPosition (float position, out TValue result) {
            return GetValueAtPosition(position, 0, _Items.Count - 1, out result);
        }

        bool ICurve.EstimateExtents(IComparer comparer, out object minimum, out object maximum, int detail, ExtentEstimator estimator) {
            if (EstimateExtents((IComparer<TValue>)comparer ?? Comparer<TValue>.Default, out var min, out var max, detail, estimator)) {
                minimum = min;
                maximum = max;
                return true;
            } else {
                minimum = maximum = null;
                return false;
            }
        }

        /// <summary>
        /// Estimates the minimum and maximum values of the entire curve (if possible).
        /// </summary>
        /// <param name="comparer">The comparer used to determine which of two values is larger.</param>
        /// <param name="detail">The number of steps between each control point to evaluate (to account for non-linear interpolation)</param>
        public bool EstimateExtents<TComparer> (TComparer comparer, out TValue minimum, out TValue maximum, int detail = 10, Delegate extentEstimator = null)
            where TComparer : IComparer<TValue>
        {
            var eeo = extentEstimator as ExtentEstimator;
            var eet = extentEstimator as ExtentEstimator<TValue>;
            if ((eeo == null) && (eet == null) && (extentEstimator != null))
                throw new ArgumentOutOfRangeException(nameof(extentEstimator), $"Must be of type ExtentEstimator or ExtentEstimator<{typeof(TValue).Name}>");

            if (!typeof(TComparer).IsValueType) {
                if (comparer == null)
                    throw new ArgumentNullException(nameof(comparer));
            }
            if ((detail < 1) || (detail > 100))
                throw new ArgumentOutOfRangeException(nameof(detail));

            bool result = false;
            minimum = maximum = default;
            object boxedMinimum = null, boxedMaximum = null;

            if (Count < 1)
                return false;

            for (int i = 0; i < Count; i++) {
                if (!TryGetPositionAtIndex(i + 1, out float p1))
                    detail = 1;
                float p0 = GetPositionAtIndex(i),
                    fDetail = 1.0f / detail;

                for (int j = 0; j < detail; j++) {
                    float p = Arithmetic.Lerp(p0, p1, j * fDetail);
                    if (GetValueAtPosition(p, out var current)) {
                        if (result) {
                            if (eet != null)
                                eet(ref current, ref minimum, ref maximum);
                            else if (eeo != null)
                                eeo(current, ref boxedMinimum, ref boxedMaximum);
                            else {
                                if (comparer.Compare(current, minimum) < 0)
                                    minimum = current;
                                if (comparer.Compare(current, maximum) > 0)
                                    maximum = current;
                            }
                        } else {
                            result = true;
                            minimum = maximum = current;
                            if (eeo != null)
                                boxedMinimum = boxedMaximum = maximum;
                        }
                    }
                }
            }

            if (result && (eeo != null)) {
                minimum = (TValue)boxedMinimum;
                maximum = (TValue)boxedMaximum;
            }
            return result;
        }

        protected abstract bool GetValueAtPosition (float position, int firstIndex, int lastIndex, out TValue result, Interpolator<TValue> interpolator = null);

        public void Clear () {
            _Items.Clear();

            _Version++;
            OnChanged();
        }

        /// <summary>
        /// Copies all the control points from this curve into the destination curve.
        /// </summary>
        /// <param name="erase">If true, any existing control points in the destination will be removed first.</param>
        public void CopyTo (CurveBase<TValue, TData> destination, bool erase) {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            else if (!GetType().IsAssignableFrom(destination.GetType()))
                throw new ArgumentException(nameof(destination), "Destination curve's type is incompatible with source curve's type");

            if (erase)
                destination._Items.Clear();

            foreach (var item in _Items)
                destination.SetValueAtPositionInternal(item.Position, item.Value, item.Data, false);

            destination._Version++;
            destination.OnChanged();
        }

        public void Clamp (float newStartPosition, float newEndPosition) {
            GetValueAtPosition(newStartPosition, out var newStartValue);
            GetValueAtPosition(newEndPosition, out var newEndValue);

            int i = 0;
            while (i < _Items.Count) {
                float position = _Items.DangerousGetItem(i).Position;
                if ((position <= newStartPosition) || (position >= newEndPosition)) {
                    _Items.RemoveAtOrdered(i);
                } else {
                    i++;
                }
            }

            SetValueAtPositionInternal(newStartPosition, newStartValue, default(TData), false);
            SetValueAtPositionInternal(newEndPosition, newEndValue, default(TData), false);

            OnChanged();
        }

        public bool RemoveAtIndex (int index) {
            if ((index < 0) || (index >= _Items.Count))
                return false;

            _Items.RemoveAtOrdered(index);

            if (_Items.Count == 0)
                _Items.Add(default(Point));

            _Version++;
            OnChanged();
            return true;
        }

        public bool RemoveAtPosition (float position, float epsilon = DefaultEpsilon) {
            var index = GetLowerIndexForPosition(position);
            var item = _Items.DangerousGetItem(index);
            if (Math.Abs(item.Position - position) > epsilon)
                return false;

            _Items.RemoveAtOrdered(index);

            if (_Items.Count == 0)
                _Items.Add(default(Point));

            _Version++;
            OnChanged();
            return true;
        }

        protected void SetValueAtPositionInternal (float position, in TValue value, in TData data, bool dispatchEvent) {
            var oldIndex = GetLowerIndexForPosition(position);

            var newItem = new Point {
                Position = position,
                Value = value,
                Data = data
            };

            var ok = _Items.DangerousTryGetItem(oldIndex, out Point oldItem);
            if (ok && (oldItem.Position == position)) {
                _Items.DangerousSetItem(oldIndex, newItem);
            } else {
                _Items.Add(newItem);
                _Items.Sort(_PositionComparer);
            }

            _Version++;
            if (dispatchEvent)
                OnChanged();
        }

        protected void OnChanged () {
            if (Changed != null)
                Changed(this, EventArgs.Empty);
        }

        public IEnumerator<Point> GetEnumerator () {
            return _Items.GetEnumerator();
        }

        IEnumerable<CurvePoint<object>> ICurve.Points {
            get {
                foreach (var item in _Items)
                    yield return new CurvePoint<object>(item.Position, item.Value);
            }
        }

        public IEnumerable<CurvePoint<TValue>> Points {
            get {
                foreach (var item in _Items)
                    yield return new CurvePoint<TValue>(item.Position, item.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return _Items.GetEnumerator();
        }

        public TValue this[float position] {
            get {
                GetValueAtPosition(position, out TValue result);
                return result;
            }
        }

        public Window GetWindow (float lowPosition, float highPosition) {
            return GetWindow(
                GetLowerIndexForPosition(lowPosition),
                GetLowerIndexForPosition(highPosition) + 1
            );
        }

        public Window GetWindow (int firstIndex, int lastIndex) {
            return new Window(
                this,
                firstIndex,
                lastIndex
            );
        }
    }

    public static class Curve {
        public static ICurve New (Type valueType) {
            var t = typeof(Curve<>).MakeGenericType(valueType);
            return (ICurve)Activator.CreateInstance(t);
        }
    }

    public class Curve<T> : CurveBase<T, Curve<T>.PointData>
        where T : struct 
    {
        public struct PointData {
            public Interpolator<T> Interpolator; 
        }

        public Interpolator<T> DefaultInterpolator;

        protected InterpolatorSource<T> _InterpolatorSource;

        public Curve () {
            DefaultInterpolator = Interpolators<T>.Default;
            _InterpolatorSource = GetValueAtIndex;
        }

        public Curve (IEnumerable<CurvePoint<T>> points) 
            : this () {

            foreach (var kvp in points)
                Add(kvp.Position, kvp.Value);
        }

        public void SetValueAtPosition (float position, in T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        public void Add (float position, in T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        protected override bool GetValueAtPosition (float position, int firstIndex, int lastIndex, out T result, Interpolator<T> interpolator = null) {
            if (lastIndex < firstIndex) {
                result = default;
                return false;
            }

            int index = GetLowerIndexForPosition(position, firstIndex, lastIndex);
            ref var lowerItem = ref _Items.DangerousItem(index);
            ref var upperItem = ref _Items.DangerousItem((index == lastIndex) ? lastIndex : index + 1);

            var rangeSize = upperItem.Position - lowerItem.Position;
            if (rangeSize > 0) {
                float offset = (position - lowerItem.Position) / rangeSize;

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;

                interpolator = interpolator ?? lowerItem.Data.Interpolator ?? DefaultInterpolator;
                result = interpolator(_InterpolatorSource, index, offset);
            } else {
                if ((index == lastIndex) && (firstIndex != lastIndex) && (index > 0)) {
                    interpolator = interpolator ?? lowerItem.Data.Interpolator ?? DefaultInterpolator;
                    // HACK: Try to avoid harsh 'snapping' when arriving at the end if the interpolator has a
                    //  weird behavior where t=1 does not produce the end value. (Interpolators shouldn't do
                    //  this, but they can - pSRGBColor.LerpOkLCh for example)
                    result = interpolator(_InterpolatorSource, index - 1, 1f);
                } else 
                    result = lowerItem.Value;
            }

            return true;
        }

        new public T this[float position] {
            get {
                GetValueAtPosition(position, out T result);
                return result;
            }
            set {
                SetValueAtPositionInternal(position, value, default(PointData), true);
            }
        }
    }

    public static class HermiteSpline {
        public static ICurve New (Type valueType) {
            var t = typeof(HermiteSpline<>).MakeGenericType(valueType);
            return (ICurve)Activator.CreateInstance(t);
        }
    }

    public class HermiteSpline<T> : CurveBase<T, HermiteSpline<T>.PointData>
        where T : struct {

        public struct PointData {
            public T Velocity;
        }

        protected Interpolator<T> _Interpolator; 
        protected InterpolatorSource<T> _InterpolatorSource;

        protected static Arithmetic.BinaryOperatorMethod<T, T> _Sub;
        protected static Arithmetic.BinaryOperatorMethod<T, float> _Mul;

        static HermiteSpline () {
            _Sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
            _Mul = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply);
        }

        public HermiteSpline () {
            _Interpolator = Interpolators<T>.Hermite;
            _InterpolatorSource = GetHermiteInputForIndex;
        }

        protected override bool GetValueAtPosition (float position, int firstIndex, int lastIndex, out T result, Interpolator<T> interpolator = null) {
            if (lastIndex < firstIndex) {
                result = default;
                return false;
            }

            int index = GetLowerIndexForPosition(position, firstIndex, lastIndex);
            ref var lowerItem = ref _Items.DangerousItem(index);
            ref var upperItem = ref _Items.DangerousItem(Math.Min(index + 1, _Items.Count - 1));

            if (lowerItem.Position < upperItem.Position) {
                float offset = (position - lowerItem.Position) / (upperItem.Position - lowerItem.Position);

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;

                result = (interpolator ?? _Interpolator)(_InterpolatorSource, (index * 2), offset);
            } else {
                result = lowerItem.Value;
            }
            return true;
        }

        private ref readonly T GetHermiteInputForIndex (int index) {
            int quadIndex = index / 4, itemInQuad = index % 4;
            if (quadIndex < 0)
                quadIndex = 0;
            int aIndex = (quadIndex * 2), dIndex = aIndex + 1;

            switch (itemInQuad) {
                case 0: // A
                    return ref GetValueAtIndex(aIndex);
                case 1: // U
                    return ref GetDataAtIndex(aIndex).Velocity;
                case 2: // D
                    return ref GetValueAtIndex(dIndex);
                default:
                case 3: // V
                    return ref GetDataAtIndex(dIndex).Velocity;
            }
        }

        public void GetValuesAtIndex (int index, out float position, out T value, out T velocity) {
            position = GetPositionAtIndex(index);
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;
        }

        public bool GetValuesAtPosition (float position, out T value, out T velocity) {
            int index = GetLowerIndexForPosition(position);
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;

            return GetPositionAtIndex(index) == position;
        }

        public void SetValuesAtPosition (float position, in T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        public void Add (float position, in T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        /// <summary>
        /// Automatically generates velocity values for each point using a cardinal spline algorithm
        /// </summary>
        /// <param name="looping">If set, the first and last points are equivalent and velocity values will be generated for a closed loop</param>
        public void ConvertToCardinal (float tension, bool looping) {
            float tensionFactor = (1f / 2f) * (1f - tension);

            if (_Items.Count < (looping ? 3 : 2))
                return;

            for (int start = looping ? 0 : 1, end = _Items.Count - (looping ? 1 : 2), i = start; i <= end; i++) {
                var previous = _Items.DangerousGetItem((i == 0) ? (end - 1) : i - 1);
                var pt = _Items.DangerousGetItem(i);
                var next = _Items.DangerousGetItem((i == _Items.Count - 1) ? 1 : i + 1);

                var tangent = _Sub(next.Value, previous.Value);
                pt.Data.Velocity = _Mul(tangent, tensionFactor);
                _Items.DangerousSetItem(i, ref pt);
            }
        }

        public static HermiteSpline<T> CatmullRom (IEnumerable<CurvePoint<T>> points, bool looping) =>
            CatmullRom<HermiteSpline<T>>(points, looping);

        public static TSpline CatmullRom<TSpline> (IEnumerable<CurvePoint<T>> points, bool looping)
            where TSpline : HermiteSpline<T>, new()
        {
            return Cardinal<TSpline>(points, 0.5f, looping);
        }

        public static HermiteSpline<T> Cardinal (IEnumerable<CurvePoint<T>> points, float tension, bool looping)
            => Cardinal<HermiteSpline<T>>(points, tension, looping);

        public static TSpline Cardinal<TSpline> (IEnumerable<CurvePoint<T>> points, float tension, bool looping)
            where TSpline : HermiteSpline<T>, new()
        {
            var result = new TSpline();

            foreach (var pt in points)
                result.Add(pt.Position, pt.Value, default(T));

            result.ConvertToCardinal(tension, looping);

            return result;
        }
    }

    public static class CurveUtil {
        private static class Operators<T> {
            public static Arithmetic.BinaryOperatorMethod<T, T> Add, Sub;
            public static Arithmetic.BinaryOperatorMethod<T, float> Mul;

            static Operators () {
                Add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add);
                Sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
                Mul = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply);
            }
        }

        public static void CubicToHermite<T> (
            in T a, in T b,
            in T c, in T d,
            out T u, out T v
        ) {
            u = Operators<T>.Mul(Operators<T>.Sub(b, a), 3f);
            v = Operators<T>.Mul(Operators<T>.Sub(d, c), 3f);
        }

        public static void HermiteToCubic<T> (
            in T a, in T u,
            in T d, in T v,
            out T b, out T c
        ) {
            var multiplier = 1f / 3f;
            b = Operators<T>.Add(a, Operators<T>.Mul(u, multiplier));
            c = Operators<T>.Sub(d, Operators<T>.Mul(v, multiplier));
        }
    }

    public delegate float CurveSearchHeuristic<in T> (float position, T value) where T : struct;

    public static class CurveExtensions {
        public const int DefaultSearchSubdivision = 32;
        public const int DefaultMaxSearchRecursion = 7;
        public const float DefaultSearchEpsilon = float.Epsilon * 5;

        /// <summary>
        /// Searches the entire curve based on a heuristic.
        /// </summary>
        /// <param name="heuristic">A heuristic that measures how close a value is to the target value. This heuristic should return 0 if the target value is a match.</param>
        /// <returns>The position the search ended at if it was successful.</returns>
        public static float? Search<TValue> (
            this ICurve<TValue> curve, 
            CurveSearchHeuristic<TValue> heuristic
        )
            where TValue : struct
        {
            return Search(curve, heuristic, curve.Start, curve.End);
        }

        /// <summary>
        /// Searches a region of the curve based on a heuristic.
        /// </summary>
        /// <param name="heuristic">A heuristic that measures how close a value is to the target value. This heuristic should return 0 if the target value is a match.</param>
        /// <param name="low">The beginning of the search window.</param>
        /// <param name="high">The end of the search window.</param>
        /// <param name="subdivision">The number of sample points within the search window. A higher number of partitions will increase the likelihood that the best match will be found by the search, but increase the cost of the search.</param>
        /// <param name="maxRecursion">The maximum level of recursion for the search. High values for this argument will increase the precision of the search but also increase its cost.</param>
        /// <returns>The position the search ended at if it was successful.</returns>
        public static float? Search<TValue> (
            this ICurve<TValue> curve,
            CurveSearchHeuristic<TValue> heuristic, float low, float high,
            int? subdivision = null, int? maxRecursion = null,
            float? epsilon = null
        )
            where TValue : struct
        {
            float? result = null;
            float bestScore = float.MaxValue;

            SearchInternal(
                curve,
                heuristic,
                low, high,
                subdivision.GetValueOrDefault(DefaultSearchSubdivision),
                maxRecursion.GetValueOrDefault(DefaultMaxSearchRecursion),
                epsilon.GetValueOrDefault(DefaultSearchEpsilon),
                ref result, ref bestScore,
                0
            );

            return result;
        }

        private static void SearchInternal<TValue> (
            this ICurve<TValue> curve,
            CurveSearchHeuristic<TValue> heuristic,
            float low, float high,
            int subdivision, int maxRecursion,
            float epsilon,
            ref float? bestScoringPosition, ref float bestScore,
            int depth
        ) 
            where TValue : struct
        {
            if (subdivision < 2)
                subdivision = 2;

            if (high <= low)
                return;

            var actualSubdivision = subdivision;

            if (depth == 0) {
                actualSubdivision = Math.Min(1024, curve.Count * 2);
                actualSubdivision = Math.Max(actualSubdivision, subdivision);
            }

            bool improved = false;
            float partitionSize = (high - low) / actualSubdivision, partitionSizeHalf = partitionSize * 0.5f;
            if (Math.Abs(partitionSizeHalf) <= epsilon)
                return;

            for (int i = 0; i < actualSubdivision; i++) {
                var samplePosition = low + (partitionSize * i) + partitionSizeHalf;
                curve.GetValueAtPosition(samplePosition, out var value);
                var score = heuristic(samplePosition, value);

                if (score < bestScore) {
                    bestScore = score;
                    bestScoringPosition = samplePosition;
                    improved = true;
                }
            }

            if (depth >= maxRecursion)
                return;

            if (!improved)
                return;

            var newLow = Math.Max(low, bestScoringPosition.Value - partitionSizeHalf);
            var newHigh = Math.Min(high, bestScoringPosition.Value + partitionSizeHalf);

            SearchInternal(
                curve,
                heuristic,
                newLow, newHigh,
                subdivision, maxRecursion, epsilon,
                ref bestScoringPosition, ref bestScore,
                depth + 1
            );
        }

        /// <summary>
        /// Splits a hermite spline at a position. 
        /// Note that this may produce more than two output splines in order to eliminate discontinuities.
        /// </summary>
        /// <param name="splitPosition">The position at which to split the spline.</param>
        public static HermiteSpline<T>[] Split<T> (
            this HermiteSpline<T> spline,
            float splitPosition
        )
            where T : struct {
            var resultList = new List<HermiteSpline<T>>(4);

            spline.SplitInto(splitPosition, resultList);

            return resultList.ToArray();
        }

        /// <summary>
        /// Splits a hermite spline at a position. 
        /// Note that this may produce more than two output splines in order to eliminate discontinuities.
        /// </summary>
        /// <param name="splitPosition">The position at which to split the spline.</param>
        /// <param name="output">The list that will receive the new splines created by the split (up to 4).</param>
        public static void SplitInto<T> (
            this HermiteSpline<T> spline,
            float splitPosition,
            List<HermiteSpline<T>> output
        )
            where T : struct {

            if ((splitPosition <= spline.Start) || (splitPosition >= spline.End)) {
                output.Add(spline);
                return;
            }

            int count = spline.Count;
            int splitFirstPoint = spline.GetLowerIndexForPosition(splitPosition), splitSecondPoint = splitFirstPoint + 1;

            HermiteSpline<T> temp;

            if (splitFirstPoint > 0) {
                float position;
                T value, velocity;

                output.Add(temp = new HermiteSpline<T>());
                for (int i = 0, end = splitFirstPoint; i <= end; i++) {
                    spline.GetValuesAtIndex(i, out position, out value, out velocity);
                    temp.Add(position, value, velocity);
                }
            }

            float firstPosition = spline.GetPositionAtIndex(splitFirstPoint), secondPosition = spline.GetPositionAtIndex(splitSecondPoint);
            float splitLocalPosition = (splitPosition - firstPosition) / (secondPosition - firstPosition);

            T a = spline.GetValueAtIndex(splitFirstPoint), d = spline.GetValueAtIndex(splitSecondPoint);
            T u = spline.GetDataAtIndex(splitFirstPoint).Velocity, v = spline.GetDataAtIndex(splitSecondPoint).Velocity;
            T b, c;

            CurveUtil.HermiteToCubic(in a, in u, in d, in v, out b, out c);

            var ab = Arithmetic.Lerp(a, b, splitLocalPosition);
            var bc = Arithmetic.Lerp(b, c, splitLocalPosition);
            var cd = Arithmetic.Lerp(c, d, splitLocalPosition);

            var ab_bc = Arithmetic.Lerp(ab, bc, splitLocalPosition);
            var bc_cd = Arithmetic.Lerp(bc, cd, splitLocalPosition);

            var midpoint = Arithmetic.Lerp(ab_bc, bc_cd, splitLocalPosition);

            T newA, newB, newC, newD, newU, newV;

            newA = a;
            newB = ab;
            newC = ab_bc;
            newD = midpoint;

            CurveUtil.CubicToHermite(in newA, in newB, in newC, in newD, out newU, out newV);

            output.Add(temp = new HermiteSpline<T>());
            temp.Add(
                firstPosition, newA, newU
            );
            temp.Add(
                splitPosition, newD, newV
            );

            newA = midpoint;
            newB = bc_cd;
            newC = cd;
            newD = d;

            CurveUtil.CubicToHermite(in newA, in newB, in newC, in newD, out newU, out newV);

            output.Add(temp = new HermiteSpline<T>());
            temp.Add(
                splitPosition, newA, newU
            );
            temp.Add(
                secondPosition, newD, newV
            );

            if (splitSecondPoint < (count - 1)) {
                float position;
                T value, velocity;

                output.Add(temp = new HermiteSpline<T>());
                for (int i = splitSecondPoint, end = count - 1; i <= end; i++) {
                    spline.GetValuesAtIndex(i, out position, out value, out velocity);
                    temp.Add(position, value, velocity);
                }
            }
        }
    }
}
