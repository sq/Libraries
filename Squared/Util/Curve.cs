using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
#if !XBOX
using System.Linq.Expressions;
#endif

namespace Squared.Util {
    public abstract class CurveBase<TValue, TData> : IEnumerable<CurveBase<TValue, TData>.Point>
        where TValue : struct
        where TData : struct {

        public const int DefaultSearchSubdivision = 32;
        public const int DefaultMaxSearchRecursion = 7;
        public const float DefaultSearchEpsilon = float.Epsilon * 5;

        public event EventHandler Changed;

        protected readonly List<Point> _Items = new List<Point>();
        protected Interpolator<TValue> _DefaultInterpolator;

        private PointPositionComparer _PositionComparer = new PointPositionComparer();

        public struct Point {
            public float Position;
            public TValue Value;
            public TData Data;
        }

        public class PointPositionComparer : IComparer<Point> {
            public int Compare (Point x, Point y) {
                return x.Position.CompareTo(y.Position);
            }
        }

        public float Start {
            get {
                return _Items[0].Position;
            }
        }

        public float End {
            get {
                return _Items[_Items.Count - 1].Position;
            }
        }

        protected int FindIndexOfPosition (float position) {
            return _Items.BinarySearch(new Point {
                Position = position
            }, _PositionComparer);
        }

        public int GetLowerIndexForPosition (float position) {
            int count = _Items.Count;
            int low = 0;
            int high = count - 1;
            int index;
            int nextIndex;

            if (position < Start) {
                return 0;
            } else if (position >= End) {
                return count - 1;
            } else {
                index = FindIndexOfPosition(position);
                if (index >= 0)
                    return index;
                else
                    index = low;
            }

            while (low <= high) {
                index = (low + high) / 2;
                nextIndex = Math.Min(index + 1, count - 1);
                float key = _Items[index].Position;
                if (low == high)
                    break;
                if (key < position) {
                    if ((nextIndex >= count) || (_Items[nextIndex].Position > position)) {
                        break;
                    } else {
                        low = index + 1;
                    }
                } else if (key == position) {
                    break;
                } else {
                    high = index - 1;
                }
            }

            return index;
        }

        /// <summary>
        /// Searches the entire curve based on a heuristic.
        /// </summary>
        /// <param name="heuristic">A heuristic that measures how close a value is to the target value. This heuristic should return 0 if the target value is a match.</param>
        /// <returns>The position the search ended at if it was successful.</returns>
        public float? Search (Func<TValue, float> heuristic) {
            return Search(heuristic, Start, End);
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
        public float? Search (
            Func<TValue, float> heuristic, float low, float high, 
            int? subdivision = null, int? maxRecursion = null,
            float? epsilon = null
        ) {
            float? result = null;
            float bestScore = float.MaxValue;

            SearchInternal(
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

        protected void SearchInternal (
            Func<TValue, float> heuristic, 
            float low, float high, 
            int subdivision, int maxRecursion,
            float epsilon,
            ref float? bestScoringPosition, ref float bestScore, 
            int depth
        ) {
            if (subdivision < 2)
                subdivision = 2;

            if (high <= low)
                return;

            var actualSubdivision = subdivision;

            if (depth == 0) {
                actualSubdivision = Math.Min(1024, _Items.Count * 2);
                actualSubdivision = Math.Max(actualSubdivision, subdivision);
            }

            bool improved = false;
            float partitionSize = (high - low) / actualSubdivision, partitionSizeHalf = partitionSize * 0.5f;
            if (Math.Abs(partitionSizeHalf) <= epsilon)
                return;

            for (int i = 0; i < actualSubdivision; i++) {
                var samplePosition = low + (partitionSize * i) + partitionSizeHalf;
                var score = heuristic(GetValueAtPosition(samplePosition));

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
                heuristic,
                newLow, newHigh,
                subdivision, maxRecursion, epsilon,
                ref bestScoringPosition, ref bestScore,
                depth + 1
            );
        }

        public float GetPositionAtIndex (int index) {
            index = Math.Min(Math.Max(0, index), _Items.Count - 1);
            return _Items[index].Position;
        }

        public TData GetDataAtIndex (int index) {
            index = Math.Min(Math.Max(0, index), _Items.Count - 1);
            return _Items[index].Data;
        }

        public TValue GetValueAtIndex (int index) {
            index = Math.Min(Math.Max(0, index), _Items.Count - 1);
            return _Items[index].Value;
        }

        protected abstract TValue Interpolate (int index, float offset);

        protected TValue GetValueAtPosition (float position) {
            int index = GetLowerIndexForPosition(position);
            float lowerPosition = GetPositionAtIndex(index);
            float upperPosition = GetPositionAtIndex(index + 1);
            
            if (lowerPosition < upperPosition) {
                float offset = (position - lowerPosition) / (upperPosition - lowerPosition);

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;

                return Interpolate(index, offset);
            } else {
                return _Items[index].Value;
            }
        }

        public void Clear () {
            _Items.Clear();

            OnChanged();
        }

        public void Clamp (float newStartPosition, float newEndPosition) {
            TValue newStartValue = GetValueAtPosition(newStartPosition);
            TValue newEndValue = GetValueAtPosition(newEndPosition);

            int i = 0;
            while (i < _Items.Count) {
                float position = _Items[i].Position;
                if ((position <= newStartPosition) || (position >= newEndPosition)) {
                    _Items.RemoveAt(i);
                } else {
                    i++;
                }
            }

            SetValueAtPositionInternal(newStartPosition, newStartValue, default(TData), false);
            SetValueAtPositionInternal(newEndPosition, newEndValue, default(TData), false);

            OnChanged();
        }

        public bool RemoveAtPosition (float position, float precision = 0.01f) {
            var index = GetLowerIndexForPosition(position);
            var item = _Items[index];
            if (Math.Abs(item.Position - position) > precision)
                return false;

            _Items.RemoveAt(index);

            if (_Items.Count == 0)
                _Items.Add(default(Point));

            OnChanged();
            return true;
        }

        protected void SetValueAtPositionInternal (float position, TValue value, TData data, bool dispatchEvent) {
            var oldIndex = FindIndexOfPosition(position);

            var newItem = new Point {
                Position = position,
                Value = value,
                Data = data
            };

            if (oldIndex >= 0) {
                _Items[oldIndex] = newItem;
            } else {
                _Items.Add(newItem);
                _Items.Sort(_PositionComparer);
            }

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

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () {
            return _Items.GetEnumerator();
        }
    }

    public class Curve<T> : CurveBase<T, Curve<T>.PointData>
        where T : struct {

        public struct PointData {
            public Interpolator<T> Interpolator; 
        }

        public Interpolator<T> DefaultInterpolator;

        protected InterpolatorSource<T> _InterpolatorSource;

        public Curve () {
            DefaultInterpolator = Interpolators<T>.Default;
            _InterpolatorSource = GetValueAtIndex;
        }

        public void SetValueAtPosition (float position, T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        public void Add (float position, T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        protected override T Interpolate (int index, float offset) {
            var interpolator = _Items[index].Data.Interpolator ?? DefaultInterpolator;
            return interpolator(_InterpolatorSource, index, offset);
        }

        public T this[float position] {
            get {
                return GetValueAtPosition(position);
            }
            set {
                SetValueAtPositionInternal(position, value, default(PointData), true);
            }
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

        protected override T Interpolate (int index, float offset) {
            return _Interpolator(_InterpolatorSource, (index * 2), offset);
        }

        private T GetHermiteInputForIndex (int index) {
            int pairIndex = index / 4, itemInPair = index % 4;
            if (pairIndex < 0)
                pairIndex = 0;
            int aIndex = (pairIndex * 2), dIndex = aIndex + 1;

            switch (itemInPair) {
                case 0: // A
                    return GetValueAtIndex(aIndex);
                case 1: // U
                    return GetDataAtIndex(aIndex).Velocity;
                case 2: // D
                    return GetValueAtIndex(dIndex);
                default:
                case 3: // V
                    return GetDataAtIndex(dIndex).Velocity;
            }
        }

        public void GetValuesAtIndex (int index, out T value, out T velocity) {
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;
        }

        public bool GetValuesAtPosition (float position, out T value, out T velocity) {
            int index = GetLowerIndexForPosition(position);
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;

            return GetPositionAtIndex(index) == position;
        }

        public void SetValuesAtPosition (float position, T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        public void Add (float position, T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        public T this[float position] {
            get {
                return GetValueAtPosition(position);
            }
        }

        public void ConvertToCardinal (float tension) {
            float tensionFactor = (1f / 2f) * (1f - tension);

            for (int start = 1, end = _Items.Count - 2, i = start; i <= end; i++) {
                var previous = _Items[i - 1];
                var pt = _Items[i];
                var next = _Items[i + 1];

                var tangent = _Sub(next.Value, previous.Value);
                pt.Data.Velocity = _Mul(tangent, tensionFactor);
                _Items[i] = pt;
            }
        }

        public static HermiteSpline<T> CatmullRom (IEnumerable<KeyValuePair<float, T>> points) {
            return Cardinal(points, 0);
        }

        public static HermiteSpline<T> Cardinal (IEnumerable<KeyValuePair<float, T>> points, float tension) {
            var result = new HermiteSpline<T>();

            foreach (var pt in points)
                result.Add(pt.Key, pt.Value, default(T));

            result.ConvertToCardinal(tension);

            return result;
        }
    }
}
