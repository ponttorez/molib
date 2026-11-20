using System.Runtime.CompilerServices;
using Gum.Forms.Controls;
using Gum.Forms.Controls.Primitives;
using Gum.Wireframe;
using Molib.Interaction;
using Molib.Widgets;

namespace Molib.Integration;

/// <summary>
/// Turns what the player does in the interface into sound events for the game. molib plays nothing: the game
/// decides which sound to play, if any.
/// </summary>
/// <remarks>
/// <para>
/// Events come only from the player: molib listens to the events Gum raises from input (<c>Click</c>, <c>Push</c>,
/// <c>ValueChangedByUi</c>), so values set by code stay silent. Gum raises <c>SelectionChanged</c> for selections by
/// code too; a selection counts as the player's only when the pointer pressed the list in this frame, or the list
/// has the focus and a navigation key or button is pressed in this frame.
/// </para>
/// <para>
/// The subscriptions live on the controls themselves: a control the game throws away takes them along, and a
/// control taken out of the tree gets no input and stays silent. ADR: integration/sound.
/// </para>
/// </remarks>
public sealed class Sound
{
    private const double ValueStepInterval = 0.05;

    private static readonly object _mark = new();

    private readonly MolibService _molib;
    private readonly ConditionalWeakTable<FrameworkElement, object> _attached = [];
    private readonly ConditionalWeakTable<GraphicalUiElement, object> _muted = [];
    private double _seconds;
    private double _lastValueStep = double.NegativeInfinity;

    internal Sound(MolibService molib)
    {
        _molib = molib;
    }

    /// <summary>
    /// Whether events are raised at all; <see langword="true"/> by default.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Raised when the player does something that may have a sound, with the element it happened on.
    /// </summary>
    public event Action<UiSoundKind, GraphicalUiElement>? Triggered;

    /// <summary>
    /// Listens to the Forms controls in the subtree of <paramref name="element"/>. Attaching a control again does
    /// nothing; controls added to the subtree later need another call.
    /// </summary>
    /// <param name="element">Root of the subtree, for example a screen.</param>
    /// <remarks>
    /// The walk stops at controls built of parts (buttons, sliders, combo boxes, text fields, list boxes) and goes
    /// only into the content of a scroll viewer, so scroll bars and thumbs stay silent. Rows of lists sound through
    /// the selection of their list. Layers and toasts attach their content themselves.
    /// ADR: integration/sound — Attaching.
    /// </remarks>
    public void Attach(GraphicalUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        AttachTree(element);
    }

    /// <summary>
    /// Silences <paramref name="element"/> and everything inside it, including layer and toast events of that
    /// content.
    /// </summary>
    /// <param name="element">The element to silence.</param>
    public void Mute(GraphicalUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _muted.AddOrUpdate(element, _mark);
    }

    /// <summary>
    /// Lets an element silenced by <see cref="Mute"/> sound again.
    /// </summary>
    /// <param name="element">The element to let sound.</param>
    public void Unmute(GraphicalUiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        _muted.Remove(element);
    }

    /// <summary>
    /// Raises <see cref="Triggered"/> unless sound events are off or the element is muted.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="element">Where it happened.</param>
    internal void Raise(UiSoundKind kind, GraphicalUiElement element)
    {
        if (!Enabled || Triggered is null || IsMuted(element))
        {
            return;
        }

        Triggered(kind, element);
    }

    /// <summary>
    /// Advances the clock of the slider steps and looks for a press on a disabled control.
    /// </summary>
    /// <param name="deltaSeconds">Real frame time, already limited by the service.</param>
    /// <remarks>
    /// Gum gives no <c>Push</c> to a disabled control, but still puts it under the cursor (<c>VisualOver</c>), so the
    /// press is found here: one flag read per frame without a press. ADR: integration/sound — Events.
    /// </remarks>
    internal void Update(float deltaSeconds)
    {
        _seconds += deltaSeconds;
        ICursor cursor = FrameworkElement.MainCursor;
        if (cursor.PrimaryPush)
        {
            CheckDenied(cursor.VisualOver);
        }
    }

    /// <remarks>
    /// Only controls that answer the mouse with a hover state sound on hover: a label or a panel under the mouse is
    /// not something the player points at. ADR: integration/sound — Events.
    /// </remarks>
    private static bool HasHover(FrameworkElement control) =>
        control is ButtonBase or ComboBox or Slider or TextBoxBase;

