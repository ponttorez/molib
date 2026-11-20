namespace Molib.Interaction;

/// <summary>
/// The paging step passed to <see cref="Input.PageUnhandled"/>.
/// </summary>
public sealed class PageEventArgs : EventArgs
{
    internal PageEventArgs(int step)
    {
        Step = step;
    }

    /// <summary>
    /// <c>-1</c> for the previous tab or page (LB, Ctrl+PageUp), <c>+1</c> for the next one (RB, Ctrl+PageDown).
    /// </summary>
    public int Step { get; }
}
