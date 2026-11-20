using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Gum;
using Gum.Forms.Controls;
using Gum.Input;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Molib.Widgets;
using Keys = Gum.Forms.Input.Keys;

namespace Molib.Interaction;

/// <summary>
/// Gamepads, the last device, hiding and restoring the focus, the initial focus of a screen, arrow keys and paging.
/// </summary>
/// <remarks>
/// <para>
/// Gum already moves the focus with the gamepad and the keyboard and reads all four gamepad slots every frame, so a
/// gamepad connected while the game runs controls the interface at once. This module adds what a gamepad game
/// still lacks: connection events, one gamepad for the menu, the last device, a focus that hides while the player
/// uses the mouse or touch and comes back where it was, arrow keys that move like the D-pad, and LB / RB paging.
/// ADR: interaction/focus-and-gamepad — Decision.
/// </para>
/// <para>
/// It reads the input Gum has already read in this frame from Gum's own lists (<c>GamePadsForUiControl</c>,
/// <c>KeyboardsForUiControl</c>); the game turns them on with <c>UseGamepadDefaults</c> and
/// <c>UseKeyboardDefaults</c>.
/// </para>
/// </remarks>
public sealed class Input
{
    private static readonly GamepadButton[] _navigationButtons =
    [
        GamepadButton.DPadUp, GamepadButton.DPadDown, GamepadButton.DPadLeft, GamepadButton.DPadRight,
        GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y,
        GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftTrigger,
        GamepadButton.RightTrigger, GamepadButton.LeftStick, GamepadButton.RightStick, GamepadButton.Start,
        GamepadButton.LeftThumbstickUp, GamepadButton.LeftThumbstickDown, GamepadButton.LeftThumbstickLeft,
        GamepadButton.LeftThumbstickRight,
    ];

    private static readonly DPadDirection[] _stickDirections =
        [DPadDirection.Up, DPadDirection.Down, DPadDirection.Left, DPadDirection.Right];

    private static readonly Keys[] _editingKeys =
        [Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.Enter, Keys.Space, Keys.PageUp, Keys.PageDown];

    private readonly Game _game;
    private readonly GumService _gum;
    private readonly MolibLifetime _lifetime;
    private bool _isUiGamepadsSet;
    private readonly bool[] _wasConnected = new bool[UiGamepads.SlotCount];
    private readonly ConditionalWeakTable<GraphicalUiElement, Action<int>> _paging = [];
    private bool _hasCursorPosition;
    private int _cursorX;
    private int _cursorY;
    private FrameworkElement? _remembered;
    private GraphicalUiElement? _rememberedScope;
    private GraphicalUiElement? _screen;
    private FrameworkElement? _screenFocus;
    private IInputReceiver? _frameReceiver;
    private bool _frameReceiverMode;
    private bool _areArrowsNavigation;

    internal Input(Game game, GumService gum, MolibLifetime lifetime)
    {
        _game = game;
        _gum = gum;
        _lifetime = lifetime;
        GamePad[] pads = gum.Gamepads;
        for (int i = 0; i < pads.Length && i < _wasConnected.Length; i++)
        {
            _wasConnected[i] = pads[i].IsConnected;
            if (_wasConnected[i])
            {
                LastDevice = InputDeviceKind.Navigation;
            }
        }
    }

    /// <summary>
    /// Which gamepads control the interface; <see cref="UiGamepads.All"/> by default.
    /// </summary>
    /// <remarks>
    /// Setting it rebuilds the Gum gamepads in <c>FrameworkElement.GamePadsForUiControl</c> and keeps any other
    /// <c>IGamePad</c> there. Until it is set, molib leaves the list as <c>UseGamepadDefaults</c> filled it.
    /// ADR: interaction/focus-and-gamepad — Gamepads.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public UiGamepads UiGamepads
    {
        get;
        set
        {
            _lifetime.ThrowIfDisposed();
            field = value;
            _isUiGamepadsSet = true;
            ApplyUiGamepads(value);
        }
    }

