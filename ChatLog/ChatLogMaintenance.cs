using System.IO;

namespace AionSniffer.ChatLog;

/// <summary>What happened when the meter tried to empty Chat.log.</summary>
public enum EmptyResult
{
    Emptied,

    /// <summary>The file is somewhere this unprivileged process may not write -- typically an Aion
    /// installed under Program Files. The meter dropped its administrator rights when the
    /// packet-capture path was removed, so this is expected, not a bug to work around.</summary>
    NoPermission,

    /// <summary>Locked in a way that refuses even a shared write, or gone.</summary>
    Failed,
}

/// <summary>
/// Housekeeping for Aion's Chat.log. The file only ever grows -- the client never rotates it --
/// and the user's is already past 20 MB after a few days, so it needs both a warning and a way to
/// start over.
/// </summary>
public static class ChatLogMaintenance
{
    /// <summary>50 MB, per the user. Nothing breaks at this size; it is a "deal with it before it
    /// becomes annoying" mark, not a failure threshold.</summary>
    public const long WarnThresholdBytes = 50L * 1024 * 1024;

    public static long SizeOf(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>
    /// True when another process holds the file open -- in practice, a running Aion client. Probed
    /// by asking for an exclusive handle rather than by hunting for a process named "aion.bin":
    /// that answers the question actually being asked (is someone else using this file), works
    /// regardless of what the client executable is called, and needs no rights over other
    /// processes. A permission problem is reported as "not locked" here, because the caller finds
    /// out about that properly when it tries to write.
    /// </summary>
    public static bool IsHeldByAnotherProcess(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Truncates Chat.log to zero without deleting it -- the client keeps its handle, and deleting
    /// the file out from under it would be ruder than emptying it.
    ///
    /// <para><b>Only call this when nothing else has the file open.</b> Truncating a file another
    /// handle still holds does not reclaim anything: the writer keeps its offset, so its next line
    /// restores the file to its previous length as NUL bytes and appends after that. Emptying a
    /// 50 MB Chat.log with Aion running leaves 50 MB of zeros -- worse than doing nothing.
    /// Measured, not assumed: this was first written on the reasoning that log files are opened
    /// for append and would therefore land at the new start, and SelfCheck proved that wrong on
    /// the first run. Callers check <see cref="IsHeldByAnotherProcess"/> first.</para>
    ///
    /// <para>ChatLogTailer already copes with the file shrinking underneath it (it resets to the
    /// new end), so no coordination is needed beyond this call.</para>
    /// </summary>
    public static EmptyResult Empty(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            stream.SetLength(0);
            return EmptyResult.Emptied;
        }
        catch (UnauthorizedAccessException)
        {
            return EmptyResult.NoPermission;
        }
        catch (IOException)
        {
            return EmptyResult.Failed;
        }
    }
}
