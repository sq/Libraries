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
            public int RunIndex;
            public float X, Y;
        }

        private unsafe void Pass3_Arrange (ref BoxRecord control, ref BoxLayoutResult result, int depth) {
            ref readonly var config = ref control.Config;

            // HACK to fix layout starting with a floating control as the root
            // Without this, tooltips will glitch
            // Ideally we would query the parent's position, but the parent may not be laid out yet
            if ((depth == 0) && config.IsFloating)
                result.Rect.Position = (control.FloatingPosition ?? Vector2.Zero) + control.Margins.TopLeft;

            Vector2 contentPosition = result.Rect.Position + new Vector2(control.Padding.Left, control.Padding.Top),
                contentSpace = result.Rect.Size - control.Padding.Size,
                contentExtent = result.Rect.Extent - control.Padding.BottomRight,
                // HACK: In the event that a box only contains stacked controls, ContentSize will be 0 (is this a bug?)
                //  so we need to also ensure it's at least as big as the content rect
                contentSizeExpanded = new Vector2(Math.Max(contentSpace.X, result.ContentSize.X), Math.Max(contentSpace.Y, result.ContentSize.Y));

            ControlKey firstProcessed = ControlKey.Invalid,
                lastProcessed = ControlKey.Invalid;
            float w = Math.Max(0, result.Rect.Width - control.Padding.X), 
                h = Math.Max(0, result.Rect.Height - control.Padding.Y),
                x = 0, y = 0;

            if (config.GridColumnCount > 0) {
                var columns = stackalloc Pass3Column[config.GridColumnCount];
                float columnWidth = w / config.GridColumnCount;
                int columnIndex = 0;
                foreach (var run in Runs(control.Key)) {
                    columns[columnIndex] = new Pass3Column {
                        RunIndex = run, X = columnWidth * columnIndex,
                        // Make a copy, it's fine, it won't change
                        Run = Run(run)
                    };
                    columnIndex++;
                }

                config.GetRunAlignmentF(out float xAlign, out float yAlign);

                columnIndex = 0;
                foreach (var ckey in Children(control.Key)) {
                    ref var column = ref columns[columnIndex];
                    ref var run = ref column.Run;

                    float baseline = 0; // FIXME

                    x = column.X;
                    Pass3_Arrange_OneChild(
                        control, ref result, depth, config, 
                        contentPosition, contentSpace, contentExtent, 
                        contentSizeExpanded, ref x, ref column.Y, baseline, 
                        xAlign, yAlign, ckey
                    );

                    columnIndex = (columnIndex + 1) % config.GridColumnCount;
                }
            } else {
                foreach (var runIndex in Runs(control.Key)) {
                    ref var run = ref Run(runIndex);
                    bool isLastRun = run.NextRunIndex < 0;
                    float rw = config.IsVertical ? run.MaxOuterWidth : run.TotalWidth,
                        rh = config.IsVertical ? run.TotalHeight : run.MaxOuterHeight;
                    if (config.Clip) {
                        rw = Math.Min(rw, contentSpace.X);
                        rh = Math.Min(rh, contentSpace.Y);
                    }
                    float space = Math.Max(config.IsVertical ? h - rh : w - rw, 0),
                        baseline = config.IsVertical
                            // HACK: The last run needs to have its baseline expanded to our outer edge
                            //  so that anchor bottom/right will hit the edges of our content rect
                            ? (isLastRun ? contentSpace.X - x : run.MaxOuterWidth)
                            : (isLastRun ? contentSpace.Y - y : run.MaxOuterHeight);

                    config.GetRunAlignmentF(out float xAlign, out float yAlign);

                    if (config.IsVertical)
                        y = space * yAlign;
                    else
                        x = space * xAlign;

                    foreach (var ckey in Enumerate(run.First.Key, run.Last.Key)) {
                        if (firstProcessed.IsInvalid)
                            firstProcessed = ckey;
                        lastProcessed = ckey;
                        Pass3_Arrange_OneChild(
                            control, ref result, depth, config, 
                            contentPosition, contentSpace, contentExtent, 
                            contentSizeExpanded, ref x, ref y, baseline, 
                            xAlign, yAlign, ckey
                        );
                    }

                    // HACK: The floating run's contents should not change the position of other controls
                    if (runIndex == result.FloatingRunIndex)
                        ;
                    else if (config.IsVertical) {
                        x += run.MaxOuterWidth;
                        y = 0;
                    } else {
                        x = 0;
                        y += run.MaxOuterHeight;
                    }
                }
            }

            result.ContentRect.Left = Math.Min(result.Rect.Right, result.Rect.Left + control.Padding.Left);
            result.ContentRect.Top = Math.Min(result.Rect.Bottom, result.Rect.Top + control.Padding.Top);
            result.ContentRect.Width = Math.Max(0, result.Rect.Width - control.Padding.X);
            result.ContentRect.Height = Math.Max(0, result.Rect.Height - control.Padding.Y);

            // FIXME
            if (false) {
                Assert(firstProcessed == control.FirstChild);
                Assert(lastProcessed == control.LastChild);
            }
        }

        private void Pass3_Arrange_OneChild (BoxRecord control, ref BoxLayoutResult result, int depth, ControlConfiguration config, Vector2 contentPosition, Vector2 contentSpace, Vector2 contentExtent, Vector2 contentSizeExpanded, ref float x, ref float y, float baseline, float xAlign, float yAlign, ControlKey ckey) {
            ref var child = ref this[ckey];
            ref var childResult = ref Result(ckey);
            ref readonly var childConfig = ref child.Config;
            var childMargins = child.Margins;
            var childOuterSize = childResult.Rect.Size + childMargins.Size;

            childConfig.GetAlignmentF(xAlign, yAlign, out float xChildAlign, out float yChildAlign);

            if (childConfig.IsStackedOrFloating) {
                if (childConfig.IsFloating) {
                    childResult.Rect.Position = contentPosition + (child.FloatingPosition ?? Vector2.Zero) + child.Margins.TopLeft;
                    childResult.AvailableSpace = contentExtent - childResult.Rect.Position - new Vector2(childMargins.Right, childMargins.Bottom);

                    // Unless the floating child has an explicit position, we want to align it within the available space we just calculated
                    if (!child.FloatingPosition.HasValue) {
                        var alignment = (childResult.AvailableSpace - childResult.Rect.Size) * new Vector2(xChildAlign, yChildAlign);
                        alignment.X = Math.Max(alignment.X, 0);
                        alignment.Y = Math.Max(alignment.Y, 0);
                        childResult.Rect.Position += alignment;
                    }
                } else {
                    var stackSpace = (childConfig.AlignToParentBox ? contentSpace : contentSizeExpanded) - childOuterSize;
                    // If the control is stacked and aligned but did not fill the container (size constraints, etc)
                    //  then try to align it
                    stackSpace.X = Math.Max(stackSpace.X, 0f) * xChildAlign;
                    stackSpace.Y = Math.Max(stackSpace.Y, 0f) * yChildAlign;
                    childResult.Rect.Position = contentPosition +
                        new Vector2(stackSpace.X + childMargins.Left, stackSpace.Y + childMargins.Top);
                }
            } else {
                childResult.Rect.Left = contentPosition.X + childMargins.Left + x;
                childResult.Rect.Top = contentPosition.Y + childMargins.Top + y;

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
                    result.ContentSize.X = Math.Max(result.ContentSize.X, childResult.Rect.Right - contentPosition.X);
                    result.ContentSize.Y = Math.Max(result.ContentSize.Y, childResult.Rect.Bottom - contentPosition.Y);
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
