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

    public class HermiteCurve<T> : CurveBase<T, HermiteCurve<T>.PointData>
        where T : struct {

        public struct PointData {
            public T Velocity;
        }

        protected Interpolator<T> _Interpolator; 
        protected InterpolatorSource<T> _InterpolatorSource;

        protected Arithmetic.BinaryOperatorMethod<T, T> _Add, _Sub;
        protected Arithmetic.BinaryOperatorMethod<T, float> _Div; 

        public HermiteCurve () {
            _Interpolator = Interpolators<T>.Cubic;
            _InterpolatorSource = GetCubicInputForIndex;

            _Add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add);
            _Sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
            _Div = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Divide);
        }

        protected override T Interpolate (int index, float offset) {
            return _Interpolator(_InterpolatorSource, index * 2, offset);
        }

        private T GetCubicInputForIndex (int index) {
            var maxIndex = _Items.Count - 1;
            int pairIndex = index / 4, itemInPair = index % 4;
            int aIndex = (pairIndex * 2), dIndex = aIndex + 1;

            switch (itemInPair) {
                case 0: // A
                    return GetValueAtIndex(aIndex);
                case 1: // B
                    var A = GetValueAtIndex(aIndex);
                    var U = GetDataAtIndex(aIndex).Velocity;
                    return _Add(A, _Div(U, 3));
                case 2: // C
                    var D = GetValueAtIndex(dIndex);
                    var V = GetDataAtIndex(dIndex).Velocity;
                    return _Sub(D, _Div(V, 3));
                default:
                case 3: // D
                    return GetValueAtIndex(dIndex);
            }
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
    }
}
