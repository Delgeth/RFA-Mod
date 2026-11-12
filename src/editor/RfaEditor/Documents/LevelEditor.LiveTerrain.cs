using System.IO;

namespace RfaEditor;

// The ground sculpted live: the running game's terrain changed in place, as you sculpt, without
// loading the level again (the console mod's editor_terrain: see console.cpp). The game keeps a
// zone's terrain file and its sub-zones in memory laid out as the files are, and walks on and draws
// the ground from there; so every few frames, for each ground file that changed, the editor writes
// the bytes that differ from what the game has (the c-file's ranges: the same layout, so only values
// differ, never pointers), and for a sub-zone its ground mesh's vertices, as a patch in link\, and
// the game copies them in: the height field the player walks on at once, and a sub-zone a new vertex
// buffer for what is drawn. Not changed live: the far-away ground (seen from far off) and the roads
// and foliage following the ground; they follow once the level is loaded again. If the game can't
// (it answers "failed"), sculpting goes back to needing the level loaded again.
public partial class LevelEditor
{
    readonly Dictionary<string, byte[]> liveTerrainSent = new(StringComparer.OrdinalIgnoreCase);  // each ground file as the game has it now
    readonly HashSet<string> liveTerrainWaiting = new(StringComparer.OrdinalIgnoreCase);            // sent, not answered yet
    bool liveTerrainFailed;
    int liveTerrainPatch, liveTerrainTick;

    /// <summary>After a sculpting change: the game has it live, unless that has failed (then the level is loaded again).</summary>
    void SculptedLive()
    {
        if (liveTerrainFailed) LiveNeedsReload();
    }

    /// <summary>Every few frames while Live is on: the ground files that changed sent to the game, and its answers taken.</summary>
    void WatchLiveTerrain()
    {
        foreach (string file in liveTerrainWaiting.ToList())
        {
            if (gameLink.Answer($"terrain_{file}") is not string answer) continue;
            liveTerrainWaiting.Remove(file);
            if (answer.StartsWith("failed", StringComparison.OrdinalIgnoreCase) && !liveTerrainFailed)
            {
                liveTerrainFailed = true;
                context.Log($"Live: the game couldn't change {file} as it runs ({answer}); sculpting needs the level loaded again (Auto reload does it).");
                LiveNeedsReload();
            }
        }
        if (liveTerrainFailed || sculpt == null || ++liveTerrainTick % 8 != 0 || liveTerrainWaiting.Count > 0 || !sculptZones.Any(z => z.Changed)) return;
        var lines = new List<string>();
        var written = new List<string>();
        var sending = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (TerrainSculpt ground in sculptZones.Where(z => z.Changed))
            {
                Dictionary<string, byte[]> files = ground.LiveFiles();
                foreach (var (name, bytes) in files)
                {
                    if (name.EndsWith(".gtmesh_pc", StringComparison.OrdinalIgnoreCase)) continue;  // (with its c-file, below)
                    byte[]? was = liveTerrainSent.GetValueOrDefault(name) ?? ground.ReadFile(name);
                    if (was == null || was.Length != bytes.Length) continue;
                    bool subzone = name.EndsWith(".ctmesh_pc", StringComparison.OrdinalIgnoreCase);
                    string dataName = subzone ? name[..^".ctmesh_pc".Length] + ".gtmesh_pc" : "";
                    byte[]? data = subzone ? files.GetValueOrDefault(dataName) : null;
                    byte[]? dataWas = subzone ? liveTerrainSent.GetValueOrDefault(dataName) ?? ground.ReadFile(dataName) : null;
                    if (TerrainPatch(name, was, bytes, data, dataWas) is not byte[] patch) continue;
                    string patchName = $"terrain_patch_{++liveTerrainPatch}.bin";
                    File.WriteAllBytes(gameLink.FileIn(patchName), patch);
                    written.Add(gameLink.FileIn(patchName));
                    lines.Add($"editor_terrain {name} {patchName}");
                    sending[name] = bytes;
                    if (data != null) sending[dataName] = data;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            liveTerrainFailed = true;
            context.Log($"Live: the sculpted ground couldn't be sent to the game ({ex.Message}); sculpting needs the level loaded again.");
            LiveNeedsReload();
            return;
        }
        if (lines.Count == 0) return;
        if (!gameLink.TrySend(lines))  // (busy: sent again in a moment)
        {
            foreach (string path in written) try { File.Delete(path); } catch (IOException) { }
            return;
        }
        foreach (var (name, bytes) in sending)
        {
            liveTerrainSent[name] = bytes;
            if (!name.EndsWith(".gtmesh_pc", StringComparison.OrdinalIgnoreCase)) liveTerrainWaiting.Add(name);
        }
    }

    /// <summary>
    /// A patch for the game (see console.cpp's TerrainPatch): the c-file's bytes that differ, as runs;
    /// for a sub-zone its ground mesh's vertices too. Null if nothing differs.
    /// </summary>
    static byte[]? TerrainPatch(string name, byte[] was, byte[] now, byte[]? data, byte[]? dataWas)
    {
        bool subzone = data != null;
        var ranges = new List<(int Offset, int Length)>();
        for (int i = 0; i < now.Length;)
        {
            if (now[i] == was[i]) { i++; continue; }
            int start = i;
            while (i < now.Length && now[i] != was[i]) i++;
            ranges.Add((start, i - start));
        }
        int dataAt = 0, vertexOffset = 0, vertexCount = 0, vertexSize = 0;
        bool vertices = false;
        if (subzone)
        {
            if (RfaFormats.Meshes.TerrainSubzone.LiveLayout(now) is not { } layout) return null;
            (dataAt, vertexOffset, vertexCount, vertexSize) = layout;
            int length = vertexCount * vertexSize;
            if (vertexOffset < 0 || vertexOffset + length > data!.Length) return null;
            vertices = dataWas == null || dataWas.Length != data.Length || !data.AsSpan(vertexOffset, length).SequenceEqual(dataWas.AsSpan(vertexOffset, length));
        }
        if (ranges.Count == 0 && !vertices) return null;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(0x50544652u);  // "RFTP"
        w.Write(1);
        w.Write(subzone ? 1 : 0);
        w.Write(dataAt);
        w.Write(now.Length);
        w.Write(ranges.Count);
        foreach (var (offset, length) in ranges)
        {
            w.Write(offset);
            w.Write(length);
            w.Write(now, offset, length);
        }
        if (subzone)
        {
            w.Write(vertices ? vertexCount : 0);
            w.Write(vertexSize);
            if (vertices) w.Write(data!, vertexOffset, vertexCount * vertexSize);
        }
        w.Flush();
        return stream.ToArray();
    }
}
