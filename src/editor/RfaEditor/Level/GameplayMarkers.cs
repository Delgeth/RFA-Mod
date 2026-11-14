using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Xml.Linq;

namespace RfaEditor;

/// <summary>
/// One of the Guerrilla port's gameplay markers: a place its mods act on (an activity's start, a raid
/// node, a kill region, a pedestrian's action spot, ...). Armageddon has no classes for them, so the
/// Guerrilla builder leaves them out of the zones and writes them into the mods' own files, where the
/// level editor reads them to show them (see <see cref="GameplayMarkers"/>).
/// </summary>
sealed class GameplayMarker
{
    /// <summary>What it is, as the Scene panel groups it ("Raid node", "Kill region", ...).</summary>
    public required string Kind { get; init; }
    /// <summary>Its family, which gives its colour and icon (see <see cref="GameplayMarkers.Style"/>).</summary>
    public required string Family { get; init; }
    /// <summary>The file it is read from, relative to the mod folder.</summary>
    public required string File { get; init; }
    public string Name { get; init; } = "";
    public uint? Handle { get; init; }
    public Vector3 Position { get; init; }
    /// <summary>How it is turned: rows right, up and forward, as the game's placements (identity if the file doesn't say).</summary>
    public Matrix4x4 Turn { get; init; } = Matrix4x4.Identity;
    /// <summary>The box it covers, in its own space (turned by <see cref="Turn"/>), if it is a region.</summary>
    public (Vector3 Min, Vector3 Max)? Box { get; init; }
    /// <summary>The area it covers on the map (x, z), if it is a polygon; and a wider one around it, if any.</summary>
    public IReadOnlyList<Vector2>? Polygon { get; init; }
    public IReadOnlyList<Vector2>? OuterPolygon { get; init; }
    public List<(string Name, string Value)> Properties { get; } = [];

    /// <summary>Its row in the Scene panel: its name, else its handle.</summary>
    public string Label => Name.Length > 0 ? Name : Handle is uint h ? h.ToString("X8") : Kind;
}

/// <summary>
/// Reads the gameplay markers the Guerrilla builder writes for the mods (see the plan of what each file
/// holds in the builder: RfaFormats\Guerrilla's GuerrillaCampaign, GuerrillaActions, GuerrillaKiosks, ...),
/// each file by the layout the builder gives it. A line that can't be read is left out and counted.
/// Read only: the builder writes these files anew on every build. Each file is read again when it changes.
/// </summary>
static class GameplayMarkers
{
    static readonly Dictionary<string, (DateTime Written, List<GameplayMarker> Markers, int Unread)> read = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every marker in the mods' files of a mod folder, and how many lines couldn't be read.</summary>
    public static (List<GameplayMarker> Markers, int Unread) All(string modFolder)
    {
        var all = new List<GameplayMarker>();
        int unread = 0;
        foreach (var (file, reader) in Files)
        {
            string path = Path.Combine(modFolder, file);
            if (!System.IO.File.Exists(path)) continue;
            DateTime written = System.IO.File.GetLastWriteTimeUtc(path);
            if (!read.TryGetValue(path, out var known) || known.Written != written)
            {
                var markers = new List<GameplayMarker>();
                int bad = 0;
                try { reader(path, file, markers, ref bad); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { bad++; }
                read[path] = known = (written, markers, bad);
            }
            all.AddRange(known.Markers);
            unread += known.Unread;
        }
        return (all, unread);
    }

    delegate void Reader(string path, string file, List<GameplayMarker> into, ref int unread);

    static readonly (string File, Reader Read)[] Files =
    [
        (@"mods\guerrilla_campaign\world\campaign_objects.xml", ReadCampaignObjects),
        (@"mods\guerrilla_campaign\kill_regions.txt", ReadKillRegions),
        (@"mods\guerrilla_campaign\restricted_areas.txt", ReadRestrictedAreas),
        (@"mods\guerrilla_campaign\edf_buildings.txt", ReadEdfBuildings),
        (@"mods\guerrilla_world\ambush_regions.txt", ReadAmbushRegions),
        (@"mods\guerrilla_world\story_nodes.txt", ReadStoryNodes),
        (@"mods\guerrilla_people\action_nodes.txt", ReadActionNodes),
        (@"mods\guerrilla_kiosks\kiosks.txt", ReadKiosks),
        (@"mods\guerrilla_save\respawns.txt", ReadRespawns),
        (@"mods\guerrilla_vehicles\safehouses.txt", ReadSafehouseParking),
        (@"mods\guerrilla_vehicles\convoys.txt", ReadConvoys),
        (@"mods\guerrilla_hud\safehouses.txt", ReadSafehouseStations),
    ];

    // ---- How each family looks ---------------------------------------------------------------

    /// <summary>A family's colour and icon.</summary>
    public static (Color Colour, string Icon) Style(string family) => family switch
    {
        "Campaign" => (Color.FromRgb(255, 170, 60), Icons.Mission),
        "Kill region" => (Color.FromRgb(255, 70, 70), Icons.Region),
        "Restricted area" => (Color.FromRgb(255, 120, 40), Icons.Locked),
        "Ambush region" => (Color.FromRgb(200, 110, 255), Icons.Region),
        "EDF building" => (Color.FromRgb(220, 60, 90), Icons.Model),
        "Story person" => (Color.FromRgb(255, 225, 90), Icons.Spawn),
        "Action node" => (Color.FromRgb(90, 160, 255), Icons.Path),
        "Kiosk" => (Color.FromRgb(70, 220, 210), Icons.Display),
        "Respawn" => (Color.FromRgb(90, 230, 120), Icons.Start),
        "Safehouse" => (Color.FromRgb(140, 220, 120), Icons.Marker),
        "Convoy" => (Color.FromRgb(240, 200, 120), Icons.Path),
        _ => (Color.FromRgb(180, 180, 190), Icons.Marker),
    };

    // ---- The readers ---------------------------------------------------------------------------

    static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    static uint? Hex(string s) => uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint h) ? h : null;
    static Vector3 V(string[] w, int at) => new(F(w[at]), F(w[at + 1]), F(w[at + 2]));

