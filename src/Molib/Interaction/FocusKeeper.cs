using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Gum;
using Gum.Forms.Controls;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// Keeps the focus where the player expects it: hides it while the pointer is used and brings it back where it was,
/// gives a screen and a layer their initial focus, and returns it after a layer closes.
/// </summary>
/// <remarks>
/// Gum keeps the focus on an element that left the tree or lies under a modal window, so every element that gets the
/// focus back is checked first: enabled, visible with its ancestors, under one of the Gum roots and inside the top
/// modal window. ADR: interaction/focus-and-gamepad — Focus visibility, Initial focus of a screen;
/// interaction/layers — Focus.
/// </remarks>
/// <param name="gum">The Gum service, for its roots.</param>
/// <param name="input">The input module, for the last device.</param>
internal sealed class FocusKeeper(GumService gum, Input input)
{
    private FrameworkElement? _remembered;
    private GraphicalUiElement? _rememberedScope;
    private GraphicalUiElement? _screen;
    private FrameworkElement? _screenFocus;

    /// <summary>
    /// The element that has the focus or, while the focus is hidden, the element that gets it back.
    /// </summary>
    public FrameworkElement? Target => InteractiveGue.CurrentInputReceiver as FrameworkElement ?? _remembered;

    private bool IsCursorLast => input.LastDevice == InputDeviceKind.Cursor;

    /// <summary>
    /// The visual of an element in focus.
    /// </summary>
    /// <param name="receiver">The element in focus.</param>
    /// <returns>The visual of a Forms control, the element itself for a visual, or <see langword="null"/>.</returns>
    public static GraphicalUiElement? VisualOf(IInputReceiver? receiver) => receiver switch
    {
        FrameworkElement element => element.Visual,
        GraphicalUiElement visual => visual,
        _ => null,
    };

    /// <summary>
    /// Sets the initial focus of a screen, forgets the remembered focus of the previous one and, with a gamepad or the
    /// keyboard as the last device and no modal window open, moves the focus at once.
    /// </summary>
    /// <param name="screen">Root of the screen.</param>
    /// <param name="initialFocus">Element that gets the focus when the screen opens.</param>
    public void FocusScreen(GraphicalUiElement screen, FrameworkElement? initialFocus)
    {
        _screen = screen;
        _screenFocus = initialFocus;
        _remembered = null;
        _rememberedScope = null;
        if (!IsCursorLast && TopModal() is null)
        {
            Restore();
        }
    }

