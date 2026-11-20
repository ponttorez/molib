using Gum.Forms.Controls;
using Gum.Input;
using Gum.Wireframe;
using Molib.Widgets;
using Keys = Gum.Forms.Input.Keys;

namespace Molib.Interaction;

/// <summary>
/// The navigation input Gum has read in this frame, from Gum's own lists of gamepads and keyboards, and the arrow keys
/// in Gum's direction key lists.
/// </summary>
/// <remarks>
/// Reads <c>GamePadsForUiControl</c> and <c>KeyboardsForUiControl</c>; the game turns them on with
/// <c>UseGamepadDefaults</c> and <c>UseKeyboardDefaults</c>. ADR: interaction/focus-and-gamepad — What molib adds.
/// </remarks>
internal static class NavigationInput
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

    /// <summary>
    /// Whether a navigation button of a menu gamepad or a navigation key is pressed or repeats in this frame.
    /// </summary>
    /// <returns><see langword="true"/> for a press or an auto-repeat of the D-pad, the left stick, a face or shoulder
    /// button, an arrow, PageUp, PageDown, Enter, Space or Tab.</returns>
    /// <remarks>
    /// Reads the state Gum has for this frame, so it answers the same from inside a Gum event and from
    /// <c>MolibService.Update</c>. ADR: integration/sound — Silence for programmatic changes.
    /// </remarks>
    public static bool IsActive()
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
    /// Whether a navigation button of a menu gamepad or a navigation key was pushed in this frame.
    /// </summary>
    /// <param name="slots">The four Gum gamepads, to tell the slot of the gamepad pressed.</param>
    /// <param name="pickKey">The pick key of drag and drop, a navigation key too.</param>
    /// <param name="gamepadIndex">Keeps its value unless a gamepad in one of the four slots was pressed; then the
    /// slot.</param>
    /// <returns><see langword="true"/> for a push of a navigation button or key.</returns>
    /// <remarks>
    /// <c>Back</c> is not a navigation button: it is the system back key on Android. While a text field has the
    /// focus, only Tab counts: the other keys edit text. ADR: interaction/focus-and-gamepad — Last device.
    /// </remarks>
    public static bool ReadPush(GamePad[] slots, Keys pickKey, ref int gamepadIndex)
    {
        bool isPushed = false;
        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            if (!IsAnyNavigationButtonPushed(pads[i]))
            {
                continue;
            }

            isPushed = true;
            int slot = pads[i] is GamePad pad ? Array.IndexOf(slots, pad) : -1;
            if (slot >= 0)
            {
                gamepadIndex = slot;
            }
        }

        bool isEditingText = InteractiveGue.CurrentInputReceiver is TextBoxBase;
        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count && !isPushed; i++)
        {
            IInputReceiverKeyboard keyboard = keyboards[i];
            isPushed = keyboard.KeyPushed(Keys.Tab)
                || (!isEditingText && (IsAnyKeyPushed(keyboard, _editingKeys) || keyboard.KeyPushed(pickKey)));
        }

        return isPushed;
    }

    /// <summary>
    /// The paging step pushed in this frame: LB / RB or Ctrl+PageUp / Ctrl+PageDown.
    /// </summary>
    /// <returns><c>-1</c>, <c>+1</c> or <c>0</c> for none.</returns>
    /// <remarks>ADR: interaction/focus-and-gamepad — Paging: LB / RB.</remarks>
    public static int ReadPageStep()
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
    public static bool UsesKeyboardItself(IInputReceiver receiver) => receiver switch
    {
        TextBoxBase => true,
        ListBox list => list.DoListItemsHaveFocus,
        Menu or MenuItem => true,
        ComboBox comboBox => comboBox.IsDropDownOpen,
        IVirtualList => true,
        _ => false,
    };

    /// <summary>
    /// Puts the arrow keys into Gum's direction key lists or empties them, as Gum has them by default.
    /// </summary>
    /// <param name="areOn">Whether the arrows move the focus.</param>
    /// <remarks>ADR: interaction/focus-and-gamepad — Keyboard arrows.</remarks>
    public static void SetArrowCombos(bool areOn)
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

    private static bool IsAnyKeyPushed(IInputReceiverKeyboard keyboard, Keys[] keys)
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
