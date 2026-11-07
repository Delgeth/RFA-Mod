using System.Globalization;
using System.Numerics;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's EDF convoys, for the guerrilla_vehicles mod's convoys (Guerrilla's convoy code, which Armageddon
/// deleted, ported there): <c>mods\guerrilla_vehicles\convoys.txt</c>. A convoy starts at its end point
/// (object_convoy_end_point in the p_ zones: its convoys.xtbl row, its home district) and drives the route
/// of the patrol under it (in the convoy's activity container, activities.vpp_pc &lt;name&gt;.str2_pc: the
/// patrol's navpoints spread over several layer files). The route is built as Guerrilla's
/// object_convoy::build_route does it: the navpoints walked from the patrol's first (at a junction its
/// preferred start branch, else a start branch, else the link that goes on straightest), each moved to
/// the nearest road point within 60 m, and the roads between each two routed (the shortest way over the
/// roads' points and connections); the road points in order, none closer than 1 m to the last, at most
/// 512. The file:
///   convoy &lt;name&gt; &lt;Destroy|Capture&gt; &lt;difficulty&gt; &lt;normal speed&gt; &lt;evade speed&gt; &lt;start hour&gt; &lt;end hour&gt; &lt;x&gt; &lt;y&gt; &lt;z&gt; &lt;district&gt;
///   member &lt;name&gt; &lt;Target|Defender|Attack&gt; &lt;people&gt; &lt;its vehicles built, by commas&gt;   (in order: the first leads)
///   route &lt;name&gt; &lt;x,y,z&gt; &lt;x,y,z&gt; ...
/// A convoy any of whose members' vehicle groups has none of the vehicles built is left out.
/// </summary>
public static class GuerrillaConvoys
{
    public const string File = "convoys.txt";
    const float RoadSnap = 60, MinStep = 1;
    const int MostPoints = 512;

    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static Vector3 V(byte[] v, int at = 0) => new(BitConverter.ToSingle(v, at), BitConverter.ToSingle(v, at + 4), BitConverter.ToSingle(v, at + 8));
    static uint[] Links(GuerrillaZoneObject o) => o.Value("obj_links") is { } l ? Enumerable.Range(0, l.Length / 4).Select(i => BitConverter.ToUInt32(l, i * 4)).Distinct().ToArray() : [];

    /// <summary>The roads (points, connections) of Guerrilla's main zones, by handle.</summary>
    sealed class Roads
    {
        readonly Dictionary<uint, Vector3[]> points = new();
        readonly Dictionary<(uint Road, int Point), List<((uint, int) To, float Length)>> next = new();

        public Roads(Packfile zones)
        {
            uint road = NameHashes.ClassHash("object_path_road");
            var connections = new List<(uint From, int FromPoint, uint To, int ToPoint)>();
            foreach (var entry in zones.Entries.Where(e => e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase) && !e.Name.StartsWith("p_", StringComparison.OrdinalIgnoreCase)))
                foreach (var o in GuerrillaZones.Objects(zones.Read(entry)).Where(o => o.ClassHash == road))
                {
                    if (o.Value("path_road_struct") is not { Length: >= 16 } shape || o.Value("path_road_data") is not { } data) continue;
                    int count = BitConverter.ToInt32(shape, 0), at = BitConverter.ToInt32(shape, 4), links = BitConverter.ToInt32(shape, 8), linksAt = BitConverter.ToInt32(shape, 12);
                    if (count <= 0 || at + count * 28 > data.Length) continue;
                    points[o.Handle] = Enumerable.Range(0, count).Select(i => V(data, at + i * 28)).ToArray();
                    // A connection: {the other road's handle, this road's point, the other road's point}.
                    for (int i = 0; i < links && linksAt + i * 8 + 8 <= data.Length; i++)
                    {
                        int c = linksAt + i * 8;
                        connections.Add((o.Handle, BitConverter.ToUInt16(data, c + 4), BitConverter.ToUInt32(data, c), BitConverter.ToUInt16(data, c + 6)));
                    }
                }
            void Join((uint, int) a, (uint, int) b)
            {
                float length = Vector3.Distance(At(a), At(b));
                (next.TryGetValue(a, out var la) ? la : next[a] = []).Add((b, length));
                (next.TryGetValue(b, out var lb) ? lb : next[b] = []).Add((a, length));
            }
            foreach (var (handle, p) in points)
                for (int i = 1; i < p.Length; i++) Join((handle, i - 1), (handle, i));
            foreach (var (from, fromPoint, to, toPoint) in connections)
                if (points.TryGetValue(to, out var target) && fromPoint < points[from].Length && toPoint < target.Length) Join((from, fromPoint), (to, toPoint));
        }

        public int Count => points.Count;
        public Vector3 At((uint Road, int Point) k) => points[k.Road][k.Point];

        public (uint, int)? Nearest(Vector3 p, out float distance)
        {
            distance = float.MaxValue;
            (uint, int)? best = null;
            foreach (var (handle, road) in points)
                for (int i = 0; i < road.Length; i++)
                {
                    float d = Vector3.Distance(p, road[i]);
                    if (d < distance) { distance = d; best = (handle, i); }
                }
            return best;
        }

        /// <summary>The shortest way over the roads from <paramref name="a"/> to <paramref name="b"/> (A*), or null.</summary>
        public List<(uint, int)>? Way((uint, int) a, (uint, int) b)
        {
            var cost = new Dictionary<(uint, int), float> { [a] = 0 };
            var from = new Dictionary<(uint, int), (uint, int)>();
            var open = new PriorityQueue<(uint, int), float>();
            var done = new HashSet<(uint, int)>();
            open.Enqueue(a, Vector3.Distance(At(a), At(b)));
            while (open.Count > 0)
            {
                var u = open.Dequeue();
                if (!done.Add(u)) continue;
                if (u == b)
                {
                    var way = new List<(uint, int)> { b };
                    while (way[^1] != a) way.Add(from[way[^1]]);
                    way.Reverse();
                    return way;
                }
                if (!next.TryGetValue(u, out var steps)) continue;
                foreach (var (v, length) in steps)
                {
                    float c = cost[u] + length;
                    if (cost.TryGetValue(v, out float old) && c >= old) continue;
                    cost[v] = c;
                    from[v] = u;
                    open.Enqueue(v, c + Vector3.Distance(At(v), At(b)));
                }
            }
            return null;
        }
    }

    /// <summary>Writes the file into <paramref name="vehiclesModFolder"/>; <paramref name="groups"/> are the vehicle spawn groups written (Guerrilla's names → the vehicles built); a line for the log.</summary>
    public static string Write(string guerrillaCache, Packfile guerrillaTables, IReadOnlyDictionary<string, List<string>> groups, string vehiclesModFolder)
    {
        XtblFile Table(string name) => XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find(name) ?? throw new InvalidDataException($"no {name} in Guerrilla's misc.vpp_pc")));
        var squadSizes = Table("squads.xtbl").Entries.GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => int.TryParse(g.First().ValueOf("member_count")?.Trim(), out int n) ? n : 1, StringComparer.OrdinalIgnoreCase);
        var definitions = Table("convoys.xtbl").Entries.GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var zones = Packfile.Open(Path.Combine(guerrillaCache, "zonescript_terr01.vpp_pc"));
        uint endClass = NameHashes.ClassHash("object_convoy_end_point"), patrolClass = NameHashes.ClassHash("object_patrol"), navClass = NameHashes.ClassHash("navpoint");
        var ends = new List<GuerrillaZoneObject>();
        foreach (var entry in zones.Entries.Where(e => e.Name.StartsWith("p_", StringComparison.OrdinalIgnoreCase) && e.Name.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase)))
            ends.AddRange(GuerrillaZones.Objects(zones.Read(entry)).Where(o => o.ClassHash == endClass));
        var roads = new Roads(zones);
        var activities = Packfile.Open(Path.Combine(guerrillaCache, "activities.vpp_pc"));

        var lines = new List<string> { "# Guerrilla's convoys, for the guerrilla_vehicles mod (written by editor\\GuerrillaBuilder.exe with the vehicles)" };
        var written = new List<string>();
        var problems = new List<string>();
        foreach (var end in ends.OrderBy(e => e.Text("convoy_type"), StringComparer.OrdinalIgnoreCase))
        {
            string name = end.Text("convoy_type")?.Trim() ?? "";
            if (!definitions.TryGetValue(name, out var row)) { problems.Add($"{name}: no convoys.xtbl row"); continue; }
            // Its members, in order (the first leads).
            var members = new List<string>();
            string? missing = null;
            foreach (var squad in row.Element("MemberSquads")?.Elements ?? [])
            {
                string group = squad.ValueOf("VehicleGroup")?.Trim() ?? "", role = squad.ValueOf("Role")?.Trim() ?? "";
                if (role.Equals("On Foot Escort", StringComparison.OrdinalIgnoreCase)) continue;  // Guerrilla's data has none
                if (!groups.TryGetValue(group, out var vehicles) || vehicles.Count == 0) { missing = group; break; }
                string ours = role.StartsWith("Break", StringComparison.OrdinalIgnoreCase) ? "Attack" : role.Equals("Target", StringComparison.OrdinalIgnoreCase) ? "Target" : "Defender";
                int people = squadSizes.GetValueOrDefault(squad.ValueOf("Squad")?.Trim() ?? "", 1);
                members.Add($"member {name} {ours} {people} {string.Join(",", vehicles)}");
            }
            if (missing != null) { problems.Add($"{name}: none of the vehicles of its group {missing} built"); continue; }
            if (members.Count == 0) { problems.Add($"{name}: no members"); continue; }

            // Its route.
            if (activities.Find(name + ".str2_pc") is not { } container) { problems.Add($"{name}: no {name}.str2_pc in activities.vpp_pc"); continue; }
            var pack = activities.OpenContainer(container);
            var objects = new Dictionary<uint, GuerrillaZoneObject>();
            foreach (var layer in pack.Entries.Where(e => e.Name.EndsWith(".layer_pc", StringComparison.OrdinalIgnoreCase)))
                foreach (var o in GuerrillaZones.LayerObjects(pack.Read(layer))) objects[o.Handle] = o;
            var patrol = objects.Values.FirstOrDefault(o => o.ClassHash == patrolClass && o.Parent == end.Handle);
            if (patrol?.Value("patrol_start") is not { Length: >= 4 } startValue) { problems.Add($"{name}: no patrol under its end point"); continue; }
            var chain = Walk(objects, navClass, BitConverter.ToUInt32(startValue, 0), out string? walkNote);
            if (walkNote != null) problems.Add($"{name}: {walkNote}");
            var nodes = new List<(uint, int)>();
            foreach (var nav in chain)
                if (nav.Value("just_pos") is { Length: >= 12 } p && roads.Nearest(V(p), out float d) is { } k && d <= RoadSnap) nodes.Add(k);
            var route = new List<Vector3>();
            for (int i = 1; i < nodes.Count; i++)
                foreach (var k in roads.Way(nodes[i - 1], nodes[i]) ?? [])
                    if (route.Count == 0 || Vector3.Distance(route[^1], roads.At(k)) >= MinStep) route.Add(roads.At(k));
            if (route.Count < 2) { problems.Add($"{name}: no route along the roads"); continue; }
            if (route.Count > MostPoints) route.RemoveRange(MostPoints, route.Count - MostPoints);

            var tod = row.Element("tod_info");
            string Hour(string field) => int.TryParse(tod?.ValueOf(field)?.Trim(), out int h) ? h.ToString(CultureInfo.InvariantCulture) : "0";
            float Speed(string field) => Math.Clamp(float.TryParse(row.ValueOf(field)?.Trim(), CultureInfo.InvariantCulture, out float s) ? s : 8, 1, 30);
            var at = end.Value("op") is { Length: >= 12 } op ? V(op) : (end.BoundsMin + end.BoundsMax) / 2;
            string type = row.ValueOf("Type")?.Trim().Equals("Capture", StringComparison.OrdinalIgnoreCase) == true ? "Capture" : "Destroy";
            lines.Add($"convoy {name} {type} {row.ValueOf("difficulty")?.Trim() ?? "1"} {F(Speed("NormalSpeed"))} {F(Speed("EvadeSpeed"))} {Hour("spawn_start_time")} {Hour("spawn_end_time")} " +
                      $"{F(at.X)} {F(at.Y)} {F(at.Z)} {(end.Text("home_district")?.Trim() is { Length: > 0 } district ? district.Replace(' ', '_') : "-")}");
            lines.AddRange(members);
            lines.Add($"route {name} " + string.Join(" ", route.Select(p => $"{F(p.X)},{F(p.Y)},{F(p.Z)}")));
            float length = 0;
            for (int i = 1; i < route.Count; i++) length += Vector3.Distance(route[i - 1], route[i]);
            written.Add($"{name} ({members.Count} vehicles, {length / 1000:0.0} km)");
        }
        Directory.CreateDirectory(vehiclesModFolder);
        System.IO.File.WriteAllLines(Path.Combine(vehiclesModFolder, File), lines);
        return $"Convoys: {written.Count} in mods\\guerrilla_vehicles\\{File} ({string.Join(", ", written)}; {roads.Count} roads)" +
               (problems.Count > 0 ? $"; {string.Join("; ", problems)}" : "");
    }

    /// <summary>
    /// A patrol's navpoints in order from its first, as Guerrilla's build_patrol_point_list walks them: on through
    /// a navpoint's other link; at a junction (three links or more) its preferred start branch (navpoint_data
    /// +0x18 = 2), else a start branch (1), else (Guerrilla would give up: <paramref name="note"/> says so) the
    /// link that goes on straightest; it stops at the end, back at the start or where only end branches (3) go on.
    /// </summary>
    static List<GuerrillaZoneObject> Walk(IReadOnlyDictionary<uint, GuerrillaZoneObject> objects, uint navClass, uint start, out string? note)
    {
        note = null;
        int Branch(uint h) => objects.TryGetValue(h, out var o) && o.Value("navpoint_data") is { Length: >= 0x1C } d ? BitConverter.ToInt32(d, 0x18) : 0;
        Vector3 Pos(uint h) => objects[h].Value("just_pos") is { Length: >= 12 } p ? V(p) : objects[h].BoundsMin;
        var chain = new List<GuerrillaZoneObject>();
        var seen = new HashSet<uint>();
        uint previous = uint.MaxValue, current = start;
        while (objects.TryGetValue(current, out var nav) && nav.ClassHash == navClass && seen.Add(current) && chain.Count < MostPoints)
        {
            chain.Add(nav);
            var onward = Links(nav).Where(l => l != previous && objects.ContainsKey(l)).ToArray();
            uint next = uint.MaxValue;
            if (onward.Length == 1) next = onward[0];
            else if (onward.Length >= 2)
            {
                if (onward.All(l => Branch(l) == 3)) break;
                var starts = onward.Where(l => Branch(l) is 1 or 2).ToArray();
                if (starts.Length > 0) next = starts.FirstOrDefault(l => Branch(l) == 2, starts[0]);
                else
                {
                    note ??= $"junction {current:X8} has no branch marked (went on straightest)";
                    var heading = previous != uint.MaxValue && objects.ContainsKey(previous) ? Vector3.Normalize(Pos(current) - Pos(previous)) : Vector3.Zero;
                    next = onward.Where(l => !seen.Contains(l) && Branch(l) != 3)
                                 .OrderByDescending(l => Vector3.Dot(heading, Vector3.Normalize(Pos(l) - Pos(current)))).DefaultIfEmpty(uint.MaxValue).First();
                }
            }
            if (next == start || next == uint.MaxValue) break;
            previous = current;
            current = next;
        }
        return chain;
    }
}
