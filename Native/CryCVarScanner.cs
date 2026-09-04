using System.Text;

namespace AionSniffer.Native;

/// <summary>
/// Finds a CryEngine console-variable object's address inside another process's memory, by name
/// -- a direct port of ShugoConsole's memorypattern.h + remotememorylookup.cpp. Deliberately a
/// name-based scan rather than a fixed offset (matching ShugoConsole's own README claim, "works
/// with every version of Aion... scans memory instead of fixed offsets"): CryEngine CVar objects
/// are 16-byte aligned, and each one carries a validity flag (0 or 1) immediately followed by its
/// own name as ASCII, at a fixed distance from the object's start that differs only by process
/// bitness (see CryCVarHandle's offset comment) -- never by client build/version, which is what
/// makes this survive Aion client updates without re-tuning.
/// </summary>
internal static class CryCVarScanner
{
    private const int BufferSize = 131072; // same default chunk size as ShugoConsole's RemoteMemoryLookup

    /// <summary>Offset of the validity-flag byte (immediately followed by the CVar's name) from
    /// the start of the CVar object -- CVAR_MEM_NAME_32/64 minus 1 in ShugoConsole's crycvar.h.</summary>
    private static int NameFlagOffset(bool is64Bit) => is64Bit ? 8 : 4;

    public static nint? FindCVarAddress(IntPtr hProcess, string cvarName, bool is64Bit)
    {
        int nameFlagOffset = NameFlagOffset(is64Bit);
        byte[] namePattern = Encoding.ASCII.GetBytes(cvarName);
        int totalPatternSize = nameFlagOffset + 1 + namePattern.Length;

        byte[] buffer = new byte[BufferSize];
        nint address = 0;

        while (NativeMethods.VirtualQueryEx(hProcess, address, out var info, MbiStructSize) != 0)
        {
            bool isCommittedPrivate = info.State == NativeMethods.MEM_COMMIT && info.Type == NativeMethods.MEM_PRIVATE;
            bool isWritable = info.Protect == NativeMethods.PAGE_READWRITE || info.Protect == NativeMethods.PAGE_EXECUTE_READWRITE;

            if (isCommittedPrivate && isWritable)
            {
                nint? found = ScanRegion(hProcess, info.BaseAddress, info.RegionSize, buffer, nameFlagOffset, namePattern, totalPatternSize);
                if (found is not null)
                {
                    return found;
                }
            }

            nint next = info.BaseAddress + info.RegionSize;
            if (next <= address)
            {
                break; // no forward progress -- avoid an infinite loop on a malformed region
            }

            address = next;
        }

        return null;
    }

    private static nint? ScanRegion(IntPtr hProcess, nint baseAddress, nint regionSize, byte[] buffer,
        int nameFlagOffset, byte[] namePattern, int totalPatternSize)
    {
        nint begin = baseAddress;
        nint remaining = regionSize;

        while (remaining > 0)
        {
            int toRead = (int)Math.Min(remaining, buffer.Length);
            if (!NativeMethods.ReadProcessMemory(hProcess, begin, buffer, toRead, out nint bytesRead) || bytesRead <= 0)
            {
                break; // unreadable region (e.g. guard page, or freed between the query and the read) -- move on
            }

            long scanEnd = bytesRead - totalPatternSize;
            for (long i = 0; i < scanEnd; i += 16)
            {
                byte flag = buffer[i + nameFlagOffset];
                if ((flag == 0 || flag == 1) && MatchesAt(buffer, (int)i + nameFlagOffset + 1, namePattern))
                {
                    return begin + (nint)i;
                }
            }

            if (bytesRead >= remaining)
            {
                break;
            }

            remaining -= bytesRead;
            begin += bytesRead;
        }

        return null;
    }

    private static bool MatchesAt(byte[] buffer, int offset, byte[] pattern)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (buffer[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static readonly nint MbiStructSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORY_BASIC_INFORMATION>();
}
