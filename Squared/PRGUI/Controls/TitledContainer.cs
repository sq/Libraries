using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Accessibility;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.PRGUI.NewEngine;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Text;

namespace Squared.PRGUI.Controls {
    public class TitledContainer : Container, IReadingTarget, IFuzzyHitTestTarget {
        public const float MinDisclosureArrowSize = 10,
            DisclosureArrowMargin = 16,
            DisclosureAnimationDuration = 0.175f,
            DisclosureArrowSizeMultiplier = 0.375f;

        protected virtual string DescriptionPrefix => (Collapsed ? "Collapsed Group" : "Group");
        public string Description = null;

        public bool Collapsible;
        protected bool CollapsingEnabled = true;

        public bool LayoutChildrenWhenCollapsed = true, LockWidthWhileCollapsed = false;

        public AbstractString Title;

        public float? MostRecentTitleHeight { get; protected set; }
        private Tween<float> DisclosureLevel = 1f;

        public float? MostRecentTitleOuterHeight => MostRecentTitleHeight + GetTitleDecorator(Context?.Decorations)?.Padding.Y;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            HideOverflow = true,
            AlignToPixels = StaticTextBase.DefaultGlyphPixelAlignment,
            LineLimit = 1
        };

        private bool _Collapsed, _CollapsePending;
        protected bool CollapsePending => _CollapsePending;
        public bool Collapsed {
            get => _Collapsed;
            set => SetCollapsed(value, false);
        }

        /// <summary>
        /// WARNING: If you call this on Window you deserve the bugs that will result
        /// </summary>
        public void SetCollapsed (bool value, bool instant = false) {
            if (!Collapsible)
                value = false;
            if (value == _Collapsed)
                return;

            _Collapsed = value;
            if (LayoutKey.IsInvalid) {
                _CollapsePending = true;
            } else {
                HandleCollapsedChanged(value, instant);
            }
        }

        private void HandleCollapsedChanged (bool value, bool instant) {
            _CollapsePending = false;

            var targetValue = (float)(value ? 0 : 1);
            if ((Context != null) && !instant) {
                var nowL = Context.NowL;
                DisclosureLevel = Tween.StartNow(
                    from: DisclosureLevel.Get(nowL), to: targetValue, 
                    seconds: DisclosureAnimationDuration * (Context.Animations?.AnimationDurationMultiplier ?? 1), now: nowL,
                    interpolator: Interpolators<float>.Cosine
                );
            } else
                DisclosureLevel = new Tween<float>(targetValue);

            FireEvent(UIEvents.ValueChanged);
            // FIXME: Fire byUser event
        }

        public TitledContainer ()
            : base () {
            Layout.ForceBreak = true;
            AcceptsMouseInput = true;
        }

        protected float DisclosureArrowSize => (float)Math.Round(
            Math.Max(MinDisclosureArrowSize, MostRecentTitleBox.Height * DisclosureArrowSizeMultiplier), 
            MidpointRounding.AwayFromZero
        );
        protected float DisclosureArrowPadding => DisclosureArrowSize + DisclosureArrowMargin;

        protected override bool HideChildren => Collapsible && !IsContentVisible;

        protected override bool ShouldClipContent {
            get {
                if (base.ShouldClipContent)
                    return true;
                if (!Collapsible)
                    return false;
                // Forcibly clip our content if we're currently animating an open/close
                var dl = DisclosureLevel.Get(Context.NowL);
                if ((dl <= 0) || (dl >= 1))
                    return false;
                return true;
            }
        }

        protected override IDecorator GetDefaultDecorator (IDecorationProvider provider) {
            return provider?.TitledContainer ?? base.GetDefaultDecorator(provider);
        }

        protected virtual IDecorator GetTitleDecorator (IDecorationProvider provider) {
            return provider?.ContainerTitle ?? 
                provider?.WindowTitle;
        }

        protected IDecorator UpdateTitle (
            ref UIOperationContext context, ControlStates state, ref RectF contentBox,
            out Material material, ref Color? color
        ) {
            var decorations = GetTitleDecorator(context.DecorationProvider);
            if (decorations == null) {
                material = null;
                return null;
            }
            decorations.GetTextSettings(ref context, state, out material, ref color, out Vector4 userData);
            TitleLayout.SetText(Title, true);
            var wasValid = TitleLayout.IsValid;
            TitleLayout.GlyphSource = GetGlyphSource(ref context, decorations);
            TitleLayout.DefaultColor = color ?? Color.White;
            TitleLayout.LineBreakAtX = contentBox.Width;
            TitleLayout.UserData = userData;
            // We don't want to invalidate the height for text changes, but we do want to invalidate it for wrapping or font changes
            if (!TitleLayout.IsValid && wasValid)
                MostRecentTitleHeight = null;
            return decorations;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs ma) {
                if (OnMouseEvent(name, ma))
                    return true;
            }

            return base.OnEvent<T>(name, args);
        }

        protected bool ToggleCollapsed () {
            if (Collapsible && Enabled) {
                if (Collapsed)
                    GenerateDynamicContent(true);

                Collapsed = !Collapsed;

                // FIXME: Notify container(s) to update their content bounds and scroll data
                if (Collapsed)
                    Context.ReleaseDescendantFocus(this, true);
                // A click on the collapse arrow should still focus our parent if necessary
                if (TryGetParent(out Control parent) && (Context.Focused == null))
                    // HACK: If we make this user initiated it can uncollapse us, which sucks
                    Context.TrySetFocus(parent, isUserInitiated: false);

                return true;
            }

            return false;
        }

        protected bool DisclosureArrowHitTest (Vector2 localPosition) {
            if (localPosition.X > DisclosureArrowPadding)
                return false;
            if (localPosition.Y > MostRecentTitleBox.Height)
                return false;
            if ((localPosition.X < 0) || (localPosition.Y < 0))
                return false;

            return true;
        }

        protected virtual bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name != UIEvents.Click)
                return false;

            if (
                (Collapsed && CollapsingEnabled) ||
                DisclosureArrowHitTest(args.RelativeGlobalPosition - args.Box.Position) ||
                (CollapsingEnabled && (args.SequentialClickCount == 2) && !Collapsed)
            ) {
                ToggleCollapsed();
                return true;
            }

            return false;
        }

        protected override void ComputeAppearanceSpacing (
            ref UIOperationContext context, IDecorator decorations, 
            out Margins scaledMargins, out Margins scaledPadding, out Margins unscaledPadding
        ) {
            base.ComputeAppearanceSpacing(ref context, decorations, out scaledMargins, out scaledPadding, out unscaledPadding);

            var titleDecorations = context.DecorationProvider?.WindowTitle;
            if (titleDecorations == null)
                return;
            if (Title.Length == 0)
                return;

            Color? color = null;
            titleDecorations.GetTextSettings(ref context, default(ControlStates), out Material temp, ref color, out _);
            var gs = GetGlyphSource(ref context, titleDecorations);
            var height = titleDecorations.Margins.Bottom +
                // FIXME: Scale this?
                titleDecorations.Padding.Y +
                gs.LineSpacing;
            unscaledPadding.Top += height;
        }

        protected override ref BoxRecord OnGenerateLayoutTree (ref UIOperationContext context, ControlKey parent, ControlKey? existingKey) {
            FreezeDynamicContent = _Collapsed;
            SuppressChildLayout = !LayoutChildrenWhenCollapsed 
                && _Collapsed 
                && MostRecentFullSize.HasValue
                && (DisclosureLevel.Get(context.NowL) <= 0);

            ref var result = ref base.OnGenerateLayoutTree(ref context, parent, existingKey);
            if (result.IsInvalid)
                return ref result;

            if (Title.Length == 0 && !existingKey.HasValue && Collapsible) {
                ref var spacer = ref context.Engine.Create();
                spacer.OldFlags = ControlFlags.Layout_Anchor_Left | ControlFlags.Layout_Anchor_Top | ControlFlags.Layout_ForceBreak;
                spacer.FixedSize = new Vector2(DisclosureArrowPadding, MostRecentTitleBox.Height);
                context.Engine.InsertAtStart(result, spacer.Key);
            }
            return ref result;
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
            // If this focus change is the result of a top-level focus change (i.e. selecting a window),
            //  this does not indicate that the user has attempted to focus one of our descendants directly
            //  using tab or some other mechanism, so we shouldn't respond by expanding ourselves.
            // This still means focus will be trapped inside us, but it's better than nothing.
            if (!isUserInitiated)
                return;

            if (Collapsed) {
                GenerateDynamicContent(true);
                Collapsed = false;
            }
        }

        private float GetTitleHeightForConstraints (ref UIOperationContext context) {
            if (MostRecentTitleBox != default(RectF))
                return MostRecentTitleBox.Height;
            var decorations = GetTitleDecorator(context.DecorationProvider);
            return (TitleLayout.GlyphSource?.LineSpacing ?? 0) + decorations.Padding.Y;
        }

        private float? ComputeDisclosureHeight (ref UIOperationContext context, float? input) {
            if (!MostRecentFullSize.HasValue)
                return input;
            if (!Collapsible)
                return input;
            var level = DisclosureLevel.Get(Context.NowL);
            if (level >= 1)
                return input;

            // HACK
            if (
                (MostRecentTitleHeight == null) && (TitleLayout.GlyphSource == null)
            ) {
                var cb = new RectF(Vector2.Zero, new Vector2(1024, 1024));
                var color = (Color?)null;
                UpdateTitle(ref context, default(ControlStates), ref cb, out Material temp, ref color);
            }

            var thfc = GetTitleHeightForConstraints(ref context);
            float collapsedHeight = input.HasValue ? Math.Min(input.Value, thfc) : thfc;
            float expandedHeight = input.HasValue ? Math.Min(input.Value, MostRecentFullSize.Value.Height) : MostRecentFullSize.Value.Height;
            return (float)Math.Floor(Arithmetic.Lerp(collapsedHeight, expandedHeight, level));
        }

        protected override void ComputeSizeConstraints (ref UIOperationContext context, ref ControlDimension width, ref ControlDimension height, Vector2 sizeScale) {
            base.ComputeSizeConstraints(ref context, ref width, ref height, sizeScale);
            var dheight = ComputeDisclosureHeight(ref context, height.Maximum);

            // If layout is suppressed because we are collapsed, we need to make sure we don't
            //  shrink horizontally due to the loss of our child boxes. The height just needs to
            //  be locked to our disclosure height instead of set as a maximum, since there's nothing
            //  inside us to make us taller.
            if (SuppressChildLayout && MostRecentFullSize.HasValue) {
                if (LockWidthWhileCollapsed) {
                    if (width.HasMinimum)
                        width.Minimum = Math.Max(width.Minimum.Value, MostRecentFullSize.Value.Width);
                    else
                        width.Minimum = MostRecentFullSize?.Width;
                }
                height.Fixed = dheight;
            } else {
                height.Maximum = dheight;
            }
        }

        public bool IsContentVisible => DisclosureLevel.Get(Context.NowL) > 0;

        protected override void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(ref context, ref relayoutRequested);

            if (LayoutKey.IsInvalid)
                return;

            var actuallyCollapsed = !_CollapsePending && _Collapsed && Collapsible;

            if (_CollapsePending && MostRecentFullSize.HasValue)
                HandleCollapsedChanged(_Collapsed, true);

            if (!actuallyCollapsed && (DisclosureLevel.Get(Context.NowL) >= 1)) {
                var box = GetRect();
                MostRecentFullSize = box;
            }
        }

        protected override void OnRasterize (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(ref context, ref renderer, settings, decorations);

            IDecorator titleDecorator;
            Color? titleColor = null;
            if (
                (titleDecorator = UpdateTitle(ref context, settings.State, ref settings.ContentBox, out Material titleMaterial, ref titleColor)) != null
            ) {
                if (Collapsible && (context.Pass == RasterizePasses.Above))
                    RasterizeDisclosureArrow(ref context, ref renderer, settings);

                if (context.Pass != RasterizePasses.Below)
                    return;

                var layout = TitleLayout.Get();
                var titleBox = settings.Box;
                MostRecentTitleHeight = (layout.DrawCalls.Count > 0) ? layout.Size.Y : (float?)null;
                titleBox.Height = titleDecorator.Padding.Top + titleDecorator.Padding.Bottom + TitleLayout.GlyphSource.LineSpacing;
                if (Title.Length == 0) {
                    if (Collapsible)
                        titleBox.Width = DisclosureArrowPadding;
                    else
                        titleBox.Width = 0;
                    MostRecentHeaderHeight = 0;
                } else {
                    MostRecentHeaderHeight = titleBox.Height;
                }
                // FIXME: Compute this somewhere else, like in OnLayoutComplete
                MostRecentTitleBox = titleBox;

                var titleContentBox = titleBox;
                titleContentBox.Left += titleDecorator.Padding.Left;
                titleContentBox.Top += titleDecorator.Padding.Top;
                titleContentBox.Width -= titleDecorator.Padding.X;

                // HACK: We want to center the title normally (it feels weird if we don't), but we
                //  also want to prevent it from overlapping the arrow
                var offsetX = (titleContentBox.Width - layout.Size.X) / 2f;
                if (Collapsible)
                    offsetX = Math.Max(offsetX, DisclosureArrowPadding);

                var subSettings = settings;
                subSettings.Box = titleBox;
                subSettings.ContentBox = titleContentBox;
                if (Collapsed)
                    subSettings.State |= ControlStates.Pressed; // HACK

                if (layout.DrawCalls.Count > 0) {
                    renderer.Layer += 1;
                    titleDecorator.Rasterize(ref context, ref renderer, ref subSettings);

                    var textPosition = new Vector2(titleContentBox.Left + offsetX, titleContentBox.Top + (titleContentBox.Height - layout.Size.Y) * 0.3f);

                    renderer.Layer += 1;
                    renderer.DrawMultiple(
                        layout.DrawCalls, textPosition.Floor(),
                        samplerState: RenderStates.Text, multiplyColor: titleColor,
                        material: titleMaterial
                    );
                }
            }
        }

        protected override bool OnHitTest (RectF box, Vector2 position, ref HitTestState state) {
            var ok = base.OnHitTest(box, position, ref state);
            if (ok && Collapsed)
                state.Result = this;
            return ok;
        }

        private void RasterizeDisclosureArrow (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            var pad = (DisclosureArrowPadding - DisclosureArrowSize) / 2f;
            var ySpace = ((MostRecentTitleBox.Height - DisclosureArrowSize) / 2f);
            var centering = (float)(Math.Round(DisclosureArrowSize * 0.5f, MidpointRounding.AwayFromZero));
            ySpace = (float)Math.Floor(ySpace);
            settings.Box.SnapAndInset(out Vector2 tl, out Vector2 temp);
            Vector2 a = Vector2.One * -centering, b = new Vector2(DisclosureArrowSize, DisclosureArrowSize) + a, c = new Vector2((a.X + b.X) / 2f, b.Y);
            b.Y = a.Y;
            var radians = (1 - DisclosureLevel.Get(context.NowL)) * (float)(Math.PI * -0.5);
            a = a.Rotate(radians);
            b = b.Rotate(radians);
            c = c.Rotate(radians);
            var offset = new Vector2(tl.X + pad + centering, tl.Y + ySpace + centering);
            var alpha = DisclosureArrowHitTest(context.MousePosition - tl) ? 1.0f : 0.85f;
            pSRGBColor color = pSRGBColor.White(alpha), outlineColor = pSRGBColor.Black(alpha);
            a += offset;
            b += offset;
            c += offset;

            renderer.RasterizeTriangle(
                a, b, c,
                radius: 1f, outlineRadius: 1f,
                innerColor: color, outerColor: color,
                outlineColor: outlineColor
            );
        }

        AbstractString IReadingTarget.Text => Description ?? $"{DescriptionPrefix} {Title}" ?? DescriptionPrefix;

        void IReadingTarget.FormatValueInto (StringBuilder sb) {
            sb.Append(Collapsed ? "Collapsed" : "Expanded");
        }

        public override string ToString () {
            return $"{GetType().Name} #{GetHashCode():X8} '{DebugLabel ?? Title}'";
        }

        bool IFuzzyHitTestTarget.WalkChildren => true;

        int IFuzzyHitTestTarget.WalkTree (List<FuzzyHitTest.Result> output, ref FuzzyHitTest.Result thisControl, Vector2 position, Func<Control, bool> predicate, float maxDistanceSquared) {
            const float shrink = 2;
            var disclosureArrowBox = new RectF(
                thisControl.Rect.Left + shrink, thisControl.Rect.Top + shrink,
                DisclosureArrowPadding - (shrink * 2), MostRecentTitleBox.Height - (shrink * 2)
            );
            if (!thisControl.ClippedRect.Intersection(in disclosureArrowBox, out RectF clippedArrowBox))
                return 0;
            var closestPoint = disclosureArrowBox.Center;
            var distanceSquared = (closestPoint - position).LengthSquared();
            if (distanceSquared > maxDistanceSquared)
                return 0;

            var result = new FuzzyHitTest.Result {
                Control = this,
                Depth = thisControl.Depth + 1,
                // HACK
                ClosestPoint = closestPoint,
                ClippedRect = clippedArrowBox,
                Rect = disclosureArrowBox,
                IsIntangibleAtClosestPoint = false,
                Distance = (float)Math.Sqrt(distanceSquared)
            };
            output.Add(result);
            return 1;
        }
    }
}
