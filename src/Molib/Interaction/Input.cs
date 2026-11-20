using System.Runtime.CompilerServices;
using Gum;
using Gum.Forms.Controls;
using Gum.Input;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
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
/// <see cref="NavigationInput"/> reads the input Gum has already read in this frame from Gum's own lists, and
/// <see cref="FocusKeeper"/> hides, restores and moves the focus; this class tracks the devices, the gamepads and
/// paging, and turns the arrow keys on and off.
/// </para>
/// </remarks>
public sealed class Input
{
    private readonly Game _game;
    private readonly GumService _gum;
    private readonly MolibLifetime _lifetime;
    private bool _isUiGamepadsSet;
    private readonly bool[] _wasConnected = new bool[UiGamepads.SlotCount];
    private readonly ConditionalWeakTable<GraphicalUiElement, Action<int>> _paging = [];
    private bool _hasCursorPosition;
    private int _cursorX;
    private int _cursorY;
    private IInputReceiver? _frameReceiver;
    private bool _frameReceiverMode;
    private bool _areArrowsNavigation;

    internal Input(Game game, GumService gum, MolibLifetime lifetime)
    {
        _game = game;
        _gum = gum;
        _lifetime = lifetime;
        Focus = new FocusKeeper(gum, this);
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
    /// Hides and restores the focus, gives screens and layers their initial focus.
    /// </summary>
    internal FocusKeeper Focus { get; }

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
        Focus.FocusScreen(screen, initialFocus);
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
        int gamepadIndex = LastGamepadIndex;
        bool isNavigationPressed = NavigationInput.ReadPush(_gum.Gamepads, PickKey, ref gamepadIndex);
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
            Focus.Hide();
            return;
        }

        if (!isNavigationPressed)
        {
            return;
        }

        if (!Focus.IsValid(InteractiveGue.CurrentInputReceiver))
        {
            Focus.Restore();
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
        bool areArrowsNavigation = receiver is not null && !NavigationInput.UsesKeyboardItself(receiver);
        if (areArrowsNavigation != _areArrowsNavigation)
        {
            _areArrowsNavigation = areArrowsNavigation;
            NavigationInput.SetArrowCombos(areArrowsNavigation);
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
        NavigationInput.SetArrowCombos(false);
        if (_isUiGamepadsSet)
        {
            ApplyUiGamepads(UiGamepads.All);
        }

        _paging.Clear();
        Focus.Clear();
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

    private void HandlePaging()
    {
        int step = NavigationInput.ReadPageStep();
        if (step == 0)
        {
            return;
        }

        for (GraphicalUiElement? area = FocusKeeper.VisualOf(InteractiveGue.CurrentInputReceiver);
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

}
