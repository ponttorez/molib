using System.Runtime.CompilerServices;
using Gum.Forms.Controls;
using Gum.Wireframe;
using Molib.Integration;
using Molib.Motion;

namespace Molib.Interaction;

/// <summary>
/// Runs the inertia of every scroll viewer that uses it: only the ones that are pressed, coasting, springing or
/// waiting to snap.
/// </summary>
/// <remarks>
/// A viewer is found from the focused element through a weak table, so attaching inertia does not keep a viewer
/// alive after the game drops it.
/// ADR: interaction/scrolling — Decision; foundation/architecture — Zero work in an idle frame.
/// </remarks>
internal sealed class Scrolling
{
    private readonly MolibService _molib;
    private readonly ConditionalWeakTable<ScrollViewer, ScrollInertia> _inertias = [];
    private readonly List<ScrollInertia> _active = [];
    private IInputReceiver? _lastReceiver;

    /// <summary>
    /// Creates the module.
    /// </summary>
    /// <param name="molib">The service that owns the module.</param>
    public Scrolling(MolibService molib)
    {
        _molib = molib;
    }

    /// <summary>
    /// Number of the current frame; a scroll change made by Gum before <c>MolibService.Update</c> belongs to the
    /// frame that update handles.
    /// </summary>
    public int Frame { get; private set; }

    /// <summary>
    /// The motion core: time scale and the count of running animations.
    /// </summary>
    public MotionCore Motion => _molib.Motion;

    /// <summary>
    /// The input module: the last device, the focus and paging.
    /// </summary>
    public Input Input => _molib.Input;

    /// <summary>
    /// The sound module, for the sound event of a snap.
    /// </summary>
    public Sound Sound => _molib.Sound;

    /// <summary>
    /// Gives <paramref name="viewer"/> touch inertia and snapping.
    /// </summary>
    /// <param name="viewer">The scroll viewer.</param>
    /// <param name="options">How it coasts and snaps.</param>
    /// <returns>The inertia of the viewer.</returns>
    /// <exception cref="InvalidOperationException">The viewer already uses inertia.</exception>
    public ScrollInertia Attach(ScrollViewer viewer, InertiaOptions options)
    {
        if (_inertias.TryGetValue(viewer, out _))
        {
            throw new InvalidOperationException("This scroll viewer already uses inertia.");
        }

        var inertia = new ScrollInertia(this, viewer, options);
        _inertias.Add(viewer, inertia);
        return inertia;
    }

    /// <summary>
    /// Puts <paramref name="inertia"/> into the list of active ones until it has no more work.
    /// </summary>
    /// <param name="inertia">An inertia of this module.</param>
    public void Activate(ScrollInertia inertia)
    {
        if (!inertia.IsListed)
        {
            inertia.IsListed = true;
            _active.Add(inertia);
        }
    }

    /// <summary>
    /// Ends every press record without a fling: drag and drop took the pointer, and the scroll values it moves at
    /// the edges must not turn into a fling.
    /// </summary>
    public void StopTracking()
    {
        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].StopTracking();
        }
    }

    /// <summary>
    /// Reacts to a focus change and moves the active inertias.
    /// </summary>
    /// <param name="deltaSeconds">Frame time, already limited by the service.</param>
    public void Update(float deltaSeconds)
    {
        CheckFocus();
        if (_active.Count > 0)
        {
            bool isPointerDown = FrameworkElement.MainCursor.PrimaryDown;
            float scaledSeconds = deltaSeconds * Motion.TimeScale;
            for (int i = 0; i < _active.Count; i++)
            {
                ScrollInertia inertia = _active[i];
                if (!inertia.Update(isPointerDown, deltaSeconds, scaledSeconds))
                {
                    inertia.IsListed = false;
                    _active.RemoveAt(i);
                    i--;
                }
            }
        }

        Frame++;
    }

    /// <remarks>
    /// One reference comparison per frame; the walk up the tree runs only when the focus changed. While the cursor
    /// is the last device, <c>Input</c> has already hidden the focus, except on a text field.
    /// ADR: interaction/scrolling — Focus and scrolling.
    /// </remarks>
    private void CheckFocus()
    {
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        if (ReferenceEquals(receiver, _lastReceiver))
        {
            return;
        }

        _lastReceiver = receiver;
        if (receiver is not FrameworkElement focused)
        {
            return;
        }

        bool isNavigation = _molib.Input.LastDevice == InputDeviceKind.Navigation;
        bool isTouch = FrameworkElement.MainCursor.LastInputDevice == InputDevice.TouchScreen;
        for (GraphicalUiElement? current = focused.Visual; current is not null;)
        {
            ScrollViewer? viewer = VisualTree.FindScrollViewer(current);
            if (viewer is null)
            {
                return;
            }

            if (_inertias.TryGetValue(viewer, out ScrollInertia? inertia))
            {
                inertia.OnFocusEntered(focused, isNavigation, isTouch);
            }

            current = viewer.Visual;
        }
    }
}