    /// <summary>
    /// The last visible child of <c>ModalRoot</c>: the only element Gum gives input to while it exists.
    /// </summary>
    /// <returns>The top modal element, or <see langword="null"/>.</returns>
    public GraphicalUiElement? TopModal()
    {
        ObservableCollection<GraphicalUiElement> children = gum.ModalRoot.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (children[i].Visible)
            {
                return children[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="receiver"/> may keep the focus.
    /// </summary>
    /// <param name="receiver">The element in focus.</param>
    /// <returns><see langword="false"/> for none, and for a Forms control that cannot take the focus now.</returns>
    public bool IsValid(IInputReceiver? receiver) => receiver switch
    {
        null => false,
        FrameworkElement element => CanTakeFocus(element, TopModal()),
        _ => true,
    };

    /// <summary>
    /// Takes the focus away and remembers it while the pointer is used.
    /// </summary>
    /// <remarks>
    /// Elements that work with the keyboard themselves keep the focus: a text field needs it for typing.
    /// ADR: interaction/focus-and-gamepad — Focus visibility.
    /// </remarks>
    public void Hide()
    {
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        if (receiver is null || NavigationInput.UsesKeyboardItself(receiver))
        {
            return;
        }

        _remembered = receiver as FrameworkElement;
        _rememberedScope = null;
        InteractiveGue.CurrentInputReceiver = null;
    }

    /// <summary>
    /// Brings the focus back with a navigation press.
    /// </summary>
    /// <remarks>
    /// The remembered element first, then the remembered scope of a layer, then the initial focus of the screen,
    /// then the first focusable element of the top modal window, the screen or the root.
    /// ADR: interaction/focus-and-gamepad — Focus visibility.
    /// </remarks>
    public void Restore()
    {
        GraphicalUiElement? modal = TopModal();
        FrameworkElement? remembered = _remembered;
        GraphicalUiElement? rememberedScope = _rememberedScope;
        _remembered = null;
        _rememberedScope = null;
        if (CanTakeFocus(remembered, modal))
        {
            remembered.IsFocused = true;
            return;
        }

        InteractiveGue.CurrentInputReceiver = null;
        if (rememberedScope is not null && IsShown(rememberedScope, modal))
        {
            FocusFirst(rememberedScope);
        }
        else if (modal is null && CanTakeFocus(_screenFocus, null))
        {
            _screenFocus.IsFocused = true;
        }
        else if (modal is not null)
        {
            FocusFirst(modal);
        }
        else
        {
            FocusFirst(_screen is not null && IsShown(_screen, null) ? _screen : gum.Root);
        }
    }

    /// <summary>
    /// Moves the focus into a new layer: to <paramref name="target"/> if it can take it, otherwise to the first
    /// focusable element of <paramref name="scope"/>. With the pointer as the last device, only remembers it.
    /// </summary>
    /// <param name="target">The initial focus of the layer.</param>
    /// <param name="scope">Root of the layer.</param>
    /// <remarks>
    /// A focus left outside the layer is taken away in any case: Gum sends the keyboard to the element in focus
    /// even when it lies under a modal window. ADR: interaction/layers — Focus.
    /// </remarks>
    public void FocusLayer(FrameworkElement? target, GraphicalUiElement scope)
    {
        if (InteractiveGue.CurrentInputReceiver is { } receiver && !VisualTree.IsInside(VisualOf(receiver), scope))
        {
            InteractiveGue.CurrentInputReceiver = null;
        }

        FrameworkElement? inside = target is not null && VisualTree.IsInside(target.Visual, scope) ? target : null;
        if (IsCursorLast)
        {
            _remembered = inside;
            _rememberedScope = scope;
            return;
        }

        if (CanTakeFocus(inside, TopModal()))
        {
            inside.IsFocused = true;
        }
        else
        {
            FocusFirst(scope);
        }
    }

    /// <summary>
    /// Returns the focus after a layer closed: to <paramref name="target"/> if it can take it, otherwise as the
    /// first navigation press would. With the pointer as the last device, only remembers it.
    /// </summary>
    /// <param name="target">The element that had the focus when the layer opened.</param>
    /// <remarks>An element that has a valid focus already keeps it. ADR: interaction/layers — Focus.</remarks>
    public void ReturnFocus(FrameworkElement? target)
    {
        _remembered = target;
        _rememberedScope = null;
        if (!IsCursorLast && !IsValid(InteractiveGue.CurrentInputReceiver))
        {
            Restore();
        }
    }

    /// <summary>
    /// Takes the focus away from anything inside <paramref name="root"/> and forgets a remembered focus there.
    /// </summary>
    /// <param name="root">Root of a layer that starts closing.</param>
    public void ClearInside(GraphicalUiElement root)
    {
        if (InteractiveGue.CurrentInputReceiver is { } receiver && VisualTree.IsInside(VisualOf(receiver), root))
        {
            InteractiveGue.CurrentInputReceiver = null;
        }

        if (_remembered is not null && VisualTree.IsInside(_remembered.Visual, root))
        {
            _remembered = null;
        }

        if (_rememberedScope is not null && VisualTree.IsInside(_rememberedScope, root))
        {
            _rememberedScope = null;
        }
    }

    /// <summary>
    /// Takes the focus away from a text field and remembers it, so the next navigation press brings the focus back
    /// to it.
    /// </summary>
    /// <param name="field">The text field in focus.</param>
    /// <remarks>ADR: platform/text-input — What molib adds.</remarks>
    public void UnfocusTextField(TextBoxBase field)
    {
        field.IsFocused = false;
        _remembered = field;
        _rememberedScope = null;
    }

    /// <summary>
    /// Forgets the screen and the remembered focus.
    /// </summary>
    public void Clear()
    {
        _screen = null;
        _screenFocus = null;
        _remembered = null;
        _rememberedScope = null;
    }

    private static void FocusFirst(GraphicalUiElement scope)
    {
        if (scope is InteractiveGue container)
        {
            FrameworkElement.HandleTab(TabDirection.Down, null, container, shouldAskParent: false, loop: false);
        }
    }

    /// <remarks>
    /// Gum keeps the focus on an element removed from the tree, so being under one of the roots is checked here.
    /// ADR: interaction/focus-and-gamepad — What Gum gives.
    /// </remarks>
    private bool CanTakeFocus(
        [NotNullWhen(true)] FrameworkElement? element,
        GraphicalUiElement? modal) =>
        element is not null && (element.IsEnabled || element.AllowFocusWhenDisabled) && IsShown(element.Visual, modal);

    /// <summary>
    /// Whether <paramref name="element"/> is visible with all its ancestors, under one of the Gum roots, and inside
    /// <paramref name="modal"/> when there is one.
    /// </summary>
    private bool IsShown(GraphicalUiElement element, GraphicalUiElement? modal)
    {
        bool isInsideModal = modal is null;
        GraphicalUiElement top = element;
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (!current.Visible)
            {
                return false;
            }

            isInsideModal |= ReferenceEquals(current, modal);
            top = current;
        }

        return isInsideModal
            && (ReferenceEquals(top, gum.Root) || ReferenceEquals(top, gum.ModalRoot)
                || ReferenceEquals(top, gum.PopupRoot));
    }
}
