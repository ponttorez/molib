using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Gum;
using Gum.DataTypes.Variables;
using Gum.Wireframe;

namespace Molib.Motion;

/// <summary>
/// Animates the state changes of one visual: called from its <c>ApplyState</c> override, it lets Gum apply the
/// state, then moves what changed from the old values to the new ones.
/// </summary>
/// <remarks>
/// <para>
/// States come in two kinds in Gum: with variables (from the Gum editor) and with an <c>Apply</c> delegate (the V3
/// visuals built in code). Comparing the values before and after works for both, while
/// <c>InterpolateBetween</c> works only for the first kind. ADR: motion/state-transitions — Decision.
/// </para>
/// <para>
/// One visual runs one transition: a new state of any of its categories rebuilds the tracks from the current
/// values. ADR: motion/state-transitions — A state change during a transition.
/// </para>
/// </remarks>
public sealed class StateTransitions : IMotionTarget
{
    private const int InitialCapacity = 8;

    private readonly Transitions _owner;
    private readonly GraphicalUiElement _visual;
    private readonly MotionProgress _progress;
    private readonly List<Rule> _rules = [];
    private readonly List<GraphicalUiElement> _elements = [];
    private readonly List<Track> _tracks = [];
    private AnimatedValues[] _before = new AnimatedValues[InitialCapacity];
    private AnimatedProperties _animated;
    private bool _isApplying;

    internal StateTransitions(Transitions owner, GraphicalUiElement visual)
    {
        _owner = owner;
        _visual = visual;
        _progress = new MotionProgress(this);
    }

    /// <inheritdoc/>
    bool IMotionTarget.ShouldFinishNow => false;

    /// <summary>
    /// Sets the preset of every state of <paramref name="category"/> that has no rule of its own.
    /// </summary>
    /// <param name="category">Name of a state category of the visual.</param>
    /// <param name="preset">How its states animate; <see cref="MotionPreset.Instant"/> turns them off.</param>
    /// <returns>These transitions, for chaining.</returns>
    /// <remarks>
    /// A category the visual does not have yet is reported as <c>transitions.unknownCategory</c>: add categories
    /// before configuring transitions. ADR: motion/state-transitions — Configuration.
    /// </remarks>
    public StateTransitions For(string category, MotionPreset preset) => SetRule(category, null, preset);

    /// <summary>
    /// Sets the preset of the transition into the state <paramref name="to"/> of <paramref name="category"/>.
    /// </summary>
    /// <param name="category">Name of a state category of the visual.</param>
    /// <param name="to">Name of the target state.</param>
    /// <param name="preset">How the transition into it animates; <see cref="MotionPreset.Instant"/> turns it off.
    /// </param>
    /// <returns>These transitions, for chaining.</returns>
    /// <remarks>
    /// The most specific rule wins: category and state, then category, then <see cref="Transitions.Default"/>.
    /// </remarks>
    public StateTransitions For(string category, string to, MotionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(to);
        return SetRule(category, to, preset);
    }

    /// <summary>
    /// Applies <paramref name="state"/> through <paramref name="applyBase"/> and animates what it changed.
    /// </summary>
    /// <param name="state">The state Gum passed to <c>ApplyState</c>.</param>
    /// <param name="applyBase">The base <c>ApplyState</c> of the visual, usually <c>base.ApplyState</c>.</param>
    /// <remarks>
    /// <para>
    /// The state is applied at once, finishing the running transition first, when it belongs to no category of the
    /// visual (a frame of a Gum animation, a state without a category), when its preset is instant, or when nobody
    /// saw the old values: the visual is not on screen, or molib applies an orientation without animation.
    /// </para>
    /// <para>
    /// Otherwise molib records the animatable values of the visual and its descendants, lets Gum apply the state,
    /// puts the changed values back and moves them to the new ones. Layout is suspended meanwhile, so Gum lays the
    /// visual out once. ADR: motion/state-transitions — How a transition runs.
    /// </para>
    /// </remarks>
    public void Apply(StateSave state, Action<StateSave> applyBase)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(applyBase);
        if (_isApplying)
        {
            applyBase(state);
            return;
        }

        string? category = FindCategory(state);
        MotionPreset preset = category is null ? MotionPreset.Instant : ChoosePreset(category, state.Name);
        if (preset.Kind == MotionKind.Instant || _owner.Motion.IsInstant || !IsOnScreen())
        {
            FinishTracks();
            _owner.Motion.Stop(_progress);
            applyBase(state);
            return;
        }

        _isApplying = true;
        bool suspends = !_visual.IsLayoutSuspended && !GraphicalUiElement.IsAllLayoutSuspended;
        if (suspends)
        {
            _visual.SuspendLayout(true);
        }

        try
        {
            BuildTracks(state, applyBase);
        }
        finally
        {
            _isApplying = false;
            if (suspends)
            {
                _visual.ResumeLayout(true);
            }
        }

