using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Input;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using Squared.Util.Text;

namespace Squared.PRGUI {
    public sealed partial class UIContext : IDisposable {
        public static readonly HashSet<Keys> ModifierKeys = new HashSet<Keys> {
            Keys.LeftAlt,
            Keys.LeftControl,
            Keys.LeftShift,
            Keys.RightAlt,
            Keys.RightControl,
            Keys.RightShift,
            Keys.LeftWindows,
            Keys.RightWindows
        };

        public static readonly HashSet<Keys> SuppressRepeatKeys = new HashSet<Keys> {
            Keys.Escape,
            Keys.CapsLock,
            Keys.Scroll,
            Keys.NumLock,
            Keys.Insert
        };

        public bool LogRelayoutRequests = false;

        /// <summary>
        /// Reserves empty space around composited controls on all sides to create room for drop shadows and
        ///  any other decorations that may extend outside the control's rectangle.
        /// </summary>
        public int CompositorPaddingPx = 16;

        public float BackgroundFadeOpacity = 0.2f;
        public float BackgroundFadeDuration = 0.2f;

        // Allocate scratch rendering buffers (for composited controls) at a higher or lower resolution
        //  than the canvas, to improve the quality of transformed imagery
        public const float DefaultScratchScaleFactor = 1.0f;

        // Full occlusion tests are performed with this padding region (in pixels) to account for things like
        //  drop shadows being visible even if the control itself is not
        public const float VisibilityPadding = 16;

        /// <summary>
        /// If set, it is possible for Focused to become null. Otherwise, the context will attempt to ensure
        ///  that a control is focused at all times
        /// </summary>
        public bool AllowNullFocus = true;

        /// <summary>
        /// Globally tracks whether text editing should be in insert or overwrite mode
        /// </summary>
        public bool TextInsertionMode = true;

        /// <summary>
        /// Tooltips will only appear after the mouse remains over a single control for this long (in seconds)
        /// </summary>
        public float TooltipAppearanceDelay = 0.3f;
        /// <summary>
        /// If the mouse leaves tooltip-bearing controls for this long (in seconds) the tooltip appearance delay will reset
        /// </summary>
        public float TooltipDisappearDelay = 0.2f;
        public float TooltipFadeDurationFast = 0.1f;
        public float TooltipFadeDuration = 0.2f;

        /// <summary>
        /// Constrains the size of tooltips. If the tooltip becomes too wide, its content will be wrapped.
        /// If the tooltip becomes too tall, its content will shrink.
        /// </summary>
        public Vector2 MaxTooltipSize = new Vector2(0.4f, 0.5f);

        /// <summary>
        /// Double-clicks will only be tracked if this far apart or less (in seconds)
        /// </summary>
        public double DoubleClickWindowSize = 0.4;
        /// <summary>
        /// If the mouse is only moved this far (in pixels) it will be treated as no movement for the purposes of click detection
        /// </summary>
        public float MinimumMouseMovementDistance = 10;
        /// <summary>
        /// If the mouse has moved more than the minimum movement distance, do not generate a click event if a scroll occurred.
        /// Otherwise, a click event will be generated as long as the mouse is still within the target control.
        /// If this is not set, drag-to-scroll may misbehave.
        /// </summary>
        public bool SuppressSingleClickOnMovementWhenAppropriate = true;
        /// <summary>
        /// If the mouse moves more than the minimum movement distance, convert it into scrolling instead of
        ///  a click, when appropriate
        /// </summary>
        public bool EnableDragToScroll = true;
        /// <summary>
        /// Drag-to-scroll will have its movement speed increased by this factor
        /// </summary>
        public float DragToScrollSpeed = 1.0f;

        public float AutoscrollMargin = 4;
        public float AutoscrollSpeedSlow = 6;
        public float AutoscrollSpeedFast = 80;
        public float AutoscrollFastThreshold = 512;
        public float AutoscrollInstantThreshold = 1200;

        /// <summary>
        /// A key must be held for this long (in seconds) before repeating begins
        /// </summary>
        public double FirstKeyRepeatDelay = 0.4;
        /// <summary>
        /// Key repeating begins at this rate (in seconds)
        /// </summary>
        public double KeyRepeatIntervalSlow = 0.09;
        /// <summary>
        /// Key repeating accelerates to this rate (in seconds) over time
        /// </summary>
        public double KeyRepeatIntervalFast = 0.03;
        /// <summary>
        /// The key repeating rate accelerates over this period of time (in seconds)
        /// </summary>
        public double KeyRepeatAccelerationDelay = 4.5;

        private bool _ScratchRenderTargetsInvalid = false;
        private float _ScratchScaleFactor = DefaultScratchScaleFactor;

        public float ScratchScaleFactor {
            get => _ScratchScaleFactor;
            set {
                value = (float)Math.Round(Arithmetic.Clamp(value, 0.5f, 4f), 2, MidpointRounding.AwayFromZero);
                if (value == _ScratchScaleFactor)
                    return;
                _ScratchScaleFactor = value;
                _ScratchRenderTargetsInvalid = true;
            }
        }

        /// <summary>
        /// Configures the appearance and size of controls
        /// </summary>
        public IDecorationProvider Decorations;
        public IAnimationProvider Animations;

        public ITimeProvider TimeProvider { get; private set; }

        public readonly List<IInputSource> InputSources = new List<IInputSource>();

        public DefaultMaterialSet Materials { get; private set; }
        public RichTextConfiguration RichTextConfiguration = new RichTextConfiguration();

        public event Action<string> OnLogMessage;

        private UnorderedList<MouseEventArgs> SpareMouseEventArgs = new UnorderedList<MouseEventArgs>(),
            PurgatoryMouseEventArgs = new UnorderedList<MouseEventArgs>(),
            UsedMouseEventArgs = new UnorderedList<MouseEventArgs>();
    }
}
