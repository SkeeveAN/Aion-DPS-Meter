using System.Runtime.InteropServices;

namespace AionSniffer.Native;

/// <summary>
/// Win32 P/Invoke surface for the g_chatlog memory patch (see ChatLog/ChatLogCvarSwitch.cs) --
/// a direct port of the handful of kernel32 calls ShugoConsole uses (win64utils.cpp,
/// remotememorylookup.cpp, crycvar.h) to read/write another process's memory.
/// </summary>
internal static class NativeMethods
{
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_PRIVATE = 0x20000;
    public const uint PAGE_READWRITE = 0x04;
    public const uint PAGE_EXECUTE_READWRITE = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint VirtualQueryEx(IntPtr hProcess, nint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nint dwLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(IntPtr hProcess, nint lpBaseAddress, byte[] lpBuffer, int dwSize, out nint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(IntPtr hProcess, nint lpBaseAddress, byte[] lpBuffer, int nSize, out nint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    /// <summary>Port of ShugoConsole's win64utils.cpp IsProcess64 -- simplified to the common case
    /// (this app itself running as a native 64-bit process, the .NET default on a 64-bit OS since
    /// there's no Prefer32Bit setting in the csproj) rather than the C++ original's extra branch
    /// for a 32-bit host: a target is 64-bit iff the OS is 64-bit AND the target isn't itself
    /// running under WOW64.</summary>
    public static bool IsProcess64Bit(IntPtr hProcess)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        if (!IsWow64Process(hProcess, out bool isWow64))
        {
            return false; // API call failed -- fall back to 32-bit, same as ShugoConsole's own fallback
        }

        return !isWow64;
    }
}
