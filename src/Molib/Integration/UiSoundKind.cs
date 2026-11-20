namespace Molib.Integration;

/// <summary>
/// What the player did in the interface, for the game to pick a sound.
/// </summary>
/// <remarks>ADR: integration/sound — Events.</remarks>
public enum UiSoundKind
{
    /// <summary>The mouse moved onto an enabled control; never from touch.</summary>
    Hover,

    /// <summary>A button, check box or radio button was pressed: mouse, touch, Enter, Space or A.</summary>
    Press,

    /// <summary>A button was activated: mouse, touch, Enter, Space or A.</summary>
    Click,

    /// <summary>The player switched a toggle button, check box or radio button.</summary>
    Toggle,

    /// <summary>The player moved a slider; at most once per 50 ms while dragging.</summary>
    ValueStep,

    /// <summary>The player selected a row of a list box, a combo box or a virtual list.</summary>
    Select,

    /// <summary>The focus moved with the keyboard or a gamepad.</summary>
    Focus,

    /// <summary>The player pressed a disabled control.</summary>
    Denied,

    /// <summary>A modal or popup layer opened.</summary>
    LayerOpened,

    /// <summary>A modal or popup layer started to close.</summary>
    LayerClosed,

    /// <summary>A toast appeared.</summary>
    ToastShown,

    /// <summary>A drag started.</summary>
    DragStarted,

    /// <summary>A drag ended on a target that took it.</summary>
    DragDropped,

    /// <summary>A drag was cancelled.</summary>
    DragCancelled,

    /// <summary>A snapping scroll viewer came to rest on another item or page.</summary>
    SnapChanged,
}
