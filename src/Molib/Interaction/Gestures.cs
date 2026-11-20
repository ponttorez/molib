using System.Runtime.CompilerServices;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// Runs the long presses that are held now.
/// </summary>
/// <remarks>
/// Only held presses are in the list, so a quiet frame returns at once. Elements are registered in a weak table, so
/// a long press does not keep its element alive. ADR: interaction/gestures — Long press.
/// </remarks>
internal sealed class Gestures
{
    private readonly ConditionalWeakTable<InteractiveGue, LongPress> _longPresses = [];
    private readonly List<LongPress> _held = [];

    /// <summary>
    /// Gives <paramref name="element"/> a long press.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <param name="options">How long the press has to be held.</param>
    /// <returns>The long press of the element.</returns>
    /// <exception cref="InvalidOperationException">The element already has a long press.</exception>
    public LongPress Attach(InteractiveGue element, LongPressOptions options)
    {
        if (_longPresses.TryGetValue(element, out _))
        {
            throw new InvalidOperationException("This element already has a long press.");
        }

        var longPress = new LongPress(this, element, options);
        _longPresses.Add(element, longPress);
        return longPress;
    }

    /// <summary>
    /// Puts a pressed <paramref name="longPress"/> into the list of held ones.
    /// </summary>
    /// <param name="longPress">A long press of this module.</param>
    public void Activate(LongPress longPress)
    {
        if (!longPress.IsListed)
        {
            longPress.IsListed = true;
            _held.Add(longPress);
        }
    }

    /// <summary>
    /// Counts the hold time of every held press.
    /// </summary>
    /// <param name="deltaSeconds">Frame time, already limited by the service; real time, not scaled.</param>
    public void Update(float deltaSeconds)
    {
        for (int i = 0; i < _held.Count; i++)
        {
            LongPress longPress = _held[i];
            if (!longPress.Update(deltaSeconds))
            {
                longPress.IsListed = false;
                _held.RemoveAt(i);
                i--;
            }
        }
    }
}
