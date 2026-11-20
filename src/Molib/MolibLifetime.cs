namespace Molib;

/// <summary>
/// Whether the <see cref="MolibService"/> that owns a module is disposed; shared by all its modules.
/// </summary>
/// <remarks>
/// After <see cref="MolibService.Dispose"/> methods that add or start something throw
/// <see cref="ObjectDisposedException"/>, while methods that remove, stop or undo something do nothing, so cleanup
/// code may run in any order. ADR: foundation/architecture — Disposal.
/// </remarks>
internal sealed class MolibLifetime
{
    /// <summary>
    /// Whether the service is disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Marks the service disposed.
    /// </summary>
    public void End() => IsDisposed = true;

    /// <summary>
    /// Throws if the service is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, typeof(MolibService));
}
