using System.Reflection;

namespace AionSniffer.Update;

/// <summary>
/// The running build's version, read back from the assembly so AionSniffer.csproj's &lt;Version&gt;
/// stays the only place it is written. Both the window title and the update check need it, and a
/// second hand-typed copy is exactly how a title and an update comparison drift apart.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// Display form, e.g. "0.5.1". The SDK appends "+&lt;commit sha&gt;" to InformationalVersion;
    /// only the part before it is the version anyone means.
    /// </summary>
    /// <remarks>
    /// MUST stay declared above <see cref="Current"/>. Static field initializers run in textual
    /// order, so with Current first it parsed a Text that was still null and every build reported
    /// itself as 0.0.0 -- which made every release look newer and the update notice appear
    /// permanently, on a fresh install of the very version being offered. Caught by
    /// SelfCheck.RunAppVersionScenario, not by reading the code.
    /// </remarks>
    public static string Text { get; } = (Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "")
        .Split('+')[0];

    /// <summary>
    /// Parsed form, for comparing against a release tag. Falls back to 0.0.0 rather than throwing:
    /// a build without version metadata should still start, it just never considers itself current
    /// (which is the safe direction -- it offers an update rather than hiding one).
    /// </summary>
    public static Version Current { get; } = Parse(Text);

    /// <summary>
    /// Turns "v0.5.1", "0.5.1" or "v1.0.0-beta.2" into a Version. Everything from the first "-" is
    /// dropped, the same way the release workflow derives the MSI's ProductVersion from the tag --
    /// so a prerelease tag compares as its base version instead of failing to parse at all.
    /// </summary>
    public static Version Parse(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        int dash = s.IndexOf('-');
        if (dash >= 0)
        {
            s = s[..dash];
        }

        return Version.TryParse(s, out var parsed) ? Normalize(parsed) : new Version(0, 0, 0);
    }

    /// <summary>
    /// Pads to major.minor.build so "0.5" and "0.5.0" compare equal, and drops Revision so the
    /// assembly's own "0.5.1.0" never counts as newer than the tag "v0.5.1" it was built from --
    /// which would make every installed copy permanently believe it was ahead of the release.
    /// </summary>
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
}
