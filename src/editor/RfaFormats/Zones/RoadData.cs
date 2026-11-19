using System.Numerics;

namespace RfaFormats.Zones;

/// <summary>
/// Data structures for road/spline editing, reverse-engineered from RFG.exe (with PDB symbols).
/// These match the game's internal structures for object_path_road and related types.
/// </summary>

/// <summary>
/// A road spline's data: an array of points and connections between them.
/// Size: 16 bytes (from RFG's road_spline_data)
/// </summary>
public sealed class RoadSplineData
{
    public int NumPoints { get; set; }
    public RoadSplinePoint[] Points { get; set; } = [];
    public int NumConnections { get; set; }
    public RoadSplineConnection[] Connections { get; set; } = [];

    public static RoadSplineData Read(byte[] data)
    {
        // The data format in zone files is different from the in-memory structure.
        // In zone files, path_road_struct (16 bytes) contains:
        //   +0  num_points (int)
        //   +4  points_offset (int, index into path_road_data)
        //   +8  num_connections (int)
        //  +12  connections_offset (int, index into path_road_data)
        // The actual points and connections are in path_road_data.
        if (data.Length < 16) return new RoadSplineData();

        var result = new RoadSplineData
        {
            NumPoints = BitConverter.ToInt32(data, 0),
            NumConnections = BitConverter.ToInt32(data, 8),
        };
        // Points and connections would be read from path_road_data separately
        return result;
    }

    public byte[] ToStructBytes()
    {
        var bytes = new byte[16];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), NumPoints);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), 0); // offset placeholder
        BitConverter.TryWriteBytes(bytes.AsSpan(8), NumConnections);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), 0); // offset placeholder
        return bytes;
    }
}

/// <summary>
/// A single point on a road spline.
/// Size: 28 bytes (from RFG's road_spline_point)
///   +0  pos (Vector3) - world position
///  +12  pt_handle (Vector3) - curve handle (tangent * scale)
///  +24  width (float) - road width at this point
/// </summary>
public sealed class RoadSplinePoint
{
    public Vector3 Pos { get; set; }
    public Vector3 PtHandle { get; set; }
    public float Width { get; set; }

    public static RoadSplinePoint Read(byte[] data, int offset = 0)
    {
        if (data.Length < offset + 28) return new RoadSplinePoint();
        return new RoadSplinePoint
        {
            Pos = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8)),
            PtHandle = new Vector3(BitConverter.ToSingle(data, offset + 12), BitConverter.ToSingle(data, offset + 16), BitConverter.ToSingle(data, offset + 20)),
            Width = BitConverter.ToSingle(data, offset + 24),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[28];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), Pos.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), Pos.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), Pos.Z);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), PtHandle.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(16), PtHandle.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), PtHandle.Z);
        BitConverter.TryWriteBytes(bytes.AsSpan(24), Width);
        return bytes;
    }
}

/// <summary>
/// A connection between two points on (potentially different) road splines.
/// Size: 8 bytes (from RFG's road_spline_connection)
///   +0  handle (uint) - the other road's handle
///   +4  from_pt (ushort) - point index on this road
///   +6  to_pt (ushort) - point index on the other road
/// </summary>
public sealed class RoadSplineConnection
{
    public uint Handle { get; set; }
    public ushort FromPoint { get; set; }
    public ushort ToPoint { get; set; }

    public static RoadSplineConnection Read(byte[] data, int offset = 0)
    {
        if (data.Length < offset + 8) return new RoadSplineConnection();
        return new RoadSplineConnection
        {
            Handle = BitConverter.ToUInt32(data, offset),
            FromPoint = BitConverter.ToUInt16(data, offset + 4),
            ToPoint = BitConverter.ToUInt16(data, offset + 6),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), Handle);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), FromPoint);
        BitConverter.TryWriteBytes(bytes.AsSpan(6), ToPoint);
        return bytes;
    }
}

/// <summary>
/// Information about a road segment for pathfinding/vehicle AI.
/// Size: 8 bytes (from RFG's road_segment_info)
///   +0  road_handle (uint)
///   +4  start_index (ushort)
///   +6  end_index (ushort)
/// </summary>
public sealed class RoadSegmentInfo
{
    public uint RoadHandle { get; set; }
    public ushort StartIndex { get; set; }
    public ushort EndIndex { get; set; }

