namespace Molib.Motion;

/// <summary>
/// What <see cref="GraphicalUiElementExtensions.HideAsync"/> does with the element after its animation.
/// </summary>
public enum HideAction
{
    /// <summary>
    /// Sets <c>Visible</c> to <see langword="false"/>.
    /// </summary>
    Hide,

    /// <summary>
    /// Removes the element from its parent (<c>RemoveFromRoot</c>, that is <c>Parent = null</c>).
    /// </summary>
    Remove,
}
