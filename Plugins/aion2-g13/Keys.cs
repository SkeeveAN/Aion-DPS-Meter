using System.Runtime.InteropServices;

namespace Aion2G13Plugin;

/// <summary>Sends a key combination such as "Ctrl+Alt+F13" to whatever has the focus. F13-F24 are used by hardly any game, so they are the safe choice.</summary>
public static class Keys
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public uint Type;
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
        public long Pad; // makes the union as large as MOUSEINPUT
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, KeyInput[] inputs, int size);

    public static bool TryParse(string text, out List<ushort> modifiers, out ushort key)
    {
        modifiers = new List<ushort>();
        key = 0;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.ToLowerInvariant();
            switch (part)
            {
                case "ctrl" or "strg": modifiers.Add(0x11); continue;
                case "alt": modifiers.Add(0x12); continue;
                case "shift": modifiers.Add(0x10); continue;
                case "win": modifiers.Add(0x5B); continue;
            }

            if (key != 0)
            {
                return false;
            }

            if (part.Length >= 2 && part[0] == 'f' && int.TryParse(part[1..], out int f) && f is >= 1 and <= 24)
            {
                key = (ushort)(0x70 + f - 1);
            }
            else if (part.Length == 1 && (char.IsAsciiLetterOrDigit(part[0])))
            {
                key = (ushort)char.ToUpperInvariant(part[0]);
            }
            else if (part.StartsWith("num") && int.TryParse(part[3..], out int n) && n is >= 0 and <= 9)
            {
                key = (ushort)(0x60 + n);
            }
            else
            {
                return false;
            }
        }

        return key != 0;
    }

    public static bool Send(string text)
    {
        if (!TryParse(text, out var modifiers, out ushort key))
        {
            return false;
        }

        var inputs = new List<KeyInput>();
        foreach (ushort m in modifiers)
        {
            inputs.Add(Make(m, false));
        }

        inputs.Add(Make(key, false));
        inputs.Add(Make(key, true));
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(Make(modifiers[i], true));
        }

        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<KeyInput>()) == inputs.Count;
    }

    private static KeyInput Make(ushort vk, bool up) => new() { Type = 1, Vk = vk, Flags = up ? 2u : 0u };
}