    public static RoadSegmentInfo Read(byte[] data, int offset = 0)
    {
        if (data.Length < offset + 8) return new RoadSegmentInfo();
        return new RoadSegmentInfo
        {
            RoadHandle = BitConverter.ToUInt32(data, offset),
            StartIndex = BitConverter.ToUInt16(data, offset + 4),
            EndIndex = BitConverter.ToUInt16(data, offset + 6),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), RoadHandle);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), StartIndex);
        BitConverter.TryWriteBytes(bytes.AsSpan(6), EndIndex);
        return bytes;
    }
}

/// <summary>
/// Identifier for a specific point on a specific road spline.
/// Size: 8 bytes (from RFG's road_spline_identifier)
///   +0  road_spline (object_path_road*) - in game memory; in zone files this is a handle
///   +4  point (ushort)
/// </summary>
public sealed class RoadSplineIdentifier
{
    public uint RoadHandle { get; set; } // In zone files, this is the handle of the object_path_road
    public ushort Point { get; set; }

    public static RoadSplineIdentifier Read(byte[] data, int offset = 0)
    {
        if (data.Length < offset + 8) return new RoadSplineIdentifier();
        return new RoadSplineIdentifier
        {
            RoadHandle = BitConverter.ToUInt32(data, offset),
            Point = BitConverter.ToUInt16(data, offset + 4),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), RoadHandle);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), Point);
        return bytes;
    }
}

/// <summary>
/// Road flags for object_path_road (from RFG's path_road_flags / road_flags).
/// These control ambient traffic, one-way, special roads, etc.
/// </summary>
[Flags]
public enum RoadFlags : uint
{
    None = 0,
    AmbientTraffic = 1 << 0,      // Ambient traffic uses this road
    OneWay = 1 << 1,              // One-way road
    Special = 1 << 2,             // Special road (e.g. highway)
    NoReinforcements = 1 << 3,    // No reinforcements on this road
    GpsOnly = 1 << 4,             // GPS-only (for mission routing)
}

/// <summary>
/// Complete road data from an object_path_road zone object.
/// Combines path_road_struct (header) and path_road_data (points + connections).
/// </summary>
public sealed class RoadData
{
    public RoadSplineData SplineData { get; set; } = new();
    public RoadFlags Flags { get; set; }
    public float OuterRadius { get; set; }
    public float SpeedLimit { get; set; }
    public uint PrevHandle { get; set; } = 0xFFFFFFFF;
    public uint NextHandle { get; set; } = 0xFFFFFFFF;

    /// <summary>
    /// Reads road data from an object_path_road zone object's properties.
    /// </summary>
    public static RoadData? FromZoneObject(ZoneObject obj)
    {
        if (obj.ClassHash != NameHashes.ClassHash("object_path_road")) return null;

        var data = new RoadData();

        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("path_road_struct")) is { } structProp)
        {
            data.SplineData = RoadSplineData.Read(structProp.Value);
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("path_road_data")) is { } dataProp)
        {
            // Parse points and connections from path_road_data
            var bytes = dataProp.Value;
            int pointOffset = data.SplineData.NumPoints > 0 ? 0 : 0; // would come from struct offsets
            int connOffset = data.SplineData.NumPoints * 28; // each point is 28 bytes

            data.SplineData.Points = new RoadSplinePoint[data.SplineData.NumPoints];
            for (int i = 0; i < data.SplineData.NumPoints && pointOffset + 28 <= bytes.Length; i++, pointOffset += 28)
            {
                data.SplineData.Points[i] = RoadSplinePoint.Read(bytes, pointOffset);
            }

            data.SplineData.Connections = new RoadSplineConnection[data.SplineData.NumConnections];
            for (int i = 0; i < data.SplineData.NumConnections && connOffset + 8 <= bytes.Length; i++, connOffset += 8)
            {
                data.SplineData.Connections[i] = RoadSplineConnection.Read(bytes, connOffset);
            }
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("path_road_flags")) is { } flagsProp && flagsProp.Value.Length >= 4)
        {
            data.Flags = (RoadFlags)BitConverter.ToUInt32(flagsProp.Value, 0);
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("Outer_Radius")) is { } radiusProp && radiusProp.Value.Length >= 4)
        {
            data.OuterRadius = BitConverter.ToSingle(radiusProp.Value, 0);
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("speed_limit")) is { } speedProp && speedProp.Value.Length >= 4)
        {
            data.SpeedLimit = BitConverter.ToSingle(speedProp.Value, 0);
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("prev")) is { } prevProp && prevProp.Value.Length >= 4)
        {
            data.PrevHandle = BitConverter.ToUInt32(prevProp.Value, 0);
        }
        if (obj.Properties.FirstOrDefault(p => p.NameHash == NameHashes.PropertyHash("NEXT")) is { } nextProp && nextProp.Value.Length >= 4)
        {
            data.NextHandle = BitConverter.ToUInt32(nextProp.Value, 0);
        }

        return data;
    }

    /// <summary>
    /// Creates a new default road data for editing.
    /// </summary>
    public static RoadData New()
    {
        return new RoadData
        {
            SplineData = new RoadSplineData { NumPoints = 0, NumConnections = 0 },
            Flags = RoadFlags.AmbientTraffic,
            OuterRadius = 5f,
            SpeedLimit = 20f,
            PrevHandle = 0xFFFFFFFF,
            NextHandle = 0xFFFFFFFF,
        };
    }

    /// <summary>
    /// Reads road data from raw path_road_data bytes.
    /// </summary>
    public static RoadData? FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 16) return null;

        // path_road_data format: points (28 bytes each) followed by connections (8 bytes each)
        // The counts come from path_road_struct, so we need to handle it differently
        // For editing, we just return a placeholder that can be edited
        return new RoadData
        {
            SplineData = new RoadSplineData
            {
                NumPoints = 0,
                NumConnections = 0,
                Points = [],
                Connections = []
            }
        };
    }
}

