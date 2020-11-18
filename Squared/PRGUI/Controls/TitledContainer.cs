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

namespace Squared.PRGUI.Controls {
    public class TitledContainer : Container {
        public const float DisclosureArrowSize = 14, 
            DisclosureArrowPadding = DisclosureArrowSize + 12;

        public bool Collapsed;
        public bool Collapsible;

        public string Title;

        protected DynamicStringLayout TitleLayout = new DynamicStringLayout {
            LineLimit = 1
        };

        protected RectF MostRecentTitleBox;

        public TitledContainer ()
            : base () {
            AcceptsMouseInput = true;
        }

        protected override bool HideChildren => Collapsible && Collapsed;

        protected virtual IDecorator GetTitleDecorator (UIOperationContext context) {
            return context.DecorationProvider?.WindowTitle;
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

            if (Collapsed) {
                if (fixedHeight.HasValue)
                    fixedHeight = Math.Min(fixedHeight.Value, MostRecentHeaderHeight);
                else
                    fixedHeight = MostRecentHeaderHeight;
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
                if (Collapsible && (context.Pass == RasterizePasses.Above)) {
                    var pad = (DisclosureArrowPadding - DisclosureArrowSize) / 2f;
                    var ySpace = ((MostRecentHeaderHeight - DisclosureArrowSize) / 2f);
                    var centering = DisclosureArrowSize * 0.5f;
                    Vector2 a = Vector2.One * -centering,
                        b = new Vector2(DisclosureArrowSize, DisclosureArrowSize) + a,
                        c = new Vector2((a.X + b.X) / 2f, b.Y);
                    b.Y = a.Y;
                    if (Collapsed) {
                        var radians = (float)(Math.PI * -0.5);
                        a = a.Rotate(radians);
                        b = b.Rotate(radians);
                        c = c.Rotate(radians);
                    }
                    var offset = new Vector2(settings.Box.Left + pad + centering, settings.Box.Top + ySpace + centering);
                    var color = Color.White;
                    var outlineColor = Color.Black * 0.8f;

                    renderer.RasterizeTriangle(
                        a + offset, b + offset, c + offset,
                        radius: 1f, outlineRadius: 1.1f,
                        innerColor: color, outerColor: color, 
                        outlineColor: outlineColor
                    );
                }

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
    }
}
