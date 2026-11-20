using System.Diagnostics;
using Gum.DataTypes.Variables;
using Gum.Forms.Controls;
using Molib.Motion;

namespace Molib.Visuals;

/// <summary>
/// Gum's V3 list box item visual whose state changes animate through molib: hover, selection, focus and disabling.
/// </summary>
/// <remarks>
/// <para>
/// The base constructor runs with <c>tryCreateFormsObject: false</c>, and the <see cref="ListBoxItem"/> control is
/// created here once the transitions exist, as Gum's own <c>ThumbVisual</c> does: otherwise the first
/// <c>UpdateState</c> of the control would bypass them. Register it in <c>FrameworkElement.DefaultFormsTemplates</c>
/// to give every <see cref="ListBoxItem"/> of the game this visual.
/// </para>
/// <para>
/// The Gum visual writes its placeholder text through the translated <c>Text</c>, so Gum remembers it as a string
/// key, while the list writes the item text past translation; a language switch would then put the placeholder back
/// into every row. The constructor keeps the placeholder as plain text. ADR: integration/localization — What Gum does.
/// </para>
/// <para>ADR: motion/state-transitions — molib visuals.</para>
/// </remarks>
public sealed class ListBoxItemVisual : Gum.Forms.DefaultVisuals.V3.ListBoxItemVisual
{
    private readonly Action<StateSave> _applyBaseState;
    private readonly StateTransitions? _transitions;

    /// <summary>
    /// Creates the visual and, unless told otherwise, its <see cref="ListBoxItem"/> control.
    /// </summary>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="fullInstantiation">Passed to the Gum visual.</param>
    /// <param name="tryCreateFormsObject">Whether to create the <see cref="ListBoxItem"/> control; Gum passes
    /// <see langword="false"/> when it creates the control around the visual itself.</param>
    public ListBoxItemVisual(MolibService molib, bool fullInstantiation = true, bool tryCreateFormsObject = true)
        : base(fullInstantiation, tryCreateFormsObject: false)
    {
        ArgumentNullException.ThrowIfNull(molib);
        _applyBaseState = base.ApplyState;
        _transitions = molib.Transitions.Create(this);
        TextInstance.SetTextNoTranslate(TextInstance.Text);
        if (tryCreateFormsObject)
        {
            FormsControlAsObject = new ListBoxItem(this);
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
