using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Squared.Util;

namespace CurveTester {
    public partial class MainWindow : Form {
        public HermiteSpline<Vector2> Spline = new HermiteSpline<Vector2> {
            {0, new Vector2 { X = 32, Y = 32 }, new Vector2 { X = 0, Y = 0 } },
            {1, new Vector2 { X = 256, Y = 32 }, new Vector2 { X = 0, Y = 32 } },
            {2, new Vector2 { X = 256, Y = 256 }, new Vector2 { X = -32, Y = 0 } },
        };

        private float? DraggingPosition = null;
        private Vector2 DragStartPosition, DragStartVelocity;
        private Point DragMousePosition;

        public MainWindow () {
            SetStyle(
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Opaque,
                true
            );

            InitializeComponent();

            CurveMode.SelectedIndex = 2;
        }

        protected float? GetPositionUnderMouse (float threshold = 8) {
            var cursorPos = PointToClient(Cursor.Position);

            HermiteSpline<Vector2>.Point? closestPoint = null;
            double closestDistance = double.MaxValue;

            foreach (var pt in Spline) {
                var distance = Math.Sqrt(Math.Pow(cursorPos.X - pt.Value.X, 2) + Math.Pow(cursorPos.Y - pt.Value.Y, 2));

                if (distance > threshold)
                    continue;

                if (distance < closestDistance) {
                    closestPoint = pt;
                    closestDistance = distance;
                }
            }

            if (closestPoint.HasValue)
                return closestPoint.Value.Position;
            else
                return null;
        }

        protected override void OnPaint (PaintEventArgs e) {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            ICurve<Vector2> spline = null;
            HermiteSpline<Vector2> hermite = null;
            
            switch (CurveMode.SelectedIndex) {
                default:
                case 0:
                case 1:
                    var curve = new Curve<Vector2>(Spline);
                    curve.DefaultInterpolator = Interpolators<Vector2>.Linear;

                    if (CurveMode.SelectedIndex == 1)
                        curve.DefaultInterpolator = Interpolators<Vector2>.Cubic;
                        
                    spline = curve;
                    break;
                case 2:
                    spline = hermite = Spline;
                    break;
                case 3:
                    spline = hermite = HermiteSpline<Vector2>.CatmullRom(
                        Spline
                    );
                    break;
                case 4:
                    spline = hermite = HermiteSpline<Vector2>.Cardinal(
                        Spline,
                        Tension.Value / 100f
                    );
                    break;
            }

            using (var linePen = new Pen(ForeColor, 1f))
            using (var path = new GraphicsPath()) {
                var previous = spline[spline.Start];

                for (float step = 0.01f, t = spline.Start + step; t <= spline.End; t += step) {
                    var current = spline[t];
                    path.AddLine(previous.X, previous.Y, current.X, current.Y);
                    previous = current;
                }

                e.Graphics.DrawPath(linePen, path);
            }

            var activePosition = GetPositionUnderMouse();

            using (var inactivePen = new Pen(Color.Gray, 2f))
            using (var activePen = new Pen(Color.White, 2f))
            using (var arrowPen = new Pen(Color.Blue, 2f)) {
                foreach (var pt in spline) {
                    e.Graphics.DrawEllipse(
                        pt.Key == activePosition.GetValueOrDefault(-32f) ? activePen : inactivePen,
                        pt.Value.X - 3f, pt.Value.Y - 3f,
                        6f, 6f
                    );
                }

                if (hermite != null)
                foreach (var pt in hermite) {
                    e.Graphics.DrawLine(
                        arrowPen,
                        pt.Value.X, pt.Value.Y,
                        pt.Value.X + pt.Data.Velocity.X, pt.Value.Y + pt.Data.Velocity.Y
                    );
                }
            }

            var mousePos = PointToClient(Cursor.Position);

            var closestPosition = spline.Search(
                (v) => (float)Math.Sqrt(
                    Math.Pow(v.X - mousePos.X, 2) +
                    Math.Pow(v.Y - mousePos.Y, 2)
                )
            );

            if (closestPosition.HasValue) {
                using (var closestPen = new Pen(Color.Green, 1.75f)) {
                    var closestPoint = spline[closestPosition.Value];

                    e.Graphics.DrawLine(
                        closestPen, 
                        mousePos.X, mousePos.Y, 
                        closestPoint.X, closestPoint.Y
                    );
                    e.Graphics.DrawEllipse(
                        closestPen, 
                        closestPoint.X - 3f, closestPoint.Y - 3f,
                        6f, 6f
                    );
                }
            }
        }