    /// <summary>
    /// The kind of device the player used last. At start it is <see cref="InputDeviceKind.Navigation"/> when a
    /// gamepad is connected and <see cref="InputDeviceKind.Cursor"/> otherwise.
    /// </summary>
    /// <remarks>
    /// Cursor: mouse movement, a mouse button, the wheel or a touch. Navigation: any button or stick of a gamepad
    /// that controls the interface except <c>Back</c>, which is the system back key on Android; Tab, the arrows,
    /// Enter, Space, PageUp, PageDown and the pick key of drag and drop. While a text field has the focus, only Tab
    /// counts: the other keys edit text. ADR: interaction/focus-and-gamepad — Last device.
    /// </remarks>
    public InputDeviceKind LastDevice { get; private set; } = InputDeviceKind.Cursor;

    /// <summary>
    /// Slot of the gamepad pressed last, from 0 to 3, or <c>-1</c> before any gamepad was pressed.
    /// </summary>
    public int LastGamepadIndex { get; private set; } = -1;

    /// <summary>
    /// The key of <c>DragDrop.PickKey</c>: a navigation key like the arrows, except while a text field has the
    /// focus.
    /// </summary>
    /// <remarks>ADR: interaction/focus-and-gamepad — Last device.</remarks>
    internal Keys PickKey { get; set; } = Keys.X;

    /// <summary>
    /// The element that has the focus or, while the focus is hidden, the element that gets it back.
    /// </summary>
    internal FrameworkElement? FocusTarget => InteractiveGue.CurrentInputReceiver as FrameworkElement ?? _remembered;

    /// <summary>
    /// Raised when <see cref="LastDevice"/> or <see cref="LastGamepadIndex"/> changed.
    /// </summary>
    public event EventHandler? LastDeviceChanged;

    /// <summary>
    /// Raised when a gamepad in one of the four Gum slots connects.
    /// </summary>
    public event EventHandler<GamepadEventArgs>? GamepadConnected;

    /// <summary>
    /// Raised when a gamepad in one of the four Gum slots disconnects. Pausing the game is up to the game.
    /// </summary>
    public event EventHandler<GamepadEventArgs>? GamepadDisconnected;

    /// <summary>
    /// Raised when LB / RB or Ctrl+PageUp / Ctrl+PageDown found no paging area around the focus.
    /// </summary>
    public event EventHandler<PageEventArgs>? PageUnhandled;

    /// <summary>
    /// Sets the initial focus of a screen and forgets the remembered focus of the previous one.
    /// </summary>
    /// <param name="screen">Root of the screen; it gives the first focusable element when there is no
    /// <paramref name="initialFocus"/>.</param>
    /// <param name="initialFocus">Element that gets the focus when the screen opens and when the focus comes back
    /// with nothing better to return to.</param>
    /// <remarks>
    /// With a gamepad or the keyboard as the last device and no modal window open, the focus moves at once;
    /// otherwise it appears with the first navigation press.
    /// ADR: interaction/focus-and-gamepad — Initial focus of a screen.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void FocusScreen(GraphicalUiElement screen, FrameworkElement? initialFocus = null)
    {
        ArgumentNullException.ThrowIfNull(screen);
        _lifetime.ThrowIfDisposed();
        _screen = screen;
        _screenFocus = initialFocus;
        _remembered = null;
        _rememberedScope = null;
        if (LastDevice == InputDeviceKind.Navigation && TopModal() is null)
        {
            RestoreFocus();
        }
    }

