using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util.Event;

namespace Squared.PRGUI {
    public interface IModal {
        event Action<IModal> Shown;
        event Action<IModal> Closed;
        /// <summary>
        /// Focus was transferred to this control from another control, and it will
        ///  be returned when this control goes away. Used for menus and modal dialogs
        /// </summary>
        Control FocusDonor { get; }
        /// <summary>
        /// While this modal is active, any tests that would normally hit a control below it will hit nothing.
        /// </summary>
        bool BlockHitTests { get; }
        /// <summary>
        /// While this modal is active, controls below it cannot receive input.
        /// </summary>
        bool BlockInput { get; }
        /// <summary>
        /// Focus will not be allowed to leave this modal while it is active.
        /// </summary>
        bool RetainFocus { get; }
        /// <summary>
        /// While this modal is active, any controls beneath it will be darkened by this much (0.0 = no fade, 1.0 = default fade level)
        /// </summary>
        float BackgroundFadeLevel { get; }
        /// <summary>
        /// The modal allows programmatic closing (with Close()). You can still override this with force: true
        /// </summary>
        bool AllowProgrammaticClose { get; }
        void Show (UIContext context);
        /// <summary>
        /// Attempts to close the modal.
        /// </summary>
        /// <param name="force">Overrides any preference for the modal not to close.</param>
        /// <returns>true if the modal was successfully closed.</returns>
        bool Close (bool force);
        bool OnUnhandledKeyEvent (string name, KeyEventArgs args);
        bool OnUnhandledEvent (string name, IEventInfo args);
    }

    public interface IControlCompositor {
        /// <summary>
        /// Decides whether the control needs to be composited given its current state at the specified opacity.
        /// </summary>
        bool WillComposite (Control control, float opacity);
        /// <summary>
        /// Invoked immediately before the compositor draw operation is issued to the GPU. This is the appropriate
        ///  time to update material uniforms for the composite operation.
        /// </summary>
        void BeforeIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall);
        /// <summary>
        /// Composites the control into the scene using the provided renderer and draw call data.
        /// </summary>
        void Composite (Control control, ref ImperativeRenderer renderer, ref BitmapDrawCall drawCall);
        /// <summary>
        /// Invoked immediately after the compositor draw operation is issued to the GPU.
        /// If you made any state changes in BeforeIssueComposite, you should undo them here.
        /// </summary>
        void AfterIssueComposite (Control control, DeviceManager dm, ref BitmapDrawCall drawCall);
    }

    public interface IControlContainer {
        int ChildrenToSkipWhenBuilding { get; }
        bool ClipChildren { get; set; }
        bool ChildrenAcceptFocus { get; }
        Control DefaultFocusTarget { get; }
        ControlFlags ContainerFlags { get; }
        ControlCollection Children { get; }
        /// <summary>
        /// Invoked to notify a container that one of its descendants (either a direct child or
        ///  indirect child) has received focus either as a result of user input or some other
        ///  automatic state change.
        /// </summary>
        void DescendantReceivedFocus (Control descendant, bool isUserInitiated);
        bool IsControlHidden (Control child);
    }

    public interface IScrollableControl {
        bool AllowDragToScroll { get; }
        bool Scrollable { get; set; }
        Vector2 ScrollOffset { get; }
        Vector2? MinScrollOffset { get; }
        Vector2? MaxScrollOffset { get; }
        bool TrySetScrollOffset (Vector2 value, bool forUser);
    }

    public interface IPartiallyIntangibleControl {
        bool IsIntangibleAtPosition (Vector2 position);
    }

    public interface IPostLayoutListener {
        /// <summary>
        /// This method will be invoked after the full layout pass has been completed, so this control,
        ///  its parent, and its children will all have valid boxes.
        /// </summary>
        /// <param name="relayoutRequested">Request a second layout pass (if you've changed constraints, etc)</param>
        void OnLayoutComplete (ref UIOperationContext context, ref bool relayoutRequested);
    }

    public interface ISelectionBearer {
        bool HasSelection { get; }
        RectF? SelectionRect { get; }
        Control SelectedControl { get; }
    }

    public interface IValueControl<T> {
        T Value { get; set; }
    }
}