        private void UpdateDrag (MouseButtons buttons) {
            if (!DraggingPosition.HasValue)
                return;

            var cursorPos = Cursor.Position;
            var xDelta = cursorPos.X - DragMousePosition.X;
            var yDelta = cursorPos.Y - DragMousePosition.Y;

            var position = DragStartPosition;
            var velocity = DragStartVelocity;

            if (buttons == MouseButtons.Left) {
                position.X += xDelta;
                position.Y += yDelta;
            } else if (buttons == MouseButtons.Right) {
                velocity.X += xDelta;
                velocity.Y += yDelta;
            }

            Spline.SetValuesAtPosition(
                DraggingPosition.Value,
                position, velocity
            );
        }

        private bool IsMouseOverChildControl () {
            var cursorPos = PointToClient(Cursor.Position);

            foreach (Control child in this.Controls) {
                if (child.Bounds.Contains(cursorPos))
                    return true;
            }

            return false;
        }

        protected override void OnMouseDown (MouseEventArgs e) {
            if (IsMouseOverChildControl()) {
                base.OnMouseDown(e);
                return;
            }

            DraggingPosition = GetPositionUnderMouse();
            if (DraggingPosition.HasValue) {
                DragMousePosition = Cursor.Position;
                Spline.GetValuesAtPosition(DraggingPosition.Value, out DragStartPosition, out DragStartVelocity);
            } else {
                Spline.Add(Spline.End + 1f, new Vector2 {
                    X = e.X,
                    Y = e.Y
                }, new Vector2());
            }

            Invalidate();

            base.OnMouseDown(e);
        }

        protected override void OnMouseMove (MouseEventArgs e) {
            UpdateDrag(e.Button);
            Invalidate();

            base.OnMouseMove(e);
        }

        protected override void OnMouseUp (MouseEventArgs e) {
            UpdateDrag(e.Button);
            DraggingPosition = null;
            Invalidate();

            base.OnMouseUp(e);
        }

        private void CurveMode_SelectedIndexChanged (object sender, EventArgs e) {
            Invalidate();

            Tension.Enabled = (CurveMode.SelectedIndex == 4);
        }

        private void Tension_ValueChanged (object sender, EventArgs e) {
            Invalidate();
        }

        private void CursorMode_SelectedIndexChanged (object sender, EventArgs e) {
            Invalidate();
        }
    }

    public struct Vector2 {
        public float X, Y;

        public static Vector2 operator + (Vector2 lhs, Vector2 rhs) {
            return new Vector2 {
                X = lhs.X + rhs.X,
                Y = lhs.Y + rhs.Y
            };
        }

        public static Vector2 operator - (Vector2 lhs, Vector2 rhs) {
            return new Vector2 {
                X = lhs.X - rhs.X,
                Y = lhs.Y - rhs.Y
            };
        }

        public static Vector2 operator * (Vector2 lhs, float rhs) {
            return new Vector2 {
                X = lhs.X * rhs,
                Y = lhs.Y * rhs
            };
        }

        public static Vector2 operator / (Vector2 lhs, float rhs) {
            return new Vector2 {
                X = lhs.X / rhs,
                Y = lhs.Y / rhs
            };
        }
    }
}
