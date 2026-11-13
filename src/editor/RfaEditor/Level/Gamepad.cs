using System.Runtime.InteropServices;

namespace RfaEditor;

/// <summary>An Xbox controller's buttons (XInput's XINPUT_GAMEPAD wButtons).</summary>
[Flags]
enum PadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
    Start = 0x0010, Back = 0x0020, LeftStick = 0x0040, RightStick = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

/// <summary>One reading of a controller: its buttons, and its sticks and triggers from −1 to 1 (0 to 1), past their dead zones.</summary>
readonly record struct PadState(PadButtons Buttons, float LeftX, float LeftY, float RightX, float RightY, float LeftTrigger, float RightTrigger)
{
    public bool Holds(PadButtons button) => (Buttons & button) != 0;
}

/// <summary>
/// The first Xbox (XInput) controller connected, read when asked: what it holds now, and which
/// buttons went down since the last reading. Windows' own xinput1_4.dll (Windows 8 and later), or
/// xinput9_1_0.dll; with neither, there is no controller.
/// </summary>
sealed class Gamepad
{
    [StructLayout(LayoutKind.Sequential)]
    struct XInputGamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short ThumbLX, ThumbLY, ThumbRX, ThumbRY; }

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState { public uint PacketNumber; public XInputGamepad Gamepad; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern uint GetState14(uint index, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    static extern uint GetState910(uint index, out XInputState state);

    int library;  // 0 xinput1_4, 1 xinput9_1_0, 2 none
    int slot = -1;
    PadButtons last;
    DateTime nextSearch;

    /// <summary>Whether a controller answered the last reading.</summary>
    public bool Connected => slot >= 0;

    uint Get(uint index, out XInputState state)
    {
        state = default;
        while (library < 2)
        {
            try { return library == 0 ? GetState14(index, out state) : GetState910(index, out state); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { library++; }
        }
        return 1167;  // ERROR_DEVICE_NOT_CONNECTED
    }

    /// <summary>Reads the controller; false if none is connected (looked for again at most twice a second).</summary>
    public bool Read(out PadState now, out PadButtons pressed)
    {
        now = default;
        pressed = PadButtons.None;
        if (slot < 0)
        {
            if (DateTime.Now < nextSearch) return false;
            nextSearch = DateTime.Now.AddSeconds(0.5);
            for (uint i = 0; i < 4 && slot < 0; i++)
                if (Get(i, out _) == 0) slot = (int)i;
            if (slot < 0) return false;
        }
        if (Get((uint)slot, out XInputState state) != 0)
        {
            slot = -1;
            last = PadButtons.None;
            return false;
        }
        XInputGamepad g = state.Gamepad;
        static float Stick(short value, short other)
        {
            const float dead = 7849f / 32767f;  // XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE
            float v = Math.Max(value, (short)-32767) / 32767f;
            float magnitude = MathF.Abs(v);
            return magnitude < dead ? 0 : MathF.Sign(v) * (magnitude - dead) / (1 - dead);
        }
        static float Trigger(byte value) => value < 30 ? 0 : (value - 30) / 225f;  // XINPUT_GAMEPAD_TRIGGER_THRESHOLD
        var buttons = (PadButtons)g.Buttons;
        now = new PadState(buttons, Stick(g.ThumbLX, g.ThumbLY), Stick(g.ThumbLY, g.ThumbLX), Stick(g.ThumbRX, g.ThumbRY), Stick(g.ThumbRY, g.ThumbRX),
                           Trigger(g.LeftTrigger), Trigger(g.RightTrigger));
        pressed = buttons & ~last;
        last = buttons;
        return true;
    }
}
