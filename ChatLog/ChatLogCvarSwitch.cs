using System.Diagnostics;
using AionSniffer.Native;

namespace AionSniffer.ChatLog;

public enum ChatLogCvarSwitchStatus
{
    Disabled,
    ProcessNotFound,
    AccessDenied,
    Scanning,
    Active,
    WriteFailed,
}

/// <summary>
/// Automates what ShugoConsole's "Chat Log" checkbox does by hand: forces the CryEngine console
/// variable `g_chatlog` to 1 inside the running Aion client, via the same technique ShugoConsole
/// itself uses (VirtualQueryEx region scan + ReadProcessMemory pattern match + WriteProcessMemory
/// -- see Native/CryCVarScanner.cs and Native/CryCVarHandle.cs, ported from ShugoConsole's
/// memorypattern.h/remotememorylookup.cpp/crycvar.h). NCSoft disabled the western client's
/// in-game console, and g_chatlog is what actually gates whether the client writes combat lines
/// to Chat.log at all -- without it, the whole chat-log-based path this class lives next to has
/// nothing to read.
///
/// Explicit opt-in only (MeterSettings.AutoEnableChatLogCvar, default off) -- per the user,
/// accepting the risk noted in README's ShugoConsole section (an earlier probe found the Origin
/// client's memory reads blocked by anti-cheat when hunting for the network crypto key; whether
/// that same protection covers the CVar region this class targets is unconfirmed either way).
///
/// One instance tracks one Aion process at a time and re-acquires both the process handle and the
/// scanned CVar address whenever either goes stale (process restarted, handle invalidated) --
/// same self-healing loop as ShugoConsole's AionProcessWorker.
/// </summary>
public sealed class ChatLogCvarSwitch : IDisposable
{
    private const string CVarName = "g_chatlog";
    private static readonly string[] ProcessNames = { "aion", "aion.bin" };

    private IntPtr _hProcess;
    private CryCVarHandle _cvar;

    public ChatLogCvarSwitchStatus Status { get; private set; } = ChatLogCvarSwitchStatus.Disabled;

    /// <summary>Call roughly once a second while the switch is enabled -- MainWindow drives this
    /// off the same DispatcherTimer as chat-log tailing. Finds/re-finds the Aion process and CVar
    /// address as needed, and forces the value back to 1 if the client ever resets it (restart,
    /// zone change, ...).</summary>
    public void Tick()
    {
        if (_hProcess == IntPtr.Zero && !TryAcquireProcess())
        {
            return;
        }

        if (!_cvar.Valid && !TryLocateCVar())
        {
            return;
        }

        if (!_cvar.TryGetInt(out int current))
        {
            // The handle stopped working (process exited, or access revoked mid-session) --
            // release it so the next Tick starts a clean search rather than spinning on a dead handle.
            ReleaseHandle();
            Status = ChatLogCvarSwitchStatus.AccessDenied;
            return;
        }

        if (current == 1)
        {
            Status = ChatLogCvarSwitchStatus.Active;
            return;
        }

        if (_cvar.TrySet(1))
        {
            Status = ChatLogCvarSwitchStatus.Active;
        }
        else
        {
            ReleaseHandle();
            Status = ChatLogCvarSwitchStatus.WriteFailed;
        }
    }

    /// <summary>Releases the process handle and forgets the scanned address -- call when the user
    /// turns the switch off in Settings, so re-enabling later starts a clean scan instead of
    /// reusing a stale handle/address.</summary>
    public void Reset()
    {
        ReleaseHandle();
        Status = ChatLogCvarSwitchStatus.Disabled;
    }

    private bool TryAcquireProcess()
    {
        using var process = FindAionProcess();
        if (process is null)
        {
            Status = ChatLogCvarSwitchStatus.ProcessNotFound;
            return false;
        }

        const uint access = NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE
            | NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_QUERY_INFORMATION;
        _hProcess = NativeMethods.OpenProcess(access, false, process.Id);
        if (_hProcess == IntPtr.Zero)
        {
            Status = ChatLogCvarSwitchStatus.AccessDenied;
            return false;
        }

        return true;
    }

    private bool TryLocateCVar()
    {
        Status = ChatLogCvarSwitchStatus.Scanning;
        bool is64Bit = NativeMethods.IsProcess64Bit(_hProcess);
        nint? address = CryCVarScanner.FindCVarAddress(_hProcess, CVarName, is64Bit);
        if (address is null)
        {
            return false; // try again next Tick -- the engine may not have initialized its console yet
        }

        _cvar = new CryCVarHandle(_hProcess, address.Value, is64Bit);
        return true;
    }

    private static Process? FindAionProcess()
    {
        foreach (string name in ProcessNames)
        {
            foreach (var candidate in Process.GetProcessesByName(name))
            {
                if (!candidate.HasExited)
                {
                    return candidate;
                }

                candidate.Dispose();
            }
        }

        return null;
    }

    private void ReleaseHandle()
    {
        if (_hProcess != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }

        _cvar = default;
    }

    public void Dispose() => ReleaseHandle();
}
