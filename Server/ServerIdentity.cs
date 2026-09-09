using System;
using System.IO;

namespace AionSniffer.Server;

/// <summary>
/// Identifies which private server an Aion install actually connects to. Chat.log itself carries
/// no server identity at all -- checked and confirmed against every obvious signal (no server name,
/// no IP, no login handshake is ever logged) -- so this reads the one file that DOES carry it: the
/// client's own bin64\config.ini (bin32\config.ini as a fallback, for a 32-bit-only install), whose
/// [ServerAddr] section is the actual IP:port the client connects the game session to. A private
/// server operator sets this before distributing their client, so it is stable and unique per
/// server -- unlike the client executable's own file version, which is an internal build number
/// (e.g. "4515.0319.0112.8880") that does not correspond to the community's patch labels
/// ("4.6", "7.2", ...) at all.
/// </summary>
public static class ServerIdentity
{
    /// <summary>"IP:PORT" from [ServerAddr], or null if the install folder is unset, neither config
    /// file exists, or it has no BIND_ADDR/BIND_PORT under that section -- callers must treat null
    /// as "unknown", never fabricate a fingerprint.</summary>
    public static string? DetectFingerprint(string? aionInstallFolder)
    {
        if (string.IsNullOrEmpty(aionInstallFolder))
        {
            return null;
        }

        return ReadServerAddr(Path.Combine(aionInstallFolder, "bin64", "config.ini"))
            ?? ReadServerAddr(Path.Combine(aionInstallFolder, "bin32", "config.ini"));
    }

    private static string? ReadServerAddr(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return null;
            }

            string? addr = null;
            string? port = null;
            string currentSection = "";

            foreach (string rawLine in File.ReadLines(configPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    currentSection = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(currentSection, "ServerAddr", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                string key = line[..eq].Trim();
                string value = line[(eq + 1)..].Trim();
                if (string.Equals(key, "BIND_ADDR", StringComparison.OrdinalIgnoreCase))
                {
                    addr = value;
                }
                else if (string.Equals(key, "BIND_PORT", StringComparison.OrdinalIgnoreCase))
                {
                    port = value;
                }
            }

            return addr is not null && port is not null ? $"{addr}:{port}" : null;
        }
        catch (IOException)
        {
            // The client itself only reads this file at launch, but a locked or otherwise
            // unreadable file must not take the meter down over a detail this secondary --
            // callers already treat null as "unknown, ask the user".
            return null;
        }
    }
}
