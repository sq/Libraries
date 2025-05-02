﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;

namespace Squared.PRGUI {  
    public struct HitTestOptions {
        public bool? AcceptsMouseInput, AcceptsFocus;
        public bool RejectIntangible;

        public bool RequiresInput => (AcceptsMouseInput ?? false) || (AcceptsFocus ?? false);
    }

    public struct HitTestState {
        public HitTestOptions Options;

        public Control Result;
        public Vector2 GlobalPosition, LocalPosition;
    }

    public abstract partial class Control {
        protected virtual bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) {
            if (Intangible)
                return false;
            if (state.Options.AcceptsMouseInput.HasValue && (AcceptsMouseInput != state.Options.AcceptsMouseInput.Value))
                return false;
            if (state.Options.AcceptsFocus.HasValue && (AcceptsFocus != state.Options.AcceptsFocus.Value))
                return false;
            if (state.Options.RequiresInput && !Enabled)
                return false;
            if (!box.Contains(position))
                return false;

            state.Result = this;
            return true;
        }


        public Control HitTest (Vector2 position) {
            return HitTest(position, default, out _);
        }

        public Control HitTest (Vector2 position, in HitTestOptions options) {
            return HitTest(position, in options, out _);
        }

        public Control HitTest (Vector2 position, in HitTestOptions options, out Vector2 localPosition) {
            localPosition = default;

            if (!Visible)
                return null;
            if (IsLayoutInvalid)
                return null;
            // FIXME: Is this right?
            if (GetOpacity(Context.NowL) <= 0)
                return null;

            // HACK: ApplyLocalTransformToGlobalPosition can be expensive, so we skip it this way
            if (
                options.RejectIntangible &&
                !(this is IControlContainer) && 
                !(this is IPartiallyIntangibleControl) &&
                GetInternalFlag(InternalStateFlags.Intangible)
            )
                return null;

            var box = GetRect();

            var state = new HitTestState {
                Options = options,
                Result = this,
                GlobalPosition = position,
                LocalPosition = ApplyLocalTransformToGlobalPosition(position, ref box, true)
            };

            if (OnHitTest(box, state.LocalPosition, ref state)) {
                if (state.Result == null)
                    throw new Exception("OnHitTest produced a null result on success");

                if (options.RejectIntangible) {
                    var ipic = state.Result as IPartiallyIntangibleControl;
                    if (ipic?.IsIntangibleAtPosition(position) == true)
                        state.Result = null;
                }

                return state.Result;
            }

            return null;
        }
    }

    public interface IFuzzyHitTestTarget {
        bool WalkChildren { get; }
        int WalkTree (List<FuzzyHitTest.Result> output, ref FuzzyHitTest.Result thisControl, Vector2 position, Func<Control, bool> predicate, float maxDistanceSquared);
    }

    public sealed class FuzzyHitTest : IEnumerable<FuzzyHitTest.Result> {
        public struct Result {
            public int Depth;
            public float Distance;
            public Control Control;
            public RectF Rect, ClippedRect;
            public Vector2 ClosestPoint;
            public bool IsIntangibleAtClosestPoint;
        }

        public readonly UIContext Context;
        public Vector2 Position { get; private set; }
        private readonly List<Result> Results = new List<Result>();

        public FuzzyHitTest (UIContext context) {
            Context = context;
        }

        public void Run (Vector2 position, Func<Control, bool> predicate = null, float maxDistance = 64) {
            Results.Clear();
            Position = position;

            WalkTree(
                Context.Controls, Context.CanvasRect, 
                position, 0, predicate, maxDistance * maxDistance
            );

            if (Results.Count > 1)
                Results.Sort(ResultComparer);
        }

        private static int ResultComparer (Result lhs, Result rhs) {
            var depthResult = rhs.Depth.CompareTo(lhs.Depth);
            double d1 = Math.Round(lhs.Distance, 1, MidpointRounding.AwayFromZero),
                d2 = Math.Round(rhs.Distance, 1, MidpointRounding.AwayFromZero);
            var distanceResult = d1.CompareTo(d2);

            var threshold = 0.05f;
            var isOver1 = (lhs.Distance <= threshold) && !lhs.IsIntangibleAtClosestPoint;
            var isOver2 = (rhs.Distance <= threshold) && !rhs.IsIntangibleAtClosestPoint;

            // If the cursor is directly over a control, pick it over any alternatives
            if (isOver1 || isOver2)
                return distanceResult;
            // Otherwise, sort by depth so that highest depth takes priority (i.e. children over parents)
            else if (depthResult != 0)
                return depthResult;
            // Otherwise, sort closest first
            else
                return distanceResult;
        }

        private int WalkTree (ControlCollection controls, RectF clip, Vector2 position, int depth, Func<Control, bool> predicate, float maxDistanceSquared) {
            var totalMatches = 0;

            var ordered = controls.InDisplayOrder(Context.FrameIndex);
            var stop = false;
            for (int i = ordered.Count - 1; (i >= 0) && !stop; i--) {
                var control = ordered[i];
                if (!control.Visible || control.Intangible)
                    continue;

                if ((control is IModal im) && im.BlockHitTests)
                    stop = true;

                var result = new Result {
                    Depth = depth,
                    Control = control,
                    Rect = control.GetRect(context: Context, displayRect: true)
                };

                if (!result.Rect.Intersection(in clip, out result.ClippedRect))
                    continue;

                var inside = result.ClippedRect.Contains(position);
                stop = stop || inside;

                int localMatches = 0;
                var ifht = control as IFuzzyHitTestTarget;
                if (ifht != null)
                    localMatches += ifht.WalkTree(Results, ref result, position, predicate, maxDistanceSquared);

                if (ifht?.WalkChildren != false) {
                    var icc = control as IControlContainer;
                    if (icc != null)
                        localMatches += WalkTree(icc.Children, result.ClippedRect, position, depth + 1, predicate, maxDistanceSquared);
                }

                totalMatches += localMatches;
                if (localMatches > 0)
                    continue;

                if ((predicate != null) && !predicate(control))
                    continue;

                float distanceSquared;
                if (inside) {
                    result.Distance = distanceSquared = 0f;
                    result.ClosestPoint = position;
                } else {
                    result.ClosestPoint = result.ClippedRect.Clamp(position);
                    distanceSquared = (position - result.ClosestPoint).LengthSquared();
                    if (distanceSquared > maxDistanceSquared)
                        continue;
                    result.Distance = (float)Math.Sqrt(distanceSquared);
                }

                var ipic = result.Control as IPartiallyIntangibleControl;
                result.IsIntangibleAtClosestPoint = (ipic?.IsIntangibleAtPosition(result.ClosestPoint) == true);
                Results.Add(result);
                totalMatches += 1;
            }

            return totalMatches;
        }

        public int Count => Results.Count;
        public Result this[int index] => Results[index];

        IEnumerator<Result> IEnumerable<Result>.GetEnumerator () => ((IEnumerable<Result>)Results).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator () => ((IEnumerable<Result>)Results).GetEnumerator();
        public List<Result>.Enumerator GetEnumerator () => Results.GetEnumerator();
    }
}
