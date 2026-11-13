using System.IO;
using System.Numerics;
using System.Windows;
using RfaFormats;
using RfaFormats.Zones;

namespace RfaEditor;

// "Game's level": opens the zone the running game has loaded. The game is asked (editor_level) for the
// name it loaded the level by and where its camera and player are (link\level.txt); the level's zones
// are those of the archive of that name (or a container of that name, or named after it), and of
// several, the one the camera is in (else the player, else the one nearest).
public partial class MainWindow
{
    GameLink? levelLink;
    bool askingLevel;

    async void OnOpenGameLevel(object sender, RoutedEventArgs e)
    {
        if (askingLevel) return;
        levelLink ??= new GameLink(workspace.ModFolder);
        if (!GameLink.IsRecent(levelLink.Read()))
        {
            Log("Game's level: the game isn't running with the console mod (start it from the Game panel).");
            return;
        }
        if (!context.Assets.IndexReady)
        {
            Log("Game's level: the editor is still reading the game's files. Try again in a few seconds.");
            return;
        }
        askingLevel = true;
        try
        {
            levelLink.Answer("level");  // (an old answer goes)
            levelLink.Send(["editor_level"]);
            string? answer = null;
            for (int wait = 0; wait < 30 && answer == null; wait++)
            {
                await Task.Delay(100);
                answer = levelLink.Answer("level");
            }
            if (answer == null)
            {
                Log("Game's level: the game didn't answer (is its rfa_qol.dll up to date?).");
                return;
            }
            string[] lines = answer.Split('\n', StringSplitOptions.TrimEntries);
            string level = lines.Length > 0 ? lines[0] : "";
            if (level.Length == 0)
            {
                Log("Game's level: the game has no level loaded.");
                return;
            }
            Vector3? camera = lines.Length > 1 ? Numbers(lines[1]) : null;
            Vector3? player = lines.Length > 2 && lines[2].StartsWith('1') ? Numbers(lines[2][1..]) : null;
            Try(() => OpenLevelOf(level, camera, player));
        }
        finally
        {
            askingLevel = false;
        }
    }

    static Vector3? Numbers(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        return float.TryParse(parts[0], c, out float x) && float.TryParse(parts[1], c, out float y) && float.TryParse(parts[2], c, out float z)
            ? new Vector3(x, y, z) : null;
    }

    void OpenLevelOf(string level, Vector3? camera, Vector3? player)
    {
        string bare = Path.GetFileNameWithoutExtension(level);
        bool Named(string? file) => file != null && Path.GetFileNameWithoutExtension(file).Equals(bare, StringComparison.OrdinalIgnoreCase);
        var all = context.Assets.NamesWhere(n => n.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase))
            .SelectMany(n => context.Assets.PlacesOf(n).Select(p => (Name: n, p.Archive, p.Container)))
            .ToList();
        // The level's archive, else a container of its name, else zones named after it.
        var zones = all.Where(z => Named(z.Archive)).ToList();
        if (zones.Count == 0) zones = all.Where(z => Named(z.Container)).ToList();
        if (zones.Count == 0) zones = all.Where(z => Path.GetFileNameWithoutExtension(z.Name).StartsWith(bare, StringComparison.OrdinalIgnoreCase)).ToList();
        // (A zone in several containers of the archive: the same zone, once.)
        zones = zones.GroupBy(z => z.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (zones.Count == 0)
        {
            Log($"Game's level: the game has {level} loaded, but no zone of it was found in the game's files.");
            return;
        }
        var chosen = zones[0];
        if (zones.Count > 1)
        {
            // Each zone's extent (where its objects are); the one the camera (else the player) is in.
            var extents = new List<ZoneExtent>();
            foreach (var z in zones)
            {
                try
                {
                    Packfile source = z.Container == null ? workspace.Archive(z.Archive) : workspace.Container(z.Archive, z.Container);
                    if (source.Find(z.Name) is not { } entry) continue;
                    var objects = ZoneFile.Read(source.Read(entry)).Objects;
                    if (objects.Count == 0) continue;
                    var points = objects.Select(o => new Vector2(o.Placement.Position.X, o.Placement.Position.Z)).ToList();
                    extents.Add(new ZoneExtent(z.Name, z.Archive, z.Container, points.Aggregate(Vector2.Min), points.Aggregate(Vector2.Max)));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException) { }
            }
            List<ZoneExtent> Holding(Vector3 where) =>
                extents.Where(e => e.Holds(new Vector2(where.X, where.Z))).OrderBy(e => e.Area).ToList();
            if (extents.Count > 0 && (camera ?? player) is Vector3 first)
            {
                var inside = Holding(first);
                if (inside.Count == 0 && camera != null && player is Vector3 standing) inside = Holding(standing);
                ZoneExtent pick = inside.Count > 0 ? inside[0]
                    : extents.OrderBy(e => Vector2.Distance(new Vector2(first.X, first.Z), (e.Min + e.Max) / 2)).First();
                chosen = (pick.Name, pick.Archive, pick.Container);
            }
        }
        Packfile pack = chosen.Container == null ? workspace.Archive(chosen.Archive) : workspace.Container(chosen.Archive, chosen.Container);
        if (pack.Find(chosen.Name) is not { } found)
        {
            Log($"Game's level: {chosen.Name} couldn't be read.");
            return;
        }
        Log($"Game's level: the game has {level} loaded; opening {chosen.Name}" +
            (zones.Count > 1 ? $" (of its {zones.Count} zones, the one {(camera != null ? "the camera" : "the player")} is in)." : "."));
        context.OpenLevel(new GameFile(pack, found, chosen.Archive, chosen.Container));
    }

    readonly record struct ZoneExtent(string Name, string Archive, string? Container, Vector2 Min, Vector2 Max)
    {
        public bool Holds(Vector2 spot) => spot.X >= Min.X && spot.X <= Max.X && spot.Y >= Min.Y && spot.Y <= Max.Y;
        public float Area => (Max.X - Min.X) * (Max.Y - Min.Y);
    }
}
