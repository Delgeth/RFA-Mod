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
    /// <summary>The schema's navpoint types, in their order (the number the data keeps).</summary>
    public static readonly string[] Types =
    [
        "GROUND", "PATROL", "PEDESTRIAN", "FLOATING", "SUPPLY DROPOFF", "ROAD", "ROAD JUMP", "ROAD JUMP DEST", "ROAD JUMP START", "ROAD CHECKPOINT",
        "PATH", "TRANSITION", "LADDER TRANSITION", "LADDER", "STAIRS", "TRANSITION JUMP", "TRANSITION LEAP OVER", "ROAD BRIDGE", "PATH DOOR", "TRANSITION DOOR",
    ];

    public const int PathType = 10;

    readonly byte[] bytes;

    NavpointData(byte[] bytes) => this.bytes = bytes;

    /// <summary>The data as an object has it; null if it is too short to be navpoint data.</summary>
    public static NavpointData? Read(byte[] value) => value.Length >= 20 ? new NavpointData((byte[])value.Clone()) : null;

    /// <summary>New data (version 3) for a navpoint of a type.</summary>
    public static NavpointData New(int type = PathType, float radius = 1.35f, float speedLimit = 10)
    {
        var data = new NavpointData(new byte[28]) { Version = 3, Type = type, Radius = radius, SpeedLimit = speedLimit };
        return data;
    }

    public int Version { get => BitConverter.ToInt32(bytes, 0); private init => BitConverter.TryWriteBytes(bytes.AsSpan(0), value); }
    public int Type { get => BitConverter.ToInt32(bytes, 4); set => BitConverter.TryWriteBytes(bytes.AsSpan(4), value); }
    public float Radius { get => BitConverter.ToSingle(bytes, 12); set => BitConverter.TryWriteBytes(bytes.AsSpan(12), value); }
    public float SpeedLimit { get => BitConverter.ToSingle(bytes, 16); set => BitConverter.TryWriteBytes(bytes.AsSpan(16), value); }

    /// <summary>The navigation cell it stands on (version 4); null for older data.</summary>
    public ulong? Cell => Version >= 4 && bytes.Length >= 40 ? BitConverter.ToUInt64(bytes, 32) : null;

    public string TypeName => Type >= 0 && Type < Types.Length ? Types[Type] : $"type {Type}";

    public byte[] ToBytes() => (byte[])bytes.Clone();
}
