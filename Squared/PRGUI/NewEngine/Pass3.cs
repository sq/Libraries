using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI.NewEngine {
    public partial class LayoutEngine {
        private struct Pass3Column {
            public LayoutRun Run;
            public float X, Y;
        }

        private struct Pass3Locals {
            public Vector2 contentPosition, contentSpace, contentExtent, contentSizeExpanded;
        }

        private unsafe void Pass3_Arrange (ref BoxRecord control, ref BoxLayoutResult result, int depth) {
            ref readonly var config = ref control.Config;
            var isVertical = config.IsVertical;

            // HACK to fix layout starting with a floating control as the root
            // Without this, tooltips will glitch
            // Ideally we would query the parent's position, but the parent may not be laid out yet
            if ((depth == 0) && config.IsFloating)
                result.Rect.Position = (control.FloatingPosition ?? Vector2.Zero) + control.Margins.TopLeft;

            // HACK: Don't calculate contentPosition and stuff if we have no children, it's a waste of time
            if (!control.FirstChild.IsInvalid) {
                var locals = new Pass3Locals {
                    contentPosition = result.Rect.Position + new Vector2(control.Padding.Left, control.Padding.Top),
                    contentSpace = result.Rect.Size - control.Padding.Size,
                    contentExtent = result.Rect.Extent - control.Padding.BottomRight,
                };
                // HACK: In the event that a box only contains stacked controls, ContentSize will be 0 (is this a bug?)
                //  so we need to also ensure it's at least as big as the content rect
                locals.contentSizeExpanded = new Vector2(Math.Max(locals.contentSpace.X, result.ContentSize.X), Math.Max(locals.contentSpace.Y, result.ContentSize.Y));

                float w = Math.Max(0, result.Rect.Width - control.Padding.X), 
                    h = Math.Max(0, result.Rect.Height - control.Padding.Y),
                    x = 0, y = 0;

                if (config.GridColumnCount > 0) {
                    var columns = stackalloc Pass3Column[config.GridColumnCount];
                    float columnWidth = w / config.GridColumnCount;
                    int columnIndex = 0;
                    foreach (ref var run in Runs(ref result)) {
                        columns[columnIndex] = new Pass3Column {
                            X = columnWidth * columnIndex,
                            // Make a copy, it's fine, it won't change
                            Run = run
                        };
                        columnIndex++;
                    }

                    config.GetRunAlignmentF(out float xAlign, out float yAlign);

                    columnIndex = 0;
                    ref var child = ref FirstChild(ref control);
                    while (!child.IsInvalid) {
                        ref var column = ref columns[columnIndex];
                        ref var run = ref column.Run;

                        float baseline = 0; // FIXME

                        x = column.X;
                        Pass3_Arrange_OneChild(
                            ref control, ref result, depth, config, 
                            ref locals, ref x, ref column.Y, baseline, 
                            xAlign, yAlign, ref child
                        );

                        columnIndex = (columnIndex + 1) % config.GridColumnCount;
                        child = ref NextSibling(ref child);
                    }
                } else {
                    foreach (ref var run in Runs(ref result)) {
                        bool isLastRun = run.NextRunIndex < 0;
                        float rw = isVertical ? run.MaxOuterWidth : run.TotalWidth,
                            rh = isVertical ? run.TotalHeight : run.MaxOuterHeight;
                        if (config.Clip) {
                            rw = Math.Min(rw, locals.contentSpace.X);
                            rh = Math.Min(rh, locals.contentSpace.Y);
                        }
                        float space = Math.Max(isVertical ? h - rh : w - rw, 0),
                            baseline = isVertical
                                // HACK: The last run needs to have its baseline expanded to our outer edge
                                //  so that anchor bottom/right will hit the edges of our content rect
                                ? (isLastRun ? locals.contentSpace.X - x : run.MaxOuterWidth)
                                : (isLastRun ? locals.contentSpace.Y - y : run.MaxOuterHeight);

                        config.GetRunAlignmentF(out float xAlign, out float yAlign);

                        if (isVertical)
                            y = space * yAlign;
                        else
                            x = space * xAlign;

                        ref var child = ref FirstItemInRun(ref run);
                        var stopAt = run.Last.Key;
                        while (!child.IsInvalid) {
                            Pass3_Arrange_OneChild(
                                ref control, ref result, depth, config, 
                                ref locals, ref x, ref y, baseline, 
                                xAlign, yAlign, ref child
                            );
                            child = ref NextSibling(ref child, stopAt);
                        }

                        // HACK: The floating run's contents should not change the position of other controls
                        if (run.IsFloating)
                            ;
                        else if (isVertical) {
                            x += run.MaxOuterWidth;
                            y = 0;
                        } else {
                            x = 0;
                            y += run.MaxOuterHeight;
                        }
                    }
                }
            }

            result.ContentRect.Left = Math.Min(result.Rect.Right, result.Rect.Left + control.Padding.Left);
            result.ContentRect.Top = Math.Min(result.Rect.Bottom, result.Rect.Top + control.Padding.Top);
            result.ContentRect.Width = Math.Max(0, result.Rect.Width - control.Padding.X);
            result.ContentRect.Height = Math.Max(0, result.Rect.Height - control.Padding.Y);
        }

        private void Pass3_Arrange_OneChild (
            ref BoxRecord control, ref BoxLayoutResult result, 
            int depth, ControlConfiguration config, ref Pass3Locals locals, 
            ref float x, ref float y, float baseline, float xAlign, float yAlign, 
            ref BoxRecord child
        ) {
            ref var childResult = ref Result(child.Key);
            ref readonly var childConfig = ref child.Config;
            var childMargins = child.Margins;
            var childOuterSize = childResult.Rect.Size + childMargins.Size;
            var stackedOrFloating = childConfig.IsStackedOrFloating;

            childConfig.GetAlignmentF(xAlign, yAlign, out float xChildAlign, out float yChildAlign, stackedOrFloating);

            if (stackedOrFloating) {
                if (childConfig.IsFloating) {
                    childResult.Rect.Position = locals.contentPosition + (child.FloatingPosition ?? Vector2.Zero) + child.Margins.TopLeft;
                    childResult.AvailableSpace = locals.contentExtent - childResult.Rect.Position - new Vector2(childMargins.Right, childMargins.Bottom);

                    // Unless the floating child has an explicit position, we want to align it within the available space we just calculated
                    if (!child.FloatingPosition.HasValue) {
                        var alignment = (childResult.AvailableSpace - childResult.Rect.Size) * new Vector2(xChildAlign, yChildAlign);
                        alignment.X = Math.Max(alignment.X, 0);
                        alignment.Y = Math.Max(alignment.Y, 0);
                        childResult.Rect.Position += alignment;
                    }
                } else {
                    var stackSpace = (childConfig.AlignToParentBox ? locals.contentSpace : locals.contentSizeExpanded) - childOuterSize;
                    // If the control is stacked and aligned but did not fill the container (size constraints, etc)
                    //  then try to align it
                    stackSpace.X = Math.Max(stackSpace.X, 0f) * xChildAlign;
                    stackSpace.Y = Math.Max(stackSpace.Y, 0f) * yChildAlign;
                    childResult.Rect.Position = locals.contentPosition +
                        new Vector2(stackSpace.X + childMargins.Left, stackSpace.Y + childMargins.Top);
                }
            } else {
                childResult.Rect.Left = locals.contentPosition.X + childMargins.Left + x;
                childResult.Rect.Top = locals.contentPosition.Y + childMargins.Top + y;

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

                if (!child.Config.NoMeasurement) {
                    result.ContentSize.X = Math.Max(result.ContentSize.X, childResult.Rect.Right - locals.contentPosition.X);
                    result.ContentSize.Y = Math.Max(result.ContentSize.Y, childResult.Rect.Bottom - locals.contentPosition.Y);
                }

                // TODO: Clip left/top edges as well?
                // TODO: Separate x/y
                if ((config._ContainerFlags & Enums.ContainerFlag.Boxes_Clip) != default) {
                    var rightEdge = result.Rect.Right - control.Padding.Right - childMargins.Right;
                    childResult.Rect.Width = Math.Max(0, Math.Min(childResult.Rect.Width, rightEdge - childResult.Rect.Left));
                    var bottomEdge = result.Rect.Bottom - control.Padding.Bottom - childMargins.Bottom;
                    childResult.Rect.Height = Math.Max(0, Math.Min(childResult.Rect.Height, bottomEdge - childResult.Rect.Top));
                }
            }

            Pass3_Arrange(ref child, ref childResult, depth + 1);
        }
    }
}