    /// <summary>
    /// Marks <paramref name="area"/> as a paging area: LB / RB and Ctrl+PageUp / Ctrl+PageDown page it while the
    /// focus is inside it.
    /// </summary>
    /// <param name="area">Element that holds the tabs or pages and the controls on them.</param>
    /// <param name="page">Receives <c>-1</c> or <c>+1</c>; <see langword="null"/> removes the mark.</param>
    /// <remarks>
    /// Gum has no tab control, so molib does not look for tabs itself. When areas are nested, the one nearest to
    /// the focus pages. The mark lives in a weak table, so it does not keep a thrown-away area alive.
    /// ADR: interaction/focus-and-gamepad — Paging: LB / RB.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed and <paramref name="page"/> is not
    /// <see langword="null"/>.</exception>
    public void SetPaging(GraphicalUiElement area, Action<int>? page)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (page is null)
        {
            _paging.Remove(area);
        }
        else
        {
            _lifetime.ThrowIfDisposed();
            _paging.AddOrUpdate(area, page);
        }
    }

    /// <summary>
    /// Reads connections, the last device, the focus and paging from the input Gum has read in this frame.
    /// </summary>
    /// <remarks>
    /// While the window is inactive Gum does not read input and the buttons keep their last state, so nothing is
    /// done: one press would otherwise act every frame. ADR: interaction/focus-and-gamepad — What molib adds.
    /// </remarks>
    internal void Update()
    {
        if (!_game.IsActive)
        {
            return;
        }

        UpdateConnections();
        bool isCursorUsed = ReadCursorUse();
        bool isNavigationPressed = ReadNavigationPress(out int gamepadIndex);
        if (isNavigationPressed)
        {
            SetLastDevice(InputDeviceKind.Navigation, gamepadIndex);
        }
        else if (isCursorUsed)
        {
            SetLastDevice(InputDeviceKind.Cursor, LastGamepadIndex);
        }

        if (LastDevice == InputDeviceKind.Cursor)
        {
            HideFocus();
            return;
        }

        if (!isNavigationPressed)
        {
            return;
        }

        if (!IsValid(InteractiveGue.CurrentInputReceiver))
        {
            RestoreFocus();
            return;
        }

        HandlePaging();
    }

    /// <summary>
    /// Remembers the focus for the cancel of the next frame and turns arrow navigation on or off for the next Gum
    /// frame. Runs last in <c>MolibService.Update</c>.
    /// </summary>
    /// <remarks>
    /// Gum handles the input of a frame before molib sees it, so the state before the press is the state at the
    /// end of the previous frame. Arrow navigation uses Gum's direction key lists, which are off while the focus is
    /// on an element that uses the arrows itself: those elements call the common navigation after their own
    /// handling, and an arrow would move the caret or the selection and the focus at once.
    /// ADR: interaction/focus-and-gamepad — Keyboard arrows; interaction/layers — Cancel.
    /// </remarks>
    internal void EndFrame()
    {
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        _frameReceiver = receiver;
        _frameReceiverMode = ModeOf(receiver);
        bool areArrowsNavigation = receiver is not null && !UsesKeyboardItself(receiver);
        if (areArrowsNavigation != _areArrowsNavigation)
        {
            _areArrowsNavigation = areArrowsNavigation;
            SetArrowCombos(areArrowsNavigation);
        }
    }

    /// <summary>
    /// Gives Gum back what this module changed: the direction key lists empty, as Gum has them by default, and all
    /// four Gum gamepads in <c>GamePadsForUiControl</c> if <see cref="UiGamepads"/> was set. Forgets the screen, the
    /// remembered focus and the paging marks.
    /// </summary>
    /// <remarks>ADR: foundation/architecture — Disposal.</remarks>
    internal void Shutdown()
    {
        _areArrowsNavigation = false;
        SetArrowCombos(false);
        if (_isUiGamepadsSet)
        {
            ApplyUiGamepads(UiGamepads.All);
        }

        _paging.Clear();
        _screen = null;
        _screenFocus = null;
        _remembered = null;
        _rememberedScope = null;
    }

    /// <summary>
    /// Whether the focus or its mode changed in this frame: Gum, or the focus restore of this module, answered the
    /// press already.
    /// </summary>
    /// <returns><see langword="true"/> when the element in focus or its mode differs from the end of the previous
    /// frame.</returns>
    internal bool HasFocusChangedThisFrame()
    {
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        return !ReferenceEquals(receiver, _frameReceiver) || ModeOf(receiver) != _frameReceiverMode;
    }

    /// <summary>
    /// Whether a navigation button of a menu gamepad or a navigation key is pressed or repeats in this frame.
    /// </summary>
    /// <returns><see langword="true"/> for a press or an auto-repeat of the D-pad, the left stick, a face or shoulder
    /// button, an arrow, PageUp, PageDown, Enter, Space or Tab.</returns>
    /// <remarks>
    /// Reads the state Gum has for this frame, so it answers the same from inside a Gum event and from
    /// <c>MolibService.Update</c>. ADR: integration/sound — Silence for programmatic changes.
    /// </remarks>
    internal static bool IsNavigationActive()
    {
        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            IGamePad pad = pads[i];
            for (int b = 0; b < _navigationButtons.Length; b++)
            {
                if (pad.ButtonRepeatRate(_navigationButtons[b]))
                {
                    return true;
                }
            }

            for (int d = 0; d < _stickDirections.Length; d++)
            {
                if (pad.LeftStick.AsDPadPushedRepeatRate(_stickDirections[d]))
                {
                    return true;
                }
            }
        }

        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count; i++)
        {
            IInputReceiverKeyboard keyboard = keyboards[i];
            if (keyboard.KeyTyped(Keys.Tab))
            {
                return true;
            }

            for (int k = 0; k < _editingKeys.Length; k++)
            {
                if (keyboard.KeyTyped(_editingKeys[k]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The last visible child of <c>ModalRoot</c>: the only element Gum gives input to while it exists.
    /// </summary>
    /// <returns>The top modal element, or <see langword="null"/>.</returns>
    internal GraphicalUiElement? TopModal()
    {
        ObservableCollection<GraphicalUiElement> children = _gum.ModalRoot.Children;
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
    /// Moves the focus into a new layer: to <paramref name="target"/> if it can take it, otherwise to the first
    /// focusable element of <paramref name="scope"/>. With the pointer as the last device, only remembers it.
    /// </summary>
    /// <param name="target">The initial focus of the layer.</param>
    /// <param name="scope">Root of the layer.</param>
    /// <remarks>
    /// A focus left outside the layer is taken away in any case: Gum sends the keyboard to the element in focus
    /// even when it lies under a modal window. ADR: interaction/layers — Focus.
    /// </remarks>
    internal void FocusLayer(FrameworkElement? target, GraphicalUiElement scope)
    {
        if (InteractiveGue.CurrentInputReceiver is { } receiver && !IsInside(VisualOf(receiver), scope))
        {
            InteractiveGue.CurrentInputReceiver = null;
        }

        FrameworkElement? inside = target is not null && IsInside(target.Visual, scope) ? target : null;
        if (LastDevice == InputDeviceKind.Cursor)
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
    internal void ReturnFocus(FrameworkElement? target)
    {
        _remembered = target;
        _rememberedScope = null;
        if (LastDevice == InputDeviceKind.Navigation && !IsValid(InteractiveGue.CurrentInputReceiver))
        {
            RestoreFocus();
        }
    }

    /// <summary>
    /// Takes the focus away from anything inside <paramref name="root"/> and forgets a remembered focus there.
    /// </summary>
    /// <param name="root">Root of a layer that starts closing.</param>
    internal void ClearFocusInside(GraphicalUiElement root)
    {
        if (InteractiveGue.CurrentInputReceiver is { } receiver && IsInside(VisualOf(receiver), root))
        {
            InteractiveGue.CurrentInputReceiver = null;
        }

        if (_remembered is not null && IsInside(_remembered.Visual, root))
        {
            _remembered = null;
        }

        if (_rememberedScope is not null && IsInside(_rememberedScope, root))
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
    internal void UnfocusTextField(TextBoxBase field)
    {
        field.IsFocused = false;
        _remembered = field;
        _rememberedScope = null;
    }

    /// <summary>
    /// Whether <paramref name="receiver"/> handles the arrow keys itself and keeps the focus under the pointer.
    /// </summary>
    /// <param name="receiver">The element in focus.</param>
    /// <returns><see langword="true"/> for a text field, a list with the focus on its rows, an open combo box, a
    /// menu and a molib virtual list.</returns>
    /// <remarks>
    /// Checked against Gum 2026.11.2.1: these elements handle the arrows and then still call the common keyboard
    /// navigation. ADR: interaction/focus-and-gamepad — Keyboard arrows, Focus visibility.
    /// </remarks>
    private static bool UsesKeyboardItself(IInputReceiver receiver) => receiver switch
    {
        TextBoxBase => true,
        ListBox list => list.DoListItemsHaveFocus,
        Menu or MenuItem => true,
        ComboBox comboBox => comboBox.IsDropDownOpen,
        IVirtualList => true,
        _ => false,
    };

    /// <summary>
    /// The mode of the element in focus that a cancel press can change in Gum: the focus on the rows of a list or
    /// a scroll viewer, an open drop-down.
    /// </summary>
    private static bool ModeOf(IInputReceiver? receiver) => receiver switch
    {
        ListBox list => list.DoListItemsHaveFocus,
        ScrollViewer viewer => viewer.DoItemsHaveFocus,
        ComboBox comboBox => comboBox.IsDropDownOpen,
        _ => false,
    };

    private static GraphicalUiElement? VisualOf(IInputReceiver? receiver) => receiver switch
    {
        FrameworkElement element => element.Visual,
        GraphicalUiElement visual => visual,
        _ => null,
    };

    private static bool IsInside(GraphicalUiElement? element, GraphicalUiElement root)
    {
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private static void FocusFirst(GraphicalUiElement scope)
    {
        if (scope is InteractiveGue container)
        {
            FrameworkElement.HandleTab(TabDirection.Down, null, container, shouldAskParent: false, loop: false);
        }
    }

    private static void SetArrowCombos(bool areOn)
    {
        SetCombo(FrameworkElement.UpKeyCombos, Keys.Up, areOn);
        SetCombo(FrameworkElement.DownKeyCombos, Keys.Down, areOn);
        SetCombo(FrameworkElement.LeftKeyCombos, Keys.Left, areOn);
        SetCombo(FrameworkElement.RightKeyCombos, Keys.Right, areOn);

        static void SetCombo(List<KeyCombo> combos, Keys key, bool isOn)
        {
            combos.Clear();
            if (isOn)
            {
                combos.Add(new KeyCombo { PushedKey = key, IsTriggeredOnRepeat = true });
            }
        }
    }

    private static bool IsAnyNavigationButtonPushed(IGamePad pad)
    {
        for (int i = 0; i < _navigationButtons.Length; i++)
        {
            if (pad.ButtonPushed(_navigationButtons[i]))
            {
                return true;
            }
        }

        for (int i = 0; i < _stickDirections.Length; i++)
        {
            if (pad.RightStick.AsDPadPushed(_stickDirections[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateConnections()
    {
        GamePad[] pads = _gum.Gamepads;
        for (int i = 0; i < pads.Length && i < _wasConnected.Length; i++)
        {
            bool isConnected = pads[i].IsConnected;
            if (isConnected == _wasConnected[i])
            {
                continue;
            }

            _wasConnected[i] = isConnected;
            EventHandler<GamepadEventArgs>? handler = isConnected ? GamepadConnected : GamepadDisconnected;
            handler?.Invoke(this, new GamepadEventArgs(i));
        }
    }

    /// <remarks>
    /// The first frame only records the cursor position: Gum starts the cursor at zero, so its first movement is
    /// not a movement of the mouse.
    /// </remarks>
    private bool ReadCursorUse()
    {
        ICursor cursor = FrameworkElement.MainCursor;
        bool hasMoved = _hasCursorPosition && (cursor.X != _cursorX || cursor.Y != _cursorY);
        _hasCursorPosition = true;
        _cursorX = cursor.X;
        _cursorY = cursor.Y;
        return hasMoved || cursor.PrimaryPush || cursor.SecondaryPush || cursor.MiddlePush
            || cursor.ScrollWheelChange != 0;
    }

    private bool ReadNavigationPress(out int gamepadIndex)
    {
        gamepadIndex = LastGamepadIndex;
        bool isPressed = false;
        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            if (!IsAnyNavigationButtonPushed(pads[i]))
            {
                continue;
            }

            isPressed = true;
            int slot = pads[i] is GamePad pad ? Array.IndexOf(_gum.Gamepads, pad) : -1;
            if (slot >= 0)
            {
                gamepadIndex = slot;
            }
        }

        bool isEditingText = InteractiveGue.CurrentInputReceiver is TextBoxBase;
        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count && !isPressed; i++)
        {
            IInputReceiverKeyboard keyboard = keyboards[i];
            isPressed = keyboard.KeyPushed(Keys.Tab)
                || (!isEditingText && (IsAnyKeyPushed(keyboard, _editingKeys) || keyboard.KeyPushed(PickKey)));
        }

        return isPressed;

        static bool IsAnyKeyPushed(IInputReceiverKeyboard keyboard, Keys[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (keyboard.KeyPushed(keys[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void SetLastDevice(InputDeviceKind device, int gamepadIndex)
    {
        if (device == LastDevice && gamepadIndex == LastGamepadIndex)
        {
            return;
        }

        LastDevice = device;
        LastGamepadIndex = gamepadIndex;
        LastDeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <remarks>
    /// Elements that work with the keyboard themselves keep the focus: a text field needs it for typing.
    /// ADR: interaction/focus-and-gamepad — Focus visibility.
    /// </remarks>
    private void HideFocus()
    {
        IInputReceiver? receiver = InteractiveGue.CurrentInputReceiver;
        if (receiver is null || UsesKeyboardItself(receiver))
        {
            return;
        }

        _remembered = receiver as FrameworkElement;
        _rememberedScope = null;
        InteractiveGue.CurrentInputReceiver = null;
    }

    /// <remarks>
    /// The remembered element first, then the remembered scope of a layer, then the initial focus of the screen,
    /// then the first focusable element of the top modal window, the screen or the root.
    /// ADR: interaction/focus-and-gamepad — Focus visibility.
    /// </remarks>
    private void RestoreFocus()
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
            FocusFirst(_screen is not null && IsShown(_screen, null) ? _screen : _gum.Root);
        }
    }

    private void HandlePaging()
    {
        int step = ReadPageStep();
        if (step == 0)
        {
            return;
        }

        for (GraphicalUiElement? area = VisualOf(InteractiveGue.CurrentInputReceiver);
            area is not null;
            area = area.Parent)
        {
            if (_paging.TryGetValue(area, out Action<int>? page))
            {
                page(step);
                return;
            }
        }

        PageUnhandled?.Invoke(this, new PageEventArgs(step));
    }

    private static int ReadPageStep()
    {
        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            if (pads[i].ButtonPushed(GamepadButton.LeftShoulder))
            {
                return -1;
            }

            if (pads[i].ButtonPushed(GamepadButton.RightShoulder))
            {
                return 1;
            }
        }

        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count; i++)
        {
            IInputReceiverKeyboard keyboard = keyboards[i];
            if (!keyboard.IsCtrlDown)
            {
                continue;
            }

            if (keyboard.KeyPushed(Keys.PageUp))
            {
                return -1;
            }

            if (keyboard.KeyPushed(Keys.PageDown))
            {
                return 1;
            }
        }

        return 0;
    }

    private void ApplyUiGamepads(UiGamepads selection)
    {
        List<IGamePad> list = FrameworkElement.GamePadsForUiControl;
        GamePad[] pads = _gum.Gamepads;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is GamePad pad && Array.IndexOf(pads, pad) >= 0)
            {
                list.RemoveAt(i);
            }
        }

        for (int i = 0; i < pads.Length; i++)
        {
            if (selection.Includes(i))
            {
                list.Add(pads[i]);
            }
        }
    }

    private bool IsValid(IInputReceiver? receiver) => receiver switch
    {
        null => false,
        FrameworkElement element => CanTakeFocus(element, TopModal()),
        _ => true,
    };

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
            && (ReferenceEquals(top, _gum.Root) || ReferenceEquals(top, _gum.ModalRoot)
                || ReferenceEquals(top, _gum.PopupRoot));
    }
}
