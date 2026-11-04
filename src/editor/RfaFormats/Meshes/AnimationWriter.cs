using System.Numerics;

namespace RfaFormats.Meshes;

/// <summary>
/// Writes an animation (.anim_pc, version 10) as <see cref="Animation"/> reads it: key counts and key
/// times as u16 (flags 0x80 and 0x02), a list of the bones it animates, the weight curves if any track
/// has one (flag 0x40), one block of keys per bone in the list's order (with scale keys, flag 0x20, if
/// any track has them), then the root track (its weight curve first with flag 0x40).
///
/// Rotations are written in runs of up to 16 keys. A run gives each axis a starting value (7 bits,
/// × 256) and a step (1, 2, 4 or 32); each key is 3 signed bytes, how many steps each axis moves from
/// the run's previous key, over 16,383; w is the rest of the length 1, its sign the run's. Each run
/// takes the smallest step its keys' moves fit in, so the error is half a step (1/32,766 to
/// 1/1,024 of the quaternion). A key whose w changes sign from the one before starts a run of its own
/// (a rotation and its negative are the same turn, so keys are first kept on one side, nearest the
/// key before). Positions are written in runs, each a starting point in 1/64 m and keys up to 127/4000
/// m from it, in 1/4000 m.
/// </summary>
public static class AnimationWriter
{
    const int WideTimes = 0x02, ScaleKeys = 0x20, WeightCurves = 0x40, WideCounts = 0x80;
    const int RotationScale = 16383;
    static readonly int[] Steps = [1, 2, 4, 32];

    /// <summary>
    /// An animation of <paramref name="frames"/> frames for a skeleton of <paramref name="skeletonBones"/>
    /// bones. <paramref name="header"/> is a source animation's first 0x30 bytes: its 0x08 bytes, its
    /// turn over the clip (0x0C, a quaternion) and its movement (0x1C) are kept. The tracks' bones must
    /// be under 256; the root track's bone is ignored.
    /// </summary>
    public static byte[] Write(ReadOnlySpan<byte> header, int frames, int skeletonBones, IReadOnlyList<AnimationTrack> tracks, AnimationTrack root)
    {
        if (header.Length < 0x30) throw new ArgumentException("the header is the first 0x30 bytes of an animation");
        if (tracks.Count > 255 || skeletonBones > 255) throw new ArgumentException("an animation holds at most 255 bones");
        bool scales = tracks.Append(root).Any(t => t.Scales.Count > 0), weights = tracks.Append(root).Any(t => t.Weights.Count > 0);
        var o = new List<byte>();
        o.AddRange(header[..0x30].ToArray());
        o[0] = (byte)'A'; o[1] = (byte)'N'; o[2] = (byte)'I'; o[3] = (byte)'M';
        o[4] = 10;
        o[5] = (byte)(WideTimes | WideCounts | (scales ? ScaleKeys : 0) | (weights ? WeightCurves : 0));
        Put16(o, 6, frames);
        o[0xA] = (byte)tracks.Count;
        o[0xB] = (byte)skeletonBones;
        Put32(o, 0x28, 0x30);
        foreach (var track in tracks)
        {
            if (track.Bone is < 0 or > 255) throw new ArgumentException($"bone {track.Bone} can't be listed");
            o.Add((byte)track.Bone);
        }
        if (weights) Weights(o, tracks);
        foreach (var track in tracks) Block(o, track, scales);
        Put32(o, 0x2C, o.Count);
        if (weights) Weights(o, [root]);
        Block(o, root, scales);
        return [.. o];
    }

    /// <summary>The weight curves of some tracks: a key count each (not aligned), then every value from a 4-byte boundary, then every time.</summary>
    static void Weights(List<byte> o, IReadOnlyList<AnimationTrack> tracks)
    {
        foreach (var track in tracks) Add16(o, track.Weights.Count);
        Align(o, 4);
        foreach (var key in tracks.SelectMany(t => t.Weights)) o.AddRange(BitConverter.GetBytes(key.Value));
        Times(o, tracks.SelectMany(t => t.Weights).Select(k => k.Frame));
    }

    static void Block(List<byte> o, AnimationTrack track, bool scales)
    {
        Align(o, 2);
        Add16(o, track.Rotations.Count);
        Add16(o, track.Positions.Count);
        if (scales) Add16(o, track.Scales.Count);
        Rotations(o, track.Rotations.Select(k => k.Value).ToList());
        Times(o, track.Rotations.Select(k => k.Frame));
        Align(o, 2);
        Positions(o, track.Positions.Select(k => k.Value).ToList());
        Times(o, track.Positions.Select(k => k.Frame));
        if (track.Scales.Count > 0)
        {
            Align(o, 4);
            foreach (var key in track.Scales)
            {
                o.AddRange(BitConverter.GetBytes(key.Value.X));
                o.AddRange(BitConverter.GetBytes(key.Value.Y));
                o.AddRange(BitConverter.GetBytes(key.Value.Z));
            }
            Times(o, track.Scales.Select(k => k.Frame));
        }
    }

    static void Times(List<byte> o, IEnumerable<int> frames)
    {
        Align(o, 2);
        foreach (int frame in frames) Add16(o, frame);
    }

