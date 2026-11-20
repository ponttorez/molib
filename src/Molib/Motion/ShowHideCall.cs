namespace Molib.Motion;

/// <summary>
/// One <c>ShowAsync</c> or <c>HideAsync</c> call: its task and its cancellation.
/// </summary>
/// <remarks>
/// <para>
/// The task completes without <c>RunContinuationsAsynchronously</c>: molib completes it on the game thread inside
/// <c>MolibService.Update</c>, so the code after <c>await</c> runs there too (or is posted to the synchronization
/// context the caller had) and may change the interface at once.
/// </para>
/// <para>
/// A cancellation may come from any thread. The callback only marks the call; the animation reads the mark in the
/// next update and finishes there. ADR: motion/show-hide — API.
/// </para>
/// </remarks>
internal sealed class ShowHideCall
{
    private readonly TaskCompletionSource<bool> _completion = new();
    private readonly CancellationToken _cancellationToken;
    private readonly CancellationTokenRegistration _registration;
    private volatile bool _isCancelRequested;

    /// <summary>
    /// Creates the call and listens to <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="cancellationToken">Token of the caller.</param>
    public ShowHideCall(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        if (cancellationToken.CanBeCanceled)
        {
            _registration = cancellationToken.Register(static state => (state as ShowHideCall)?.MarkCancelled(), this);
        }
    }

    /// <summary>
    /// The task the caller awaits.
    /// </summary>
    public Task<bool> Task => _completion.Task;

    /// <summary>
    /// Whether the caller cancelled the call.
    /// </summary>
    public bool IsCancelRequested => _isCancelRequested;

    /// <summary>
    /// Completes the task: cancelled if the caller asked for it, otherwise with <paramref name="reachedEnd"/>.
    /// </summary>
    /// <param name="reachedEnd">
    /// <see langword="true"/> if the animation ran to its end, <see langword="false"/> if a later call took over.
    /// </param>
    public void Complete(bool reachedEnd)
    {
        _registration.Unregister();
        if (_isCancelRequested)
        {
            _completion.TrySetCanceled(_cancellationToken);
        }
        else
        {
            _completion.TrySetResult(reachedEnd);
        }
    }

    private void MarkCancelled() => _isCancelRequested = true;
}
