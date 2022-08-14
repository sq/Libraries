using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private void Pass3_Arrange (ref BoxRecord control, ref BoxLayoutResult result) {
            ref readonly var config = ref control.Config;

            if (config.IsFloating)
                result.Rect.Position = control.FloatingPosition;

            result.ContentRect = result.Rect;
            result.ContentRect.Left += control.Padding.Left;
            result.ContentRect.Top += control.Padding.Top;
            result.ContentRect.Width = Math.Max(0, result.ContentRect.Width - control.Padding.X);
            result.ContentRect.Height = Math.Max(0, result.ContentRect.Height - control.Padding.Y);

            ControlKey firstProcessed = ControlKey.Invalid,
                lastProcessed = ControlKey.Invalid;
            float w = result.ContentRect.Width, h = result.ContentRect.Height,
                x = 0, y = 0;

            foreach (var runIndex in Runs(control.Key)) {
                ref var run = ref Run(runIndex);
                bool isLastRun = run.NextRunIndex < 0;
                float rw = config.IsVertical ? run.MaxOuterWidth : run.TotalWidth,
                    rh = config.IsVertical ? run.TotalHeight : run.MaxOuterHeight,
                    space = Math.Max(config.IsVertical ? h - rh : w - rw, 0),
                    baseline = config.IsVertical
                        // HACK: The last run needs to have its baseline expanded to our outer edge
                        //  so that anchor bottom/right will hit the edges of our content rect
                        ? (isLastRun ? result.ContentRect.Size.X - x : run.MaxOuterWidth)
                        : (isLastRun ? result.ContentRect.Size.Y - y : run.MaxOuterHeight);

                config.GetRunAlignmentF(out float xAlign, out float yAlign);

                if (config.IsVertical)
                    y = space * yAlign;
                else
                    x = space * xAlign;

                foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                    if (firstProcessed.IsInvalid)
                        firstProcessed = ckey;
                    lastProcessed = ckey;
                    ref var child = ref this[ckey];
                    ref var childResult = ref Result(ckey);
                    ref readonly var childConfig = ref child.Config;
                    var childMargins = child.Margins;
                    var childOuterSize = childResult.Rect.Size + childMargins.Size;

                    childConfig.GetRunAlignmentF(out float xChildAlign, out float yChildAlign);

                    if (childConfig.IsStackedOrFloating) {
                        if (childConfig.IsFloating) {
                            childResult.Rect.Position = child.FloatingPosition;
                            childResult.AvailableSpace = result.ContentRect.Extent - childResult.Rect.Position - new Vector2(childMargins.Right, childMargins.Bottom);
                        } else {
                            var stackSpace = result.ContentRect.Size - childOuterSize;
                            // If the control is stacked and aligned but did not fill the container (size constraints, etc)
                            //  then try to align it
                            stackSpace.X = Math.Max(stackSpace.X, 0f) * xChildAlign;
                            stackSpace.Y = Math.Max(stackSpace.Y, 0f) * yChildAlign;
                            childResult.Rect.Position = result.ContentRect.Position +
                                new Vector2(stackSpace.X + childMargins.Left, stackSpace.Y + childMargins.Top);
                        }
                    } else {
                        childResult.Rect.Left = result.ContentRect.Left + childMargins.Left + x;
                        childResult.Rect.Top = result.ContentRect.Top + childMargins.Top + y;

                        if (config.IsVertical) {
                            var alignment = (xChildAlign * Math.Max(0, baseline - childOuterSize.X));
                            if (alignment != 0)
                                childResult.Rect.Left += alignment;
                            y += childOuterSize.Y;
                        } else {
                            var alignment = (yChildAlign * Math.Max(0, baseline - childOuterSize.Y));
                            if (alignment != 0)
                                childResult.Rect.Top += alignment;
                            x += childOuterSize.X;
                        }

                        // TODO: Clip left/top edges as well?
                        // TODO: Separate x/y
                        if ((config._ContainerFlags & Enums.ContainerFlag.Boxes_Clip) != default) {
                            var rightEdge = result.ContentRect.Right - childMargins.Right;
                            childResult.Rect.Width = Math.Max(0, Math.Min(childResult.Rect.Width, rightEdge - childResult.Rect.Left));
                            var bottomEdge = result.ContentRect.Bottom - childMargins.Bottom;
                            childResult.Rect.Height = Math.Max(0, Math.Min(childResult.Rect.Height, bottomEdge - childResult.Rect.Top));
                        }
                    }

                    Pass3_Arrange(ref child, ref childResult);
                }

                if (config.IsVertical) {
                    x += run.MaxOuterWidth;
                    y = 0;
                } else {
                    x = 0;
                    y += run.MaxOuterHeight;
                }
            }

            if (
                ((control.Config.Flags & Enums.BoxFlags.Floating) == Enums.BoxFlags.Floating) && 
                (result.Rect.Position != control.FloatingPosition)
            )
                throw new Exception();

            result.ContentRect.Position = result.Rect.Position + new Vector2(control.Padding.Left, control.Padding.Top);

            // FIXME
            if (false) {
                Assert(firstProcessed == control.FirstChild);
                Assert(lastProcessed == control.LastChild);
            }
        }
    }
}
