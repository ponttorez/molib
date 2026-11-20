using Gum;
using Gum.Wireframe;

namespace Molib.Motion;

/// <summary>
/// Smooth transitions between Gum states: creates the <see cref="StateTransitions"/> of each visual and holds the
/// preset they use by default.
/// </summary>
/// <remarks>
/// Gum applies states at once. A visual that routes its <c>ApplyState</c> through <see cref="StateTransitions"/>
/// animates whatever the state changed from the old values to the new ones. The visuals in <c>Molib.Visuals</c> do
/// this for the standard Gum V3 visuals. ADR: motion/state-transitions — Decision.
/// </remarks>
public sealed class Transitions
{
    internal Transitions(GumService gum, MotionCore motion, MolibDiagnostics diagnostics)
    {
        Gum = gum;
        Motion = motion;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Preset of every category and state without its own rule; <see cref="MotionPreset.Snappy"/> by default.
    /// </summary>
    /// <remarks>
    /// Read at every state change, so a new value applies to all visuals. <see cref="MotionPreset.Instant"/> turns
    /// transitions off where no rule says otherwise.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public MotionPreset Default
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = MotionPreset.Snappy;

    /// <summary>
    /// The Gum service, for the roots a visual must be under to be on screen.
    /// </summary>
    internal GumService Gum { get; }

    /// <summary>
    /// The motion core that moves the transitions.
    /// </summary>
    internal MotionCore Motion { get; }

    /// <summary>
    /// Where configuration problems are reported.
    /// </summary>
    internal MolibDiagnostics Diagnostics { get; }

    /// <summary>
    /// Creates the transitions of <paramref name="visual"/>; call <see cref="StateTransitions.Apply"/> from its
    /// <c>ApplyState</c> override.
    /// </summary>
    /// <param name="visual">The visual whose states should animate.</param>
    /// <returns>Transitions bound to <paramref name="visual"/>, configured further with
    /// <see cref="StateTransitions.For(string, MotionPreset)"/>.</returns>
    /// <remarks>ADR: motion/state-transitions — The intercept point.</remarks>
    public StateTransitions Create(GraphicalUiElement visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return new StateTransitions(this, visual);
    }
}
