using System.Diagnostics;
using Gum.DataTypes.Variables;
using Gum.Forms.Controls;
using Molib.Motion;

namespace Molib.Visuals;

/// <summary>
/// Gum's V3 button visual whose state changes animate through molib: hover, press, focus and disabling.
/// </summary>
/// <remarks>
/// <para>
/// The base constructor runs with <c>tryCreateFormsObject: false</c>, and the <see cref="Button"/> control is
/// created here once the transitions exist, as Gum's own <c>ThumbVisual</c> does: otherwise the first
/// <c>UpdateState</c> of the control would bypass them. Register it in <c>FrameworkElement.DefaultFormsTemplates</c>
/// to give every <see cref="Button"/> of the game this visual.
/// </para>
/// <para>ADR: motion/state-transitions — molib visuals.</para>
/// </remarks>
public sealed class ButtonVisual : Gum.Forms.DefaultVisuals.V3.ButtonVisual
{
    private readonly Action<StateSave> _applyBaseState;
    private readonly StateTransitions? _transitions;

    /// <summary>
    /// Creates the visual and, unless told otherwise, its <see cref="Button"/> control.
    /// </summary>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="fullInstantiation">Passed to the Gum visual.</param>
    /// <param name="tryCreateFormsObject">Whether to create the <see cref="Button"/> control; Gum passes
    /// <see langword="false"/> when it creates the control around the visual itself.</param>
    public ButtonVisual(MolibService molib, bool fullInstantiation = true, bool tryCreateFormsObject = true)
        : base(fullInstantiation, tryCreateFormsObject: false)
    {
        ArgumentNullException.ThrowIfNull(molib);
        _applyBaseState = base.ApplyState;
        _transitions = molib.Transitions.Create(this);
        if (tryCreateFormsObject)
        {
            FormsControlAsObject = new Button(this);
        }
    }

    /// <summary>
    /// Transitions of this visual, to configure with <see cref="StateTransitions.For(string, MotionPreset)"/>.
    /// </summary>
    public StateTransitions Transitions
    {
        get
        {
            Debug.Assert(_transitions is not null, "The constructor creates the transitions.");
            return _transitions;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// While the base constructor runs, the transitions do not exist yet; the visual is not on screen then, so the
    /// state is applied at once, as it would be anyway.
    /// </remarks>
    public override void ApplyState(StateSave state)
    {
        if (_transitions is null)
        {
            base.ApplyState(state);
            return;
        }

        _transitions.Apply(state, _applyBaseState);
    }
}
