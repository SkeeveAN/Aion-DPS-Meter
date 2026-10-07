using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AionDPS.Aion2.Protocol;

/// <summary>
/// How a game frame is delimited on the wire. "fixed" (default): a length prefix of
/// <see cref="LengthSize"/> bytes at <see cref="LengthOffset"/>. "varint": a LEB128 length at the
/// start of the frame; the frame is <c>length + prefixBytes + LengthBias</c> bytes long in total and
/// the decoder sees it WITHOUT the prefix (opcode at <see cref="OpcodeOffset"/> of the body).
/// Aion 2 (verified against a real capture, 2026-09-30) is varint with bias -4.
/// </summary>
public sealed record FrameLayout(
    int LengthOffset,
    int LengthSize,
    bool LittleEndian,
    bool LengthIncludesHeader,
    int HeaderSize,
    int OpcodeOffset,
    int OpcodeSize,
    int MaxFrameLength,
    string LengthEncoding = "fixed",
    int LengthBias = 0,
    bool OpcodeBigEndian = false)
{
    public bool IsVarint => string.Equals(LengthEncoding, "varint", StringComparison.OrdinalIgnoreCase);
}

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
    /// <summary>Party roster frame: lists every member's name (no combat id).</summary>
    Roster,
    /// <summary>The local player's own character record: combat id, name, class, level, equipment.</summary>
    Character,
    /// <summary>The local player's full equipment at login: every slot with its item and enchant level.</summary>
    Equipment,
    /// <summary>The local player's skill list at login: skill id with total and base level.</summary>
    Skills,
    /// <summary>The local player's activated Daevanion nodes, per board.</summary>
    Daevanion,
    /// <summary>The local player's species knowledge (Cognia, Fera, Natura, Varia, Specia) at login: level,
    /// progress and the analysed effects of each.</summary>
    Species,
    /// <summary>The titles the local player wears (slot 1..3 with the title id), sent at login and on a change.</summary>
    Titles,
    /// <summary>The local player's skills that have specialisation variants: the five-variant ones are the
    /// stigmas (login).</summary>
    Stigmas,
    /// <summary>The local player's skill bar (the three macro pages) at login: which skills sit on it.</summary>
    SkillBar,
    /// <summary>Another player's character window ("inspect"): name, class, level, gear score, legion and the
    /// full equipment with enchant levels. Sent when the local player opens that player's profile.</summary>
    Inspect,
    /// <summary>"Player seen" frame: skill and combat id, then <c>18 05</c>, name and guild.</summary>
    Appearance,
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

    /// <summary>"varint-v1" = the damage frame is parsed by <see cref="Aion2FrameDecoder"/>'s
    /// built-in varint layout (target id varint, 2 flag bytes, actor id varint, skill id u32, ...);
    /// anything else uses the fixed offsets in <c>fields.damage</c>.</summary>
    public string DamageLayout { get; private init; } = "";

    /// <summary>"varint-v1" = nickname frames are opcode | combat id (varint) | a few bytes | a
    /// length-prefixed name, found by scanning (see Aion2FrameDecoder.DecodeVarintNickname).</summary>
    public string NicknameLayout { get; private init; } = "";

    /// <summary>"varint-v1" = the damage-over-time tick frame is parsed by
    /// <see cref="Aion2FrameDecoder"/>'s built-in layout (target varint, flag byte, actor varint,
    /// stack varint, effect id u32, then optional fields named by the flags); anything else uses
    /// the fixed offsets in <c>fields.dot</c>.</summary>
    public string DotLayout { get; private init; } = "";

    /// <summary>"varint-v1" = the hit-point frame is parsed by <see cref="Aion2FrameDecoder"/>'s
    /// built-in layout (entity varint, format byte, then groups of kind + u32 / kind + u64 values).</summary>
    public string HpLayout { get; private init; } = "";

    /// <summary>Opcode of the "bundle" frame: a 4-byte little-endian uncompressed size followed by
    /// an LZ4 block that holds further complete frames. Null = this protocol has no bundles.</summary>
    public int? BundleOpcode { get; private init; }

    /// <summary>Opcodes known to occur often; only used to recognise a real frame start when a
    /// capture begins in the middle of a connection (see TcpReassembler). Empty = no check.</summary>
    public IReadOnlySet<int> SyncOpcodes { get; private init; } = new HashSet<int>();
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
            DamageLayout = doc.DamageLayout ?? "",
            NicknameLayout = doc.NicknameLayout ?? "",
            DotLayout = doc.DotLayout ?? "",
            HpLayout = doc.HpLayout ?? "",
            BundleOpcode = doc.BundleOpcode,
            SyncOpcodes = new HashSet<int>(doc.SyncOpcodes ?? Array.Empty<int>()),
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
        public string? DamageLayout { get; set; }
        public string? NicknameLayout { get; set; }
        public string? DotLayout { get; set; }
        public string? HpLayout { get; set; }
        public int? BundleOpcode { get; set; }
        public int[]? SyncOpcodes { get; set; }
        public FrameLayout? Frame { get; set; }
        public Dictionary<string, int[]>? Opcodes { get; set; }
        public Dictionary<string, Dictionary<string, FieldSpec>>? Fields { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
