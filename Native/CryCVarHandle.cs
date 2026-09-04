using System.Text;

namespace AionSniffer.Native;

/// <summary>
/// Reads/writes a single CryEngine console-variable object once its address is known (see
/// CryCVarScanner) -- a direct port of ShugoConsole's crycvar.h. int/float/string sit
/// contiguously right after the object's flag+name (4 bytes apart each, confirmed by
/// ShugoConsole's own offsets: 160/164/168 on 32-bit, 184/188/192 on 64-bit), so setting an int
/// value writes all three representations in one WriteProcessMemory call, same as the original.
/// </summary>
internal readonly struct CryCVarHandle
{
    private const int IntOffset32 = 160;
    private const int IntOffset64 = 184;

    private readonly IntPtr _hProcess;
    private readonly nint _address;
    private readonly int _intOffset;

    public CryCVarHandle(IntPtr hProcess, nint address, bool is64Bit)
    {
        _hProcess = hProcess;
        _address = address;
        _intOffset = is64Bit ? IntOffset64 : IntOffset32;
    }

    public bool Valid => _hProcess != IntPtr.Zero && _address != 0;

    public bool TryGetInt(out int value)
    {
        var buffer = new byte[sizeof(int)];
        bool ok = NativeMethods.ReadProcessMemory(_hProcess, _address + _intOffset, buffer, buffer.Length, out _);
        value = ok ? BitConverter.ToInt32(buffer, 0) : 0;
        return ok;
    }

    public bool TrySet(int value)
    {
        byte[] stringBytes = Encoding.ASCII.GetBytes(value.ToString());
        byte[] buffer = new byte[sizeof(int) + sizeof(float) + stringBytes.Length + 1]; // trailing byte stays 0 -- the string's null terminator
        BitConverter.GetBytes(value).CopyTo(buffer, 0);
        BitConverter.GetBytes((float)value).CopyTo(buffer, sizeof(int));
        stringBytes.CopyTo(buffer, sizeof(int) + sizeof(float));

        return NativeMethods.WriteProcessMemory(_hProcess, _address + _intOffset, buffer, buffer.Length, out _);
    }
}
