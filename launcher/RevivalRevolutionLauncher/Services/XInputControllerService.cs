using System.Runtime.InteropServices;

namespace RevivalRevolution.Services;

public sealed record XInputControllerState(
    int UserIndex,
    string Buttons,
    int LeftTrigger,
    int RightTrigger,
    double LeftStickX,
    double LeftStickY,
    double RightStickX,
    double RightStickY);

/// <summary>
/// Read-only polling for XInput-compatible controllers. This does not intercept or alter game input.
/// Generic HID devices that do not expose an XInput interface are not enumerated by this service.
/// </summary>
public sealed class XInputControllerService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint userIndex, out State state);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState13(uint userIndex, out State state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint userIndex, out State state);

    public IReadOnlyList<XInputControllerState> ReadConnected()
    {
        var connected = new List<XInputControllerState>();
        for (uint index = 0; index < 4; index++)
        {
            if (!TryRead(index, out var state)) continue;
            var pad = state.Gamepad;
            connected.Add(new(
                (int)index,
                FormatButtons(pad.Buttons),
                pad.LeftTrigger,
                pad.RightTrigger,
                Normalize(pad.ThumbLX),
                Normalize(pad.ThumbLY),
                Normalize(pad.ThumbRX),
                Normalize(pad.ThumbRY)));
        }
        return connected;
    }

    private static bool TryRead(uint index, out State state)
    {
        try { return GetState14(index, out state) == 0; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        try { return GetState13(index, out state) == 0; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        try { return GetState910(index, out state) == 0; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        state = default;
        return false;
    }

    private static double Normalize(short value) =>
        Math.Round(Math.Clamp(value / 32767d, -1d, 1d) * 100d, 0);

    private static string FormatButtons(ushort value)
    {
        var names = new List<string>();
        Add(0x0001, "D-PAD ↑");
        Add(0x0002, "D-PAD ↓");
        Add(0x0004, "D-PAD ←");
        Add(0x0008, "D-PAD →");
        Add(0x0010, "START");
        Add(0x0020, "BACK");
        Add(0x0040, "L3");
        Add(0x0080, "R3");
        Add(0x0100, "LB");
        Add(0x0200, "RB");
        Add(0x1000, "A");
        Add(0x2000, "B");
        Add(0x4000, "X");
        Add(0x8000, "Y");
        return names.Count == 0 ? "nenhum" : string.Join(", ", names);

        void Add(ushort mask, string name)
        {
            if ((value & mask) != 0) names.Add(name);
        }
    }
}