/// <summary>
/// Data for an object_spline (catmull-rom spline for cameras, effects, etc.).
/// From RFG.exe's object_spline_data.
/// </summary>
public sealed class SplineData
{
    public enum SplineType : uint
    {
        Linear = 0,
        CatmullRom = 1,
        Bezier = 2,
    }

    public enum SplineFlags : uint
    {
        None = 0,
        Closed = 1 << 0,
        UniformSpeed = 1 << 1,
    }

    public SplineType Type { get; set; } = SplineType.CatmullRom;
    public SplineFlags Flags { get; set; }
    public int NumPoints { get; set; }
    public SplinePoint[] Points { get; set; } = [];
    public float Speed { get; set; } = 10f;
    public float Tension { get; set; } = 0.5f;

    public static SplineData? FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 4) return null;

        var data = new SplineData();

        // First 4 bytes: type (enum)
        data.Type = (SplineType)BitConverter.ToUInt32(bytes, 0);

        // Parse points if present
        // object_spline_data format varies, but typically:
        // +0: type (4 bytes)
        // +4: num_points (4 bytes)
        // +8: points (each is Vector3 + maybe handle/weight)
        if (bytes.Length >= 8)
        {
            data.NumPoints = BitConverter.ToInt32(bytes, 4);
        }

        return data;
    }

    public byte[] ToBytes()
    {
        // For now, just return the type
        var bytes = new byte[8];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), (uint)Type);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), NumPoints);
        return bytes;
    }
}

/// <summary>
/// A single point on a spline (catmull-rom/bezier).
/// </summary>
public sealed class SplinePoint
{
    public Vector3 Pos { get; set; }
    public Vector3 InHandle { get; set; }
    public Vector3 OutHandle { get; set; }
    public float Weight { get; set; } = 1f;

    public static SplinePoint Read(byte[] data, int offset = 0)
    {
        if (data.Length < offset + 36) return new SplinePoint();
        return new SplinePoint
        {
            Pos = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8)),
            InHandle = new Vector3(BitConverter.ToSingle(data, offset + 12), BitConverter.ToSingle(data, offset + 16), BitConverter.ToSingle(data, offset + 20)),
            OutHandle = new Vector3(BitConverter.ToSingle(data, offset + 24), BitConverter.ToSingle(data, offset + 28), BitConverter.ToSingle(data, offset + 32)),
            Weight = BitConverter.ToSingle(data, offset + 36),
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[40];
        BitConverter.TryWriteBytes(bytes.AsSpan(0), Pos.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), Pos.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(8), Pos.Z);
        BitConverter.TryWriteBytes(bytes.AsSpan(12), InHandle.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(16), InHandle.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), InHandle.Z);
        BitConverter.TryWriteBytes(bytes.AsSpan(24), OutHandle.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), OutHandle.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(32), OutHandle.Z);
        BitConverter.TryWriteBytes(bytes.AsSpan(36), Weight);
        return bytes;
    }
}