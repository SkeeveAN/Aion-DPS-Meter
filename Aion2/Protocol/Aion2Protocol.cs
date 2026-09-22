using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Aion2.Protocol;

/// <summary>How a game frame is delimited on the wire: a length prefix at a fixed offset.</summary>
public sealed record FrameLayout(
    int LengthOffset,
    int LengthSize,
    bool LittleEndian,
    bool LengthIncludesHeader,
    int HeaderSize,
    int OpcodeOffset,
    int OpcodeSize,
    int MaxFrameLength);

/// <summary>Where one value sits inside a frame. <see cref="Size"/> 1/2/4/8 for integers; strings
/// are UTF-16LE with a 2-byte character count at <see cref="Offset"/> unless <see cref="Encoding"/> says otherwise.</summary>
public sealed record FieldSpec(int Offset, int Size, bool Signed = false, string? Encoding = null, string? Mask = null);

public enum OpcodeFamily
{
    Unknown,
    Damage,
    Dot,
    Heal,
    HpUpdate,
    Nickname,
    Session,
    Kill,
    Avoid,
    NpcSpawn,
    Zone,
}

/// <summary>
/// The Aion 2 wire layout as DATA (assets/aion2/protocol/opcodes.json), not code: every patch
/// shifts opcodes, and a shifted opcode must be a one-line data fix that ships without a rebuild.
/// <see cref="IsCalibrated"/> is false until a real capture has been used to fill the tables - the
/// packet source then reports that instead of decoding nonsense. Field offsets are relative to
/// the frame start (header included).
/// </summary>
public sealed class Aion2Protocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<int, OpcodeFamily> _families = new();
    private readonly Dictionary<OpcodeFamily, IReadOnlyDictionary<string, FieldSpec>> _fields = new();

    public bool IsCalibrated { get; private init; }
    public string GameVersion { get; private init; } = "";
    public IReadOnlyList<int> ServerPorts { get; private init; } = Array.Empty<int>();
    public FrameLayout FrameLayout { get; private init; } = new(0, 2, true, true, 4, 2, 2, 65535);

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "assets", "aion2", "protocol", "opcodes.json");

    /// <summary>Loads the shipped description; an unreadable or missing file yields an
    /// uncalibrated protocol rather than an exception, so the meter still starts.</summary>
    public static Aion2Protocol Load(string? path = null)
    {
        try
        {
            return FromJson(File.ReadAllText(path ?? DefaultPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Aion2Protocol();
        }
    }

    public static Aion2Protocol FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<ProtocolDocument>(json, JsonOptions) ?? new ProtocolDocument();
        var protocol = new Aion2Protocol
        {
            IsCalibrated = doc.Calibrated,
            GameVersion = doc.GameVersion ?? "",
            ServerPorts = doc.ServerPorts ?? Array.Empty<int>(),
            FrameLayout = doc.Frame ?? new FrameLayout(0, 2, true, true, 4, 2, 2, 65535),
        };

        foreach ((string familyName, int[] opcodes) in doc.Opcodes ?? new Dictionary<string, int[]>())
        {
            if (!Enum.TryParse(familyName, ignoreCase: true, out OpcodeFamily family))
            {
                continue;
            }

            foreach (int opcode in opcodes)
            {
                protocol._families[opcode] = family;
            }
        }

        foreach ((string familyName, Dictionary<string, FieldSpec> fields) in doc.Fields ?? new Dictionary<string, Dictionary<string, FieldSpec>>())
        {
            if (Enum.TryParse(familyName, ignoreCase: true, out OpcodeFamily family))
            {
                protocol._fields[family] = fields;
            }
        }

        return protocol;
    }

    public OpcodeFamily FamilyOf(int opcode) => _families.GetValueOrDefault(opcode, OpcodeFamily.Unknown);

    public IReadOnlyDictionary<string, FieldSpec> FieldsOf(OpcodeFamily family) =>
        _fields.GetValueOrDefault(family) ?? new Dictionary<string, FieldSpec>();

    public IEnumerable<OpcodeFamily> KnownFamilies => _families.Values.Distinct();

    private sealed class ProtocolDocument
    {
        public bool Calibrated { get; set; }
        public string? GameVersion { get; set; }
        public int[]? ServerPorts { get; set; }
        public FrameLayout? Frame { get; set; }
        public Dictionary<string, int[]>? Opcodes { get; set; }
        public Dictionary<string, Dictionary<string, FieldSpec>>? Fields { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