        if (_tracks.Count == 0)
        {
            _owner.Motion.Stop(_progress);
        }
        else
        {
            _owner.Motion.Start(_progress, preset);
        }
    }

    /// <summary>
    /// Finishes the running transition at once: every value it moves gets its target value.
    /// </summary>
    /// <remarks>
    /// Call it before the visual changes its look outside its states, such as when a theme repaints it with a new
    /// palette: the running transition would keep moving its values to targets taken from the old look and paint
    /// over the new one. ADR: motion/state-transitions — A look change outside states.
    /// </remarks>
    public void Finish()
    {
        FinishTracks();
        _owner.Motion.Stop(_progress);
    }

    /// <inheritdoc/>
    void IMotionTarget.ApplyProgress(float progress)
    {
        bool suspends = (_animated & AnimatedProperties.Layout) != 0
            && !_visual.IsLayoutSuspended
            && !GraphicalUiElement.IsAllLayoutSuspended;
        if (suspends)
        {
            _visual.SuspendLayout(true);
        }

        Span<Track> tracks = CollectionsMarshal.AsSpan(_tracks);
        for (int i = 0; i < tracks.Length; i++)
        {
            ref Track track = ref tracks[i];
            AnimatedValues.Write(track.Element, track.Changed, track.From, track.To, progress);
        }

        if (suspends)
        {
            _visual.ResumeLayout(true);
        }
    }

    /// <inheritdoc/>
    void IMotionTarget.CompleteMotion() => FinishTracks();

    private static void Collect(GraphicalUiElement element, List<GraphicalUiElement> elements)
    {
        elements.Add(element);
        ObservableCollection<GraphicalUiElement> children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            Collect(children[i], elements);
        }
    }

    private StateTransitions SetRule(string category, string? to, MotionPreset preset)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(preset);
        if (!_visual.Categories.ContainsKey(category))
        {
            string visualName = string.IsNullOrEmpty(_visual.Name) ? _visual.GetType().Name : _visual.Name;
            _owner.Diagnostics.WarnOnce(
                "transitions.unknownCategory",
                category,
                $"The visual '{visualName}' has no state category '{category}'. Add categories before calling "
                    + "StateTransitions.For.");
        }

        var rule = new Rule(category, to, preset);
        for (int i = 0; i < _rules.Count; i++)
        {
            if (_rules[i].Category == category && _rules[i].State == to)
            {
                _rules[i] = rule;
                return this;
            }
        }

        _rules.Add(rule);
        return this;
    }

    private MotionPreset ChoosePreset(string category, string? state)
    {
        MotionPreset? categoryPreset = null;
        for (int i = 0; i < _rules.Count; i++)
        {
            Rule rule = _rules[i];
            if (rule.Category != category)
            {
                continue;
            }

            if (rule.State is null)
            {
                categoryPreset = rule.Preset;
            }
            else if (rule.State == state)
            {
                return rule.Preset;
            }
        }

        return categoryPreset ?? _owner.Default;
    }

    /// <remarks>
    /// States are compared by reference: Forms and <c>SetProperty</c> pass the instance from the category itself.
    /// A frame of a Gum animation is a new state every frame and belongs to no category.
    /// </remarks>
    private string? FindCategory(StateSave state)
    {
        foreach (KeyValuePair<string, StateSaveCategory> pair in _visual.Categories)
        {
            List<StateSave> states = pair.Value.States;
            for (int i = 0; i < states.Count; i++)
            {
                if (ReferenceEquals(states[i], state))
                {
                    return pair.Key;
                }
            }
        }

        return null;
    }

    /// <remarks>
    /// On screen means visible together with every ancestor, under one of the Gum roots. An element that is being
    /// built or was removed has nothing to animate from. ADR: motion/state-transitions — How a transition runs.
    /// </remarks>
    private bool IsOnScreen()
    {
        GraphicalUiElement top = _visual;
        for (GraphicalUiElement? element = _visual; element is not null; element = element.Parent)
        {
            if (!element.Visible)
            {
                return false;
            }

            top = element;
        }

        GumService gum = _owner.Gum;
        return ReferenceEquals(top, gum.Root) || ReferenceEquals(top, gum.ModalRoot)
            || ReferenceEquals(top, gum.PopupRoot);
    }

    /// <remarks>
    /// The values before are taken first, so a running transition starts over from where it is now. Its tracks are
    /// then finished, so the properties the new state does not touch keep going to their old target instead of
    /// freezing halfway.
    /// </remarks>
    private void BuildTracks(StateSave state, Action<StateSave> applyBase)
    {
        _elements.Clear();
        Collect(_visual, _elements);
        if (_before.Length < _elements.Count)
        {
            _before = new AnimatedValues[Math.Max(_elements.Count, _before.Length * 2)];
        }

        for (int i = 0; i < _elements.Count; i++)
        {
            _before[i] = AnimatedValues.Capture(_elements[i]);
        }

        FinishTracks();
        applyBase(state);

        for (int i = 0; i < _elements.Count; i++)
        {
            GraphicalUiElement element = _elements[i];
            var after = AnimatedValues.Capture(element);
            AnimatedProperties changed = _before[i].ChangesTo(after);
            if (changed != AnimatedProperties.None)
            {
                _before[i].WriteTo(element, changed);
                _tracks.Add(new Track(element, changed, _before[i], after));
                _animated |= changed;
            }
        }

        _elements.Clear();
    }

    private void FinishTracks()
    {
        if (_tracks.Count == 0)
        {
            return;
        }

        Span<Track> tracks = CollectionsMarshal.AsSpan(_tracks);
        for (int i = 0; i < tracks.Length; i++)
        {
            ref Track track = ref tracks[i];
            track.To.WriteTo(track.Element, track.Changed);
        }

        _tracks.Clear();
        _animated = AnimatedProperties.None;
    }

    /// <summary>
    /// A preset for a category, or for one target state of it when <see cref="State"/> is set.
    /// </summary>
    private readonly record struct Rule(string Category, string? State, MotionPreset Preset);

    /// <summary>
    /// The values of one element that a transition moves.
    /// </summary>
    private readonly record struct Track(
        GraphicalUiElement Element,
        AnimatedProperties Changed,
        AnimatedValues From,
        AnimatedValues To);
}
