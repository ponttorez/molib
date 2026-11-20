namespace Molib.Widgets;

/// <summary>
/// Where a toast is in its life.
/// </summary>
internal enum ToastState
{
    /// <summary>Waits in the queue of its placement.</summary>
    Waiting,

    /// <summary>Appears.</summary>
    Showing,

    /// <summary>Fully shown; its time runs.</summary>
    Shown,

    /// <summary>Disappears; keeps its place in the stack until it is gone.</summary>
    Hiding,

    /// <summary>Gone or dismissed before it was shown.</summary>
    Closed,
}