    /// <summary>Rows right, up and forward (9 numbers) as a turn.</summary>
    static Matrix4x4 Rows(string[] w, int at) => new(F(w[at]), F(w[at + 1]), F(w[at + 2]), 0, F(w[at + 3]), F(w[at + 4]), F(w[at + 5]), 0,
                                                     F(w[at + 6]), F(w[at + 7]), F(w[at + 8]), 0, 0, 0, 0, 1);

    /// <summary>The lines of a text file worth reading (not empty, not a comment), each split into words by spaces or tabs.</summary>
    static IEnumerable<string[]> Lines(string path) =>
        System.IO.File.ReadLines(path).Select(l => l.Trim())
            .Where(l => l.Length > 0 && l[0] is not ('#' or ';'))
            .Select(l => l.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Reads a line, counting it as unread if it doesn't have what is expected.</summary>
    static void Try(ref int unread, Action read)
    {
        try { read(); }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException) { unread++; }
    }

    /// <summary>"object_raid_node" as "Raid node"; "bftp" as "BFTP".</summary>
    static string KindOf(string className)
    {
        string words = className.StartsWith("object_") ? className[7..] : className;
        words = words.Replace('_', ' ').Replace("bftp", "BFTP");
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>The campaign's markers: activity, mission and safehouse nodes, each with its class, handle, place and properties.</summary>
    static void ReadCampaignObjects(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (XElement obj in XDocument.Load(path).Root?.Elements("object") ?? [])
        {
            int bad = 0;
            Try(ref bad, () =>
            {
                string className = (string?)obj.Element("class") ?? "";
                var properties = obj.Elements("property").Select(p => ((string?)p.Element("name") ?? "", (string?)p.Element("value") ?? "")).ToList();
                string[] op = properties.FirstOrDefault(p => p.Item1 == "op").Item2?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
                string[] pos = ((string?)obj.Element("pos") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string name = properties.FirstOrDefault(p => p.Item1 is "mission_info" or "house_arrest_type" or "display_name" or "name").Item2 ?? "";
                var marker = new GameplayMarker
                {
                    Kind = KindOf(className), Family = "Campaign", File = file, Name = name, Handle = Hex((string?)obj.Element("handle") ?? ""),
                    Position = op.Length >= 12 ? V(op, 0) : V(pos, 0), Turn = op.Length >= 12 ? Rows(op, 3) : Matrix4x4.Identity,
                };
                marker.Properties.Add(("class", className));
                if ((string?)obj.Element("zone") is string zone) marker.Properties.Add(("zone", zone));
                foreach (var (n, v) in properties.Where(p => p.Item1 != "op")) marker.Properties.Add((n, v));
                into.Add(marker);
            });
            unread += bad;
        }
    }

    /// <summary>region, handle, zone, cliff|mine, box|sphere, enabled, not_in_mission, not_in_activity, place (3), turn (9), box (6), radius.</summary>
    static void ReadKillRegions(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] != "region") return;
                bool sphere = w[4] == "sphere";
                float radius = F(w[26]);
                var marker = new GameplayMarker
                {
                    Kind = "Kill region", Family = "Kill region", File = file, Name = $"{w[3]} ({w[2]})", Handle = Hex(w[1]),
                    Position = V(w, 8), Turn = Rows(w, 11),
                    Box = sphere ? (new Vector3(-radius), new Vector3(radius)) : (V(w, 20), V(w, 23)),
                };
                marker.Properties.AddRange([("zone", w[2]), ("kills by", w[3]), ("shape", w[4]), ("enabled", w[5]), ("not in a mission", w[6]),
                                            ("not in an activity", w[7]), ("radius", w[26])]);
                into.Add(marker);
            });
    }

    /// <summary>area, handle, zone, disabled, allow_ambient_peds, the yellow polygon (count, then x z ...), the warning polygon the same way.</summary>
    static void ReadRestrictedAreas(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] != "area") return;
                List<Vector2> Polygon(int at, out int next)
                {
                    int count = int.Parse(w[at], CultureInfo.InvariantCulture);
                    next = at + 1 + count * 2;
                    return Enumerable.Range(0, count).Select(i => new Vector2(F(w[at + 1 + i * 2]), F(w[at + 2 + i * 2]))).ToList();
                }
                List<Vector2> yellow = Polygon(5, out int after), warning = Polygon(after, out _);
                if (yellow.Count == 0) return;
                var middle = yellow.Aggregate(Vector2.Zero, (sum, p) => sum + p) / yellow.Count;
                var marker = new GameplayMarker
                {
                    Kind = "Restricted area", Family = "Restricted area", File = file, Name = w[2], Handle = Hex(w[1]),
                    Position = new Vector3(middle.X, 0, middle.Y), Polygon = yellow, OuterPolygon = warning.Count > 0 ? warning : null,
                };
                marker.Properties.AddRange([("zone", w[2]), ("disabled", w[3]), ("allow ambient people", w[4]),
                                            ("points", $"{yellow.Count} (and {warning.Count} in the warning area)")]);
                into.Add(marker);
            });
    }

    /// <summary>building, handle, place (3), points of control, importance (0..3), propaganda (0/1).</summary>
    static void ReadEdfBuildings(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] != "building") return;
                var marker = new GameplayMarker { Kind = "EDF building", Family = "EDF building", File = file, Handle = Hex(w[1]), Position = V(w, 2) };
                marker.Properties.AddRange([("points of control", w[5]), ("importance (0-3)", w[6]), ("propaganda", w[7])]);
                into.Add(marker);
            });
    }

    /// <summary>region, handle, place (3), turn (9), box (6), enabled, day chance, night chance, least, most; node lines name its squads.</summary>
    static void ReadAmbushRegions(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        var regions = new Dictionary<string, GameplayMarker>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] == "region")
                {
                    var marker = new GameplayMarker
                    {
                        Kind = "Ambush region", Family = "Ambush region", File = file, Handle = Hex(w[1]),
                        Position = V(w, 2), Turn = Rows(w, 5), Box = (V(w, 14), V(w, 17)),
                    };
                    marker.Properties.AddRange([("enabled", w[20]), ("chance by day", w[21]), ("chance by night", w[22]), ("squads, least", w[23]), ("squads, most", w[24])]);
                    regions[w[1]] = marker;
                    into.Add(marker);
                }
                else if (w[0] == "node" && regions.TryGetValue(w[1], out GameplayMarker? region))
                    region.Properties.Add(("squad node", $"{w[2]} ({w[3]})"));
            });
    }

    /// <summary>special|vip, the squad node's handle, its squad, its character, place (3), its group, zone column and row.</summary>
    static void ReadStoryNodes(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                var marker = new GameplayMarker
                {
                    Kind = w[0] == "vip" ? "Story person (VIP)" : "Story person", Family = "Story person", File = file, Name = w[3], Handle = Hex(w[1]), Position = V(w, 4),
                };
                marker.Properties.AddRange([("squad", w[2]), ("character", w[3]), ("group", w[7])]);
                into.Add(marker);
            });
    }

    /// <summary>
    /// node|ped, handle, type, place (3), facing (x z): the squads' and the walkers' action nodes (type
    /// lines are the types' own). Its columns are split by tabs only: the types' names have spaces ("EDF Talk").
    /// </summary>
    static void ReadActionNodes(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        var lines = System.IO.File.ReadLines(path).Where(l => l.Length > 0 && l[0] != '#')
                                  .Select(l => l.Split('\t', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToArray());
        foreach (string[] w in lines)
            Try(ref unread, () =>
            {
                if (w[0] is not ("node" or "ped")) return;
                float fx = F(w[6]), fz = F(w[7]);
                var forward = new Vector3(fx, 0, fz);
                forward = forward.LengthSquared() > 1e-6f ? Vector3.Normalize(forward) : Vector3.UnitZ;
                Vector3 right = Vector3.Cross(Vector3.UnitY, forward);
                var marker = new GameplayMarker
                {
                    Kind = w[0] == "node" ? "Squad action node" : "Pedestrian node", Family = "Action node", File = file, Name = w[2], Handle = Hex(w[1]),
                    Position = V(w, 3), Turn = new Matrix4x4(right.X, right.Y, right.Z, 0, 0, 1, 0, 0, forward.X, forward.Y, forward.Z, 0, 0, 0, 0, 1),
                };
                marker.Properties.Add(("action", w[2]));
                into.Add(marker);
            });
    }

    /// <summary>zone, handle, parent's handle, its kiosk (item), its class, place (3).</summary>
    static void ReadKiosks(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                var marker = new GameplayMarker { Kind = "Kiosk", Family = "Kiosk", File = file, Name = w[3], Handle = Hex(w[1]), Position = V(w, 5) };
                marker.Properties.AddRange([("zone", w[0]), ("kind", w[4])]);
                into.Add(marker);
            });
    }

    /// <summary>safehouse name district visible enabled place (3) turn (9); start place (3) turn (9).</summary>
    static void ReadRespawns(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] == "safehouse")
                {
                    var marker = new GameplayMarker { Kind = "Respawn (safehouse)", Family = "Respawn", File = file, Name = w[1], Position = V(w, 5), Turn = Rows(w, 8) };
                    marker.Properties.AddRange([("district", w[2]), ("on the map", w[3]), ("enabled", w[4])]);
                    into.Add(marker);
                }
                else if (w[0] == "start")
                    into.Add(new GameplayMarker { Kind = "Respawn (start)", Family = "Respawn", File = file, Name = "start", Position = V(w, 1), Turn = Rows(w, 4) });
            });
    }

    /// <summary>safehouse handle name enabled; area box place (3) turn (9) min (3) max (3); node place (3) name n.</summary>
    static void ReadSafehouseParking(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        string safehouse = "";
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] == "safehouse") safehouse = w[2] != "-" ? w[2] : w[1];
                else if (w[0] == "area" && w[1] == "box")
                    into.Add(new GameplayMarker { Kind = "Safehouse area", Family = "Safehouse", File = file, Name = safehouse, Position = V(w, 2), Turn = Rows(w, 5), Box = (V(w, 14), V(w, 17)) });
                else if (w[0] == "node")
                {
                    var marker = new GameplayMarker { Kind = "Safehouse parking", Family = "Safehouse", File = file, Name = w[4], Position = V(w, 1) };
                    marker.Properties.Add(("safehouse", safehouse));
                    into.Add(marker);
                }
            });
    }

    /// <summary>convoy name Destroy|Capture difficulty speeds (2) hours (2) place (3) district; member lines add its vehicles.</summary>
    static void ReadConvoys(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        var convoys = new Dictionary<string, GameplayMarker>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] w in Lines(path))
            Try(ref unread, () =>
            {
                if (w[0] == "convoy")
                {
                    var marker = new GameplayMarker { Kind = "Convoy start", Family = "Convoy", File = file, Name = w[1], Position = V(w, 8) };
                    marker.Properties.AddRange([("goal", w[2]), ("difficulty", w[3]), ("speed, evading", $"{w[4]}, {w[5]}"), ("hours", $"{w[6]} to {w[7]}"), ("district", w[11])]);
                    convoys[w[1]] = marker;
                    into.Add(marker);
                }
                else if (w[0] == "member" && convoys.TryGetValue(w[1], out GameplayMarker? convoy))
                    convoy.Properties.Add((w[2], $"{w[3]} people, {w[4]}"));
            });
    }

    /// <summary>shop|x|y|z and cabinet|x|y|z: the safehouses' upgrade shops and weapon cabinets (text lines aren't places).</summary>
    static void ReadSafehouseStations(string path, string file, List<GameplayMarker> into, ref int unread)
    {
        foreach (string line in System.IO.File.ReadLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != ';'))
        {
            string[] w = line.Split('|');
            if (w[0] is not ("shop" or "cabinet")) continue;
            Try(ref unread, () => into.Add(new GameplayMarker
            {
                Kind = w[0] == "shop" ? "Upgrade shop" : "Weapon cabinet", Family = "Safehouse", File = file, Position = V(w, 1),
            }));
        }
    }
}
