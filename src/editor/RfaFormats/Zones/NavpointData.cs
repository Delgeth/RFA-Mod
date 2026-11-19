namespace RfaFormats.Zones;

/// <summary>
/// A navpoint's "navpoint_data" (Volition's tools packed the schema's COMBINED_EXPORT settings of a
/// navpoint into it): version 3 (Guerrilla's, 28 bytes) or 4 (Armageddon's, 40: four more bytes and
/// the people's navigation cell it stands on, a u64: the cell's zone, column × 16 + row, × 2^24, its
/// layer × 2^15, its number; see GuerrillaPopulation). Armageddon's reader takes versions 1 to 4.
///   +0  version
///   +4  type: the schema's navpoint_type, by its place in the list (0 GROUND, 1 PATROL, 2 PEDESTRIAN,
///       ... 10 PATH, 11 TRANSITION, 12 LADDER TRANSITION, 13 LADDER, 14 STAIRS, ...)
///   +12 outer_radius (the game's: 0.05 to 4)
///   +16 navpoint_speed_limit (10 in every one of the game's)
/// The other bytes are kept as they are: in every one of the game's levels' navpoints +8 is
/// 00 BF 4E 00 and +24 zero; +20 differs a little from navpoint to navpoint (what Volition's tools
/// left there). A new one has them zero.
/// </summary>
public sealed class NavpointData
{
    /// <summary>
    /// The navpoint types from the game's _navpoint_type enum (RFG PDB), in their correct index order.
    /// Used as fallback when the schema's navpoint_type enum choices aren't available.
    /// </summary>
    public static readonly string[] GameTypes =
    [
        "GROUND",                    // 0
        "PATROL",                    // 1
        "PEDESTRIAN",                // 2
        "FLOATING",                  // 3
        "SUPPLY DROPOFF",            // 4
        "ROAD",                      // 5
        "ROAD JUMP",                 // 6
        "ROAD JUMP DESTINATION",     // 7
        "ROAD JUMP START",           // 8
        "ROAD CHECKPOINT",           // 9
        "PATH",                      // 10
        "TRANSITION",                // 11
        "LADDER TRANSITION",         // 12
        "LADDER",                    // 13
        "STAIRS",                    // 14
        "TRANSITION JUMP",           // 15
        "TRANSITION LEAP OVER",      // 16
        "WAS TRANSITION",            // 17
        "HOLE WALL",                 // 18
        "HOLE FLOOR",                // 19
        "HOLE DROP POINT",           // 20
        "TRANSITION HOLE",           // 21
        "ROAD BRIDGE",               // 22
        "PATH DOOR",                 // 23
        "TRANSITION DOOR",           // 24
    ];

    /// <summary>Number of navpoint types in the game enum (NUM_NAVPOINT_TYPES = 25).</summary>
    public const int NumTypes = 25;

    /// <summary>The default type for new navpoints (PATH = 10).</summary>
    public const int PathType = 10;

    /// <summary>
    /// Gets the type names to use, preferring the schema's navpoint_type enum choices if available,
    /// otherwise falling back to the game's enum.
    /// </summary>
    public static string[] GetTypes(SchemaProperty? navpointTypeProperty = null)
    {
        if (navpointTypeProperty?.Choices is { Count: > 0 } choices)
            return [.. choices];
        return GameTypes;
    }

    byte[] bytes;

    NavpointData(byte[] bytes) => this.bytes = bytes;

    /// <summary>The data as an object has it; null if it is too short to be navpoint data.</summary>
    public static NavpointData? Read(byte[] value) => value.Length >= 20 ? new NavpointData((byte[])value.Clone()) : null;

    /// <summary>New data (version 3) for a navpoint of a type.</summary>
    public static NavpointData New(int type = PathType, float radius = 1.35f, float speedLimit = 10)
    {
        var data = new NavpointData(new byte[28]) { Version = 3, Type = type, Radius = radius, SpeedLimit = speedLimit };
        return data;
    }

    /// <summary>New data (version 4, Armageddon) for a navpoint of a type with a navigation cell.</summary>
    public static NavpointData NewV4(int type = PathType, float radius = 1.35f, float speedLimit = 10, ulong cell = 0)
    {
        var data = new NavpointData(new byte[40]) { Version = 4, Type = type, Radius = radius, SpeedLimit = speedLimit };
        if (cell != 0)
            BitConverter.TryWriteBytes(data.bytes.AsSpan(32), cell);
        return data;
    }

    public int Version { get => BitConverter.ToInt32(bytes, 0); private set => BitConverter.TryWriteBytes(bytes.AsSpan(0), value); }
    public int Type { get => BitConverter.ToInt32(bytes, 4); set => BitConverter.TryWriteBytes(bytes.AsSpan(4), value); }
    public float Radius { get => BitConverter.ToSingle(bytes, 12); set => BitConverter.TryWriteBytes(bytes.AsSpan(12), value); }
    public float SpeedLimit { get => BitConverter.ToSingle(bytes, 16); set => BitConverter.TryWriteBytes(bytes.AsSpan(16), value); }

    /// <summary>The navigation cell it stands on (version 4); null for older data.</summary>
    public ulong? Cell => Version >= 4 && bytes.Length >= 40 ? BitConverter.ToUInt64(bytes, 32) : null;

    /// <summary>Sets the navigation cell (version 4 only).</summary>
    public void SetCell(ulong cell)
    {
        if (Version < 4) Version = 4;
        if (bytes.Length < 40) Array.Resize(ref bytes, 40);
        BitConverter.TryWriteBytes(bytes.AsSpan(32), cell);
    }

    /// <summary>Gets the type name using the provided types array (or GameTypes if null).</summary>
    public string GetTypeName(string[]? types = null)
    {
        types ??= GameTypes;
        return Type >= 0 && Type < types.Length ? types[Type] : $"type {Type}";
    }

    public byte[] ToBytes() => (byte[])bytes.Clone();
}