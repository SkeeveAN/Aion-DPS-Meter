using System.IO;
using System.Text.Json;

namespace AionDPS.Update;

/// <summary>
/// Where the client finds its data files (tables, names, pictures, interface texts). The client ships a complete set (assets/) and runs from it alone; data the
/// server has newer is taken into a folder of the data directory (<see cref="DataUpdateClient"/>) and wins over the shipped file, so a changed table, a new map or a new
/// instance needs no new release. The next release carries the new set as its own. Every file is asked through <see cref="Path"/>: a file of the folder counts only when
/// the index of what was taken in lists it and the size is as listed (a half written or damaged file never counts); everything else is the shipped file. A newly taken
/// file is used from the next start on (the tables are read once).
/// </summary>
public static class AssetStore
{
    /// <summary>What the index knows about a file taken from the server.</summary>
    public sealed record Entry(string Sha256, long Size, int Format);

    private sealed class IndexFile
    {
        public string AppVersion { get; set; } = "";
        public string ManifestEtag { get; set; } = "";
        public Dictionary<string, Entry> Files { get; set; } = new();
        /// <summary>The SHA-256 of the shipped files, calculated once per installed version (what the server's list is compared with).</summary>
        public Dictionary<string, string> Shipped { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static IndexFile? _index;
    private static readonly Dictionary<string, string> Resolved = new();

    /// <summary>The data folder of the meter (the same as the settings'; a test directory through AIONDPS_DATA_DIR).</summary>
    public static string DataDir => Environment.GetEnvironmentVariable("AIONDPS_DATA_DIR") is { Length: > 0 } testDir
        ? testDir : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aion DPS Meter");

    /// <summary>The folder with the files taken from the server.</summary>
    public static string OverlayRoot => System.IO.Path.Combine(DataDir, "assets");

    /// <summary>The folder with the files the client ships.</summary>
    public static string ShippedRoot => Environment.GetEnvironmentVariable("AIONDPS_SHIPPED_DIR") is { Length: > 0 } testRoot ? testRoot : System.IO.Path.Combine(AppContext.BaseDirectory, "assets"); // the variable is for tests: another folder as the shipped set

    private static string IndexPath => System.IO.Path.Combine(DataDir, "assets-index.json");

    /// <summary>The layout of a file this client can read; the server marks a file with a higher number when its layout changed, and an older client leaves it alone.
    /// Raise it here together with the code that reads the new layout (every file counts as 1 until then).</summary>
    public static int SupportedFormat(string relative) => 1;

    /// <summary>The path of a data file, given as the parts below assets/ (for example <c>"aion2", "pets", "regions.json"</c>): the file taken from the server when there is a
    /// valid one, else the shipped one.</summary>
    public static string Path(params string[] parts)
    {
        string relative = string.Join('/', parts);
        lock (Gate)
        {
            if (Resolved.TryGetValue(relative, out string? known))
            {
                return known;
            }

            string shipped = System.IO.Path.Combine(ShippedRoot, System.IO.Path.Combine(parts));
            string chosen = shipped;
            if (Load().Files.TryGetValue(relative, out Entry? entry))
            {
                string overlay = OverlayPath(relative);
                try
                {
                    if (File.Exists(overlay) && new FileInfo(overlay).Length == entry.Size && entry.Format <= SupportedFormat(relative))
                    {
                        chosen = overlay;
                    }
                }
                catch (IOException)
                {
                    // unreadable: the shipped file stays
                }
            }

            Resolved[relative] = chosen;
            return chosen;
        }
    }

    /// <summary>Where a file taken from the server lives (below <see cref="OverlayRoot"/>, the same relative path).</summary>
    public static string OverlayPath(string relative) => System.IO.Path.Combine(OverlayRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

    internal static Entry? EntryOf(string relative)
    {
        lock (Gate)
        {
            return Load().Files.GetValueOrDefault(relative);
        }
    }

    internal static string ManifestEtag
    {
        get { lock (Gate) { return Load().ManifestEtag; } }
        set { lock (Gate) { Load().ManifestEtag = value; } }
    }

    internal static void SetEntry(string relative, Entry entry)
    {
        lock (Gate)
        {
            Load().Files[relative] = entry;
            Resolved.Remove(relative);
        }
    }

    internal static void RemoveEntry(string relative)
    {
        lock (Gate)
        {
            Load().Files.Remove(relative);
            Resolved.Remove(relative);
        }
    }

    internal static IReadOnlyList<string> IndexedPaths()
    {
        lock (Gate)
        {
            return Load().Files.Keys.ToList();
        }
    }

    /// <summary>The SHA-256 of the shipped file (null when the client does not ship it), calculated once per installed version.</summary>
    internal static string? ShippedSha256(string relative)
    {
        lock (Gate)
        {
            IndexFile index = Load();
            if (index.Shipped.TryGetValue(relative, out string? known))
            {
                return known.Length == 0 ? null : known;
            }
        }

        string path = System.IO.Path.Combine(ShippedRoot, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        string sha = "";
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = File.OpenRead(path);
                sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
            }
        }
        catch (IOException)
        {
            return null; // not remembered: tried again next time
        }

        lock (Gate)
        {
            Load().Shipped[relative] = sha;
        }

        return sha.Length == 0 ? null : sha;
    }

    internal static void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                string temp = IndexPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(Load()));
                File.Move(temp, IndexPath, overwrite: true);
            }
            catch (IOException)
            {
                // the index is only an aid: the next run finds the same differences again
            }
        }
    }

    private static IndexFile Load()
    {
        if (_index is not null)
        {
            return _index;
        }

        IndexFile index = new();
        try
        {
            if (File.Exists(IndexPath))
            {
                index = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(IndexPath)) ?? new IndexFile();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            index = new IndexFile();
        }

        // a new version brings a new shipped set: what was remembered about the old one is calculated again
        if (index.AppVersion != AppVersion.Text)
        {
            index.AppVersion = AppVersion.Text;
            index.Shipped = new Dictionary<string, string>();
            index.ManifestEtag = "";
        }

        return _index = index;
    }

    /// <summary>For tests: forgets what was read, so the next call reads the data folder again.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            _index = null;
            Resolved.Clear();
        }
    }
}