    private static bool IsPartsOnly(FrameworkElement control) =>
        control is ButtonBase or RangeBase or ComboBox or TextBoxBase or ListBox or ListBoxItem or MenuItem;

    private bool IsMuted(GraphicalUiElement element)
    {
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (_muted.TryGetValue(current, out _))
            {
                return true;
            }
        }

        return false;
    }

    private void AttachTree(GraphicalUiElement element)
    {
        if (element is InteractiveGue { FormsControlAsObject: FrameworkElement control }
            && ReferenceEquals(control.Visual, element))
        {
            AttachControl(control);
            if (IsPartsOnly(control))
            {
                return;
            }

            if (control is ScrollViewer viewer)
            {
                AttachChildren(viewer.InnerPanel);
                return;
            }
        }

        AttachChildren(element);
    }

    private void AttachChildren(GraphicalUiElement element)
    {
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            AttachTree(children[i]);
        }
    }

    private void AttachControl(FrameworkElement control)
    {
        if (control is ListBoxItem || !_attached.TryAdd(control, _mark))
        {
            return;
        }

        InteractiveGue visual = control.Visual;
        if (HasHover(control))
        {
            visual.RollOn += (_, _) => HandleRollOn(control);
        }

        control.GotFocus += (_, _) => HandleGotFocus(control);
        switch (control)
        {
            case ButtonBase button:
                button.Push += (_, _) => Raise(UiSoundKind.Press, visual);
                UiSoundKind click = button is ToggleButton ? UiSoundKind.Toggle : UiSoundKind.Click;
                button.Click += (_, _) => Raise(click, visual);
                break;
            case Slider slider:
                slider.ValueChangedByUi += (_, _) => HandleValueStep(visual);
                break;
            case ComboBox comboBox:
                comboBox.SelectionChanged += (_, _) => HandleSelection(comboBox, comboBox.ListBox);
                break;
            case ListBox list:
                list.SelectionChanged += (_, _) => HandleSelection(list, popup: null);
                break;
            case IVirtualList virtualList:
                virtualList.SelectedByUser += (_, _) => Raise(UiSoundKind.Select, visual);
                break;
        }
    }

    private void HandleRollOn(FrameworkElement control)
    {
        if (FrameworkElement.MainCursor.LastInputDevice != InputDevice.TouchScreen
            && control.Visual.IsEnabledRecursively)
        {
            Raise(UiSoundKind.Hover, control.Visual);
        }
    }

    private void HandleGotFocus(FrameworkElement control)
    {
        if (_molib.Input.LastDevice == InputDeviceKind.Navigation)
        {
            Raise(UiSoundKind.Focus, control.Visual);
        }
    }

    private void HandleValueStep(GraphicalUiElement visual)
    {
        if (_seconds - _lastValueStep >= ValueStepInterval)
        {
            _lastValueStep = _seconds;
            Raise(UiSoundKind.ValueStep, visual);
        }
    }

    /// <remarks>
    /// The drop-down of a combo box lies in <c>PopupRoot</c>, outside the visual of the combo box, so it is checked
    /// on its own. ADR: integration/sound — Silence for programmatic changes.
    /// </remarks>
    private void HandleSelection(FrameworkElement list, ListBox? popup)
    {
        ICursor cursor = FrameworkElement.MainCursor;
        bool isPointer = (cursor.PrimaryPush || cursor.PrimaryClick)
            && (IsOn(cursor.WindowPushed, list, popup) || IsOn(cursor.VisualOver, list, popup));
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        bool hasFocus = ReferenceEquals(receiver, list) || (popup is not null && ReferenceEquals(receiver, popup));
        if (isPointer || (hasFocus && Input.IsNavigationActive()))
        {
            Raise(UiSoundKind.Select, list.Visual);
        }

        static bool IsOn(GraphicalUiElement? target, FrameworkElement list, ListBox? popup) =>
            VisualTree.IsInside(target, list.Visual)
                || (popup is not null && VisualTree.IsInside(target, popup.Visual));
    }

    private void CheckDenied(GraphicalUiElement? over)
    {
        for (GraphicalUiElement? current = over; current is not null; current = current.Parent)
        {
            if (current is not InteractiveGue { FormsControlAsObject: FrameworkElement control } visual
                || !ReferenceEquals(control.Visual, current))
            {
                continue;
            }

            if (!visual.IsEnabledRecursively && _attached.TryGetValue(control, out _))
            {
                Raise(UiSoundKind.Denied, visual);
            }

            return;
        }
    }
}