    /// <summary>The runs, then the keys.</summary>
    static void Rotations(List<byte> o, List<Quaternion> rotations)
    {
        // As stored: the game's rotations turn the other way from System.Numerics' (see Animation),
        // so x, y and z are negated; and each key on the side of the one before.
        var keys = new List<(int[] Axes, bool NegativeW)>();
        Quaternion previous = Quaternion.Identity;
        for (int i = 0; i < rotations.Count; i++)
        {
            Quaternion q = Quaternion.Normalize(rotations[i]);
            if (i > 0 && Quaternion.Dot(q, previous) < 0) q = Quaternion.Negate(q);
            previous = q;
            keys.Add(([Quantize(-q.X), Quantize(-q.Y), Quantize(-q.Z)], q.W < 0));
        }
        var runs = new List<byte>();
        var bytes = new List<byte>();
        for (int first = 0; first < keys.Count;)
        {
            // The longest run from here (at most 16 keys, one sign of w) whose moves fit some step on each axis.
            int count = 1;
            while (count < 16 && first + count < keys.Count && keys[first + count].NegativeW == keys[first].NegativeW &&
                   RunFits(keys, first, count + 1))
                count++;
            var (header, steps) = Run(keys, first, count);
            runs.AddRange(header);
            bytes.AddRange(steps);
            first += count;
        }
        o.AddRange(runs);
        o.AddRange(bytes);
    }

    static int Quantize(float value) => Math.Clamp((int)MathF.Round(value * RotationScale), -RotationScale, RotationScale);

    static bool RunFits(List<(int[] Axes, bool NegativeW)> keys, int first, int count) =>
        Enumerable.Range(0, 3).All(axis => StepFor(keys, first, count, axis, out _) >= 0);

    /// <summary>The smallest step (its index in <see cref="Steps"/>) the axis's moves fit in for this run, or −1; and the run's start (7 bits).</summary>
    static int StepFor(List<(int[] Axes, bool NegativeW)> keys, int first, int count, int axis, out int start)
    {
        start = Math.Clamp((int)MathF.Round(keys[first].Axes[axis] / 256f), -64, 63);
        for (int s = 0; s < Steps.Length; s++)
        {
            int step = Steps[s], previous = 0;
            bool fits = true;
            for (int k = 0; k < count && fits; k++)
            {
                int target = (int)MathF.Round((keys[first + k].Axes[axis] - start * 256) / (float)step);
                int move = target - previous;
                if (move is < -128 or > 127 || Math.Abs(start * 256 + target * step) > short.MaxValue) fits = false;
                previous = target;
            }
            if (fits) return s;
        }
        return -1;
    }

    static (byte[] Header, byte[] Steps) Run(List<(int[] Axes, bool NegativeW)> keys, int first, int count)
    {
        var header = new byte[4];
        var moves = new byte[count * 3];
        header[3] = (byte)((count - 1) | (keys[first].NegativeW ? 0x10 : 0));
        for (int axis = 0; axis < 3; axis++)
        {
            int s = StepFor(keys, first, count, axis, out int start);
            if (s < 0) throw new InvalidDataException("a rotation key moves too far to write");
            int step = Steps[s];
            bool big = step >= 4, doubled = step is 2 or 32;
            header[axis] = (byte)((start & 0x7F) | (big ? 0x80 : 0));
            if (doubled) header[3] |= (byte)(0x80 >> axis);
            int previous = 0;
            for (int k = 0; k < count; k++)
            {
                int target = (int)MathF.Round((keys[first + k].Axes[axis] - start * 256) / (float)step);
                moves[k * 3 + axis] = unchecked((byte)(sbyte)(target - previous));
                previous = target;
            }
        }
        return (header, moves);
    }

    /// <summary>The runs (count, starting point in 1/64 m), then the keys (1/4000 m from their run's start).</summary>
    static void Positions(List<byte> o, List<Vector3> positions)
    {
        var runs = new List<(int Count, short[] Start)>();
        var bytes = new List<byte>();
        short[]? start = null;
        int inRun = 0;
        foreach (Vector3 p in positions)
        {
            float[] axes = [p.X, p.Y, p.Z];
            bool fits = start != null && inRun < ushort.MaxValue &&
                        Enumerable.Range(0, 3).All(a => Math.Abs(MathF.Round((axes[a] - start[a] / 64f) * 4000)) <= 127);
            if (!fits)
            {
                if (start != null) runs.Add((inRun, start));
                start = axes.Select(v => (short)Math.Clamp(MathF.Round(v * 64), short.MinValue, short.MaxValue)).ToArray();
                inRun = 0;
            }
            for (int a = 0; a < 3; a++) bytes.Add(unchecked((byte)(sbyte)MathF.Round((axes[a] - start![a] / 64f) * 4000)));
            inRun++;
        }
        if (start != null) runs.Add((inRun, start));
        foreach (var (count, s) in runs)
        {
            Add16(o, count);
            foreach (short v in s) Add16(o, v);
        }
        o.AddRange(bytes);
    }

    static void Align(List<byte> o, int boundary) { while (o.Count % boundary != 0) o.Add(0); }
    static void Add16(List<byte> o, int value) { o.Add((byte)value); o.Add((byte)(value >> 8)); }
    static void Put16(List<byte> o, int at, int value) { o[at] = (byte)value; o[at + 1] = (byte)(value >> 8); }
    static void Put32(List<byte> o, int at, int value) { for (int k = 0; k < 4; k++) o[at + k] = (byte)(value >> (8 * k)); }
}
