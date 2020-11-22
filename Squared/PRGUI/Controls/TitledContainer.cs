using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.PRGUI.Decorations;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;

namespace Squared.PRGUI.Controls {
    public class TitledContainer : Container {
        public const float MinDisclosureArrowSize = 10,
            DisclosureArrowMargin = 16,
            DisclosureAnimationDuration = 0.175f,
            DisclosureArrowSizeMultiplier = 0.375f;

        private bool _Collapsed;
        public bool Collapsed {
            get => _Collapsed;
            set => SetCollapsed(value, false);
        }

        protected void SetCollapsed (bool value, bool instant = false) {
            if (!Collapsible)
                value = false;
            if (value == _Collapsed)
                return;

            _Collapsed = value;
            var targetValue = (float)(value ? 0 : 1);
            if ((Context != null) && !instant) {
                var nowL = Context.NowL;
                DisclosureLevel = Tween.StartNow(
                    from: DisclosureLevel.Get(nowL), to: targetValue, 
                    seconds: DisclosureAnimationDuration, now: nowL,
                    interpolator: Interpolators<float>.Cosine
                );
            } else
                DisclosureLevel = new Tween<float>(targetValue);
        }

        public bool Collapsible;

        public string Title;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            LineLimit = 1
        };

        protected RectF MostRecentTitleBox;

        private Tween<float> DisclosureLevel = 1f;

        public TitledContainer ()
            : base () {
            AcceptsMouseInput = true;
        }

        protected float DisclosureArrowSize => (float)Math.Round(
            Math.Max(MinDisclosureArrowSize, MostRecentHeaderHeight * DisclosureArrowSizeMultiplier), 
            MidpointRounding.AwayFromZero
        );
        protected float DisclosureArrowPadding => DisclosureArrowSize + DisclosureArrowMargin;

        protected override bool HideChildren => Collapsible && (DisclosureLevel.Get(Context.NowL) <= 0);

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

        protected virtual IDecorator GetTitleDecorator (UIOperationContext context) {
            return context.DecorationProvider?.ContainerTitle ?? 
                context.DecorationProvider?.WindowTitle;
        }

        protected IDecorator UpdateTitle (UIOperationContext context, DecorationSettings settings, out Material material, ref pSRGBColor? color) {
            var decorations = GetTitleDecorator(context);
            if (decorations == null) {
                material = null;
                return null;
            }
            decorations.GetTextSettings(context, settings.State, out material, out IGlyphSource font, ref color);
            TitleLayout.Text = Title;
            TitleLayout.GlyphSource = font;
            TitleLayout.DefaultColor = color?.ToColor() ?? Color.White;
            TitleLayout.LineBreakAtX = settings.ContentBox.Width;
            return decorations;
        }

        protected override bool OnEvent<T> (string name, T args) {
            if (args is MouseEventArgs) {
                if (OnMouseEvent(name, (MouseEventArgs)(object)args))
                    return true;
            }

            return base.OnEvent<T>(name, args);
        }

        protected bool ToggleCollapsed () {
            if (Collapsible && Enabled) {
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
            if (localPosition.Y > MostRecentHeaderHeight)
                return false;
            if ((localPosition.X < 0) || (localPosition.Y < 0))
                return false;

            return true;
        }

        protected virtual bool OnMouseEvent (string name, MouseEventArgs args) {
            if (name == UIEvents.Click) {
                if (Collapsed || DisclosureArrowHitTest(args.RelativeGlobalPosition - args.Box.Position))
                    return ToggleCollapsed();
            }

            return false;
        }

        protected override Margins ComputePadding (UIOperationContext context, IDecorator decorations) {
            var result = base.ComputePadding(context, decorations);
            var titleDecorations = context.DecorationProvider?.WindowTitle;
            if (titleDecorations == null)
                return result;
            if (string.IsNullOrEmpty(Title))
                return result;

            pSRGBColor? color = null;
            titleDecorations.GetTextSettings(context, default(ControlStates), out Material temp, out IGlyphSource font, ref color);
            result.Top += titleDecorations.Margins.Bottom;
            result.Top += titleDecorations.Padding.Top;
            result.Top += titleDecorations.Padding.Bottom;
            result.Top += font.LineSpacing;
            return result;
        }

        protected override void OnDescendantReceivedFocus (Control control, bool isUserInitiated) {
            // If this focus change is the result of a top-level focus change (i.e. selecting a window),
            //  this does not indicate that the user has attempted to focus one of our descendants directly
            //  using tab or some other mechanism, so we shouldn't respond by expanding ourselves.
            // This still means focus will be trapped inside us, but it's better than nothing.
            if (!isUserInitiated)
                return;

            if (Collapsed)
                Collapsed = false;
        }

        protected override void ComputeFixedSize (out float? fixedWidth, out float? fixedHeight) {
            base.ComputeFixedSize(out fixedWidth, out fixedHeight);

            var originalFixedHeight = fixedHeight ?? MostRecentFullSize?.Height;

            if (Collapsible && originalFixedHeight.HasValue) {
                var level = DisclosureLevel.Get(Context.NowL);
                if (level >= 1)
                    return;

                if (fixedHeight.HasValue)
                    fixedHeight = Math.Min(fixedHeight.Value, MostRecentHeaderHeight);
                else
                    fixedHeight = MostRecentHeaderHeight;

                // FIXME: If the size of our content has changed while we were collapsed, this will be wrong
                // HACK: Floor to suppress jittering
                fixedHeight = (float)Math.Floor(Arithmetic.Lerp(fixedHeight.Value, originalFixedHeight.Value, level));
            }
        }

        protected override void OnLayoutComplete (UIOperationContext context, ref bool relayoutRequested) {
            base.OnLayoutComplete(context, ref relayoutRequested);

            if (Collapsible && !Collapsed && 
                (DisclosureLevel.Get(Context.NowL) >= 1)
            ) {
                var box = GetRect(context.Layout);
                MostRecentFullSize = box;
            }
        }

        protected override void ComputeSizeConstraints (out float? minimumWidth, out float? minimumHeight, out float? maximumWidth, out float? maximumHeight) {
            // FIXME
            base.ComputeSizeConstraints(out minimumWidth, out minimumHeight, out maximumWidth, out maximumHeight);
        }

        protected override void OnRasterize (UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings, IDecorator decorations) {
            base.OnRasterize(context, ref renderer, settings, decorations);

            IDecorator titleDecorator;
            pSRGBColor? titleColor = null;
            if (
                (titleDecorator = UpdateTitle(context, settings, out Material titleMaterial, ref titleColor)) != null
            ) {
                if (Collapsible && (context.Pass == RasterizePasses.Above))
                    RasterizeDisclosureArrow(ref context, ref renderer, settings);

                if (context.Pass != RasterizePasses.Below)
                    return;

                var layout = TitleLayout.Get();
                var titleBox = settings.Box;
                titleBox.Height = titleDecorator.Padding.Top + titleDecorator.Padding.Bottom + TitleLayout.GlyphSource.LineSpacing;
                // FIXME: Compute this somewhere else, like in OnLayoutComplete
                MostRecentHeaderHeight = titleBox.Height;
                MostRecentTitleBox = titleBox;

                var titleContentBox = titleBox;
                titleContentBox.Left += titleDecorator.Padding.Left;
                titleContentBox.Top += titleDecorator.Padding.Top;
                titleContentBox.Width -= titleDecorator.Padding.X;

                // HACK: We want to center the title normally (it feels weird if we don't), but we
                //  also want to prevent it from overlapping the arrow
                var offsetX = (titleContentBox.Width - layout.Size.X) / 2f;
                offsetX = Math.Max(offsetX, DisclosureArrowPadding);

                var subSettings = settings;
                subSettings.Box = titleBox;
                subSettings.ContentBox = titleContentBox;
                if (Collapsed)
                    subSettings.State |= ControlStates.Pressed; // HACK

                renderer.Layer += 1;
                titleDecorator.Rasterize(context, ref renderer, subSettings);

                var textPosition = new Vector2(titleContentBox.Left + offsetX, titleContentBox.Top);

                renderer.Layer += 1;
                renderer.DrawMultiple(
                    layout.DrawCalls, textPosition.Floor(),
                    samplerState: RenderStates.Text, multiplyColor: titleColor?.ToColor()
                );
            }
        }

        private void RasterizeDisclosureArrow (ref UIOperationContext context, ref ImperativeRenderer renderer, DecorationSettings settings) {
            var pad = (DisclosureArrowPadding - DisclosureArrowSize) / 2f;
            var ySpace = ((MostRecentHeaderHeight - DisclosureArrowSize) / 2f);
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
            var alpha = DisclosureArrowHitTest(context.MousePosition - tl) ? 1.0f : 0.75f;
            var color = Color.White * alpha;
            var outlineColor = Color.Black * (0.8f * alpha);
            a += offset;
            b += offset;
            c += offset;

            renderer.RasterizeTriangle(
                a, b, c,
                radius: 1f, outlineRadius: 1.1f,
                innerColor: color, outerColor: color,
                outlineColor: outlineColor
            );
        }
    }
}
