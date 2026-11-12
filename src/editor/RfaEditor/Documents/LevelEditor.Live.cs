using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Threading;
using RfaFormats.Zones;

namespace RfaEditor;

/// <summary>
/// Live editing: while Live is on, what you do to the zone's objects is done to the running game's
/// too (see <see cref="GameLink"/>; the game has to run with the console mod, and be in this level):
///   an object moved or turned here is moved and turned there, as you drag it (the game's own "set
///     position" for the object, which its mission scripts use);
///   an object deleted here is removed there;
///   an object whose properties you change here (the picked one: a light's colour, a flag) is made
///     again there with them: removed, and a moment later made from its zone data;
///   an object added here (a duplicate, a new chunk, a delete undone) is made there from its zone
///     data, as the game makes a level's objects. The game needs its chunk loaded: a chunk no object
///     of the zone had when Live went on is loaded first (see LoadLiveChunk), then the object made.
/// About 30 times a second the zone's objects are compared with what the game was last sent, so whatever
/// changed them (the gizmo, the inspector, the controller, magnets, undo and redo) is sent alike.
/// The level geometry's pieces are moved, taken away and added in the game too (see WatchLiveGeometry).
/// What the game can't be shown this way needs the zone saved and the level loaded again (Save and
/// reload): objects whose model can't be loaded as the game runs, and the ground. With Auto
/// reload on as well, that is done for you: two seconds after the last such change the zone is
/// saved and the game loads the level again, the player put back where they stood. Without it,
/// nothing is saved by Live itself.
/// </summary>
public partial class LevelEditor
{
    readonly DispatcherTimer liveTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
    readonly Dictionary<uint, (Vector3 Position, Matrix4x4 Rotation)> liveSent = [];  // what the game was last sent (or had)
    readonly HashSet<uint> liveNotInGame = [];  // added here since, and not made there: the game has no such object
    readonly Dictionary<uint, int> liveContent = [];  // the properties (but where it is) of objects that were picked, as the game has them
    readonly Dictionary<uint, int> liveHold = [];      // removed there to be made again: not before this tick
    int liveTicks;
    DateTime? liveReloadWanted;  // when a change was last made that the game only shows after the level is loaded again
    readonly Dictionary<uint, (Vector3 Position, Matrix4x4 Rotation)> liveGeometry = [];  // the level geometry's pieces, as the game has them
    readonly Dictionary<uint, (int List, int Index, string Name)> liveGeometryAt = [];      // where each piece is in the game's stitch list
    readonly HashSet<uint> liveGeometryHidden = [];                // pieces taken away in the game (deleted here)
    readonly Dictionary<uint, string> liveGeometryMade = [];       // pieces added here and made in the game (or asked to be), by name
    readonly Dictionary<uint, int> liveGeometryAsked = [];         // ... whose answer hasn't come yet: since which tick
    readonly HashSet<uint> liveGeometryFailed = [];                // pieces the game couldn't make: shown after Save and reload
    readonly HashSet<string> liveGeometryChunksRetried = new(StringComparer.OrdinalIgnoreCase);  // chunks loaded again as the game hadn't them
    readonly HashSet<string> liveChunks = new(StringComparer.OrdinalIgnoreCase);  // the chunks the game has loaded for this zone
    readonly Dictionary<string, (string Container, int Since)> liveChunkLoads = new(StringComparer.OrdinalIgnoreCase);  // asked of the game, not answered yet
    readonly HashSet<string> liveChunksFailed = new(StringComparer.OrdinalIgnoreCase);  // the game couldn't load them: they need a reload
    static int liveChunkNumber;
    bool liveHooked;
    int liveMoves, liveRemoves, liveCreates;

    void OnLiveChanged(object sender, RoutedEventArgs e)
    {
        if (!liveHooked)
        {
            liveHooked = true;
            liveTimer.Tick += (_, _) => LiveTick();
        }
        if (LiveBox.IsChecked == true)
        {
            // From now on: the game is taken to have every object where the zone has it now.
            NoteLiveObjects();
            liveMoves = liveRemoves = liveCreates = 0;
            liveTimer.Start();
            context.Log(GameLink.IsRecent(gameLink.Read())
                ? "Live: on. Objects you move, turn, delete or add here are moved, turned, removed and made in the running game. Other changes need Save and reload."
                : "Live: on, once the game runs (start it with RFAModLauncher or the Game panel, and go to this level).");
        }
        else
        {
            liveTimer.Stop();
            context.Log($"Live: off ({liveMoves} moves, {liveRemoves} removals and {liveCreates} new objects were sent to the game).");
        }
    }

    /// <summary>The game is taken to have every object of the zone where the zone has it now, and their chunks loaded.</summary>
    void NoteLiveObjects()
    {
        liveSent.Clear();
        liveNotInGame.Clear();
        liveChunks.Clear();
        liveContent.Clear();
        liveHold.Clear();
        liveReloadWanted = null;
        liveChunkLoads.Clear();
        liveChunksFailed.Clear();
        liveGeometry.Clear();
        liveGeometryAt.Clear();
        liveGeometryHidden.Clear();
        liveGeometryMade.Clear();
        liveGeometryAsked.Clear();
        liveGeometryFailed.Clear();
        liveGeometryChunksRetried.Clear();
        // The pieces were made objects in their stitch list's order (list by list), with rising handles:
        // a piece's place in the game's list is its place among its list's pieces, by handle.
        var perList = new Dictionary<int, int>();
        foreach (ZoneObject piece in zone.Objects.Where(IsGeometry).OrderBy(p => p.Handle))
        {
            liveGeometry[piece.Handle] = piece.Placement;
            if ((piece.Handle & 0xFFFF0000) != ZoneFile.EditorOnlyHandles) continue;  // added here: the game hasn't it
            int list = ListOf(piece);
            int index = perList.GetValueOrDefault(list);
            perList[list] = index + 1;
            liveGeometryAt[piece.Handle] = (list, index, PieceOf(piece));
            liveChunks.Add(PieceChunk(PieceOf(piece)));  // (the level loaded it)
        }
        foreach (ZoneObject obj in zone.Objects.Where(LiveKind))
        {
            liveSent[obj.Handle] = obj.Placement;
            if (obj.Property("chunk_name")?.Text is string chunk) liveChunks.Add(chunk);
        }
    }

    /// <summary>
    /// Whether the game can make this object now: it needs the object's model loaded, which a chunk
    /// the level didn't have isn't until the zone is saved and the level loaded again (making it
    /// without would fail, or crash the game).
    /// </summary>
    bool LiveCanMake(ZoneObject obj) => obj.Property("chunk_name")?.Text is not string chunk || liveChunks.Contains(chunk);

    /// <summary>
    /// Has the running game load a chunk the level hasn't, so objects of it can be made live: a
    /// container holding the chunk's model and textures (as the zone's save adds them to the level's
    /// containers) and an asset list naming only that container are put in overrides\live\, and the
    /// game told to read the list and load the container at once (editor_chunk), as it loads its
    /// preload chunks. Its answer comes back a moment later (see LiveTick). False if the chunk's files
    /// can't all be found (then only a reload shows the object).
    /// </summary>
    bool LoadLiveChunk(string chunk)
    {
        if (liveChunkLoads.ContainsKey(chunk)) return true;
        if (liveChunksFailed.Contains(chunk)) return false;
        // (Objects name their chunk with its model's file name: chunk.cchk_pc.)
        string bare = chunk.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase) ? chunk[..^8] : chunk;
        if (context.Assets.PackageOf(bare, out string? why) is not ChunkPackage package)
        {
            context.Log($"Live: the chunk {chunk} can't be loaded in the game as it runs ({why}).");
            liveChunksFailed.Add(chunk);
            return false;
        }
        try
        {
            // Names new to the game every time: the streaming system keeps a container it was given for good.
            // (Short: the game keeps names of up to 63 characters.)
            int session = Environment.ProcessId % 10000;
            string container = $"rflive{session}_{++liveChunkNumber}.rfgchunkx";
            string assetList = $"rflive{session}_{liveChunkNumber}.asm_pc";
            var files = package.Files;
            byte[] bytes = RfaFormats.Packfile.CreateContainer(files);
            var packed = RfaFormats.Packfile.FromBytes(container + ".str2_pc", bytes);
            // As the game lists its preload chunks (chunk_containers.asm_pc): type 15, flags 0x80, the files
            // in the chunk preload allocator (2).
            var entry = new RfaFormats.AsmContainer
            {
                Name = container, Type = 15, Flags = 0x80, DataOffset = (uint)packed.DataStart, CompressedSize = packed.CompressedDataSize,
            };
            entry.Sizes.AddRange(files.Select(f => f.Data.Length));
            entry.Primitives.AddRange(package.Primitives.Select(p => new RfaFormats.AsmPrimitive
            {
                Name = p.Name, Type = p.Type, Allocator = 2, Flags = p.Flags, SplitIndex = p.SplitIndex, HeaderSize = p.HeaderSize, DataSize = p.DataSize,
            }));
            var list = new RfaFormats.AsmFile();
            list.Containers.Add(entry);
            string folder = Path.Combine(context.Workspace.OverridesFolder, "live");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, container + ".str2_pc"), bytes);
            File.WriteAllBytes(Path.Combine(folder, assetList), list.ToBytes());
            if (!gameLink.TrySend(["editor_rescan", $"editor_chunk {assetList} {container}"])) return true;  // the link is busy: asked again next time
            liveChunkLoads[chunk] = (container, liveTicks);
            context.Log($"Live: loading the chunk {chunk} in the game ({files.Sum(f => (long)f.Data.Length) / 1024:N0} KB).");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            context.Log($"Live: the chunk {chunk} couldn't be prepared for the game ({ex.Message}).");
            liveChunksFailed.Add(chunk);
            return false;
        }
    }

    /// <summary>Takes the game's answers about chunks it was asked to load.</summary>
    void TakeLiveChunkAnswers()
    {
        foreach (var (chunk, (container, since)) in liveChunkLoads.ToList())
        {
            string? answer = gameLink.ChunkAnswer(container);
            if (answer == null && liveTicks - since < 300) continue;  // (30 seconds)
            liveChunkLoads.Remove(chunk);
            if (answer == "loaded")
            {
                liveChunks.Add(chunk);
                context.Log($"Live: the chunk {chunk} is loaded in the game.");
            }
            else
            {
                liveChunksFailed.Add(chunk);
                context.Log($"Live: the game couldn't load the chunk {chunk} as it runs{(answer == null ? " (no answer)" : "")}; objects of it show after Save and reload.");
            }
        }
    }

    static readonly uint PlaceHash = NameHashes.PropertyHash("oP"), JustPosHash = NameHashes.PropertyHash("just_pos");

    /// <summary>An object's properties, but where it is and how it is turned, as one number: it changes when they do.</summary>
    static int LiveContent(ZoneObject obj)
    {
        var hash = new HashCode();
        foreach (ZoneProperty property in obj.Properties)
        {
            if (property.NameHash == PlaceHash || property.NameHash == JustPosHash) continue;
            hash.Add(property.NameHash);
            hash.Add(property.Type);
            hash.AddBytes(property.Value);
        }
        return hash.ToHashCode();
    }

    /// <summary>Objects the game has as objects of its own (level geometry pieces are parts of one mesh there).</summary>
    static bool LiveKind(ZoneObject obj) => !IsGeometry(obj);

    /// <summary>
    /// A change was made that the game only shows once the level is loaded again (the ground was
    /// sculpted, level geometry changed, an object needs a model the level hasn't loaded). With Auto
    /// reload on, the zone is saved and the level loaded again two seconds after the last of them.
    /// </summary>
    void LiveNeedsReload()
    {
        if (LiveBox.IsChecked == true) liveReloadWanted = DateTime.Now;
    }

    /// <summary>
    /// The level geometry's pieces, live: one the game has that was moved or turned here is moved
    /// there (editor_stitch: its drawing and its collision); one deleted here is taken away there
    /// (editor_stitch_hide), and put back if undo brings it back; one added here (a duplicate, one
    /// from the asset browser) is made there as the game makes the level's (editor_stitch_add),
    /// its chunk loaded first if the level hasn't it (as for objects). What the game can't do (a
    /// chunk it can't load, a piece of several sub-chunks, the zone's stitch list unknown) needs the
    /// level loaded again. With a level whose pieces share one combined collision, a moved piece's
    /// collision is at both places (the old in the combined one) until the level loads again.
    /// </summary>
    void WatchLiveGeometry()
    {
        TakeLiveGeometryAnswers();
        var pieces = zone.Objects.Where(IsGeometry).ToList();
        if (stitchListName == null)
        {
            // Where the game has the pieces isn't known: any change needs a reload.
            bool changed = pieces.Count != liveGeometry.Count || pieces.Any(p => !liveGeometry.TryGetValue(p.Handle, out var had) || had != p.Placement);
            if (!changed) return;
            liveGeometry.Clear();
            foreach (ZoneObject piece in pieces) liveGeometry[piece.Handle] = piece.Placement;
            LiveNeedsReload();
            return;
        }
        string cterrain = Path.GetFileNameWithoutExtension(stitchListName) + ".cterrain_pc";
        var lines = new List<string>();
        var done = new List<Action>();  // noted once the game has been sent the lines
        var here = new HashSet<uint>();
        foreach (ZoneObject piece in pieces)
        {
            uint handle = piece.Handle;
            here.Add(handle);
            var now = piece.Placement;
            string name = PieceOf(piece);
            bool hidden = liveGeometryHidden.Contains(handle);
            bool same = liveGeometry.TryGetValue(handle, out var had) && had == now;
            if (liveGeometryAt.TryGetValue(handle, out var at))
            {
                // One of the level's own.
                if (same && !hidden) continue;
                if (!string.Equals(name, at.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Another chunk in its place: the game's list has the old one.
                    if (!same) LiveNeedsReload();
                    liveGeometry[handle] = now;
                    continue;
                }
                lines.Add(StitchCommand(cterrain, at.List, at.Index, at.Name, now.Position, now.Rotation));
                done.Add(() => { liveGeometry[handle] = now; liveGeometryHidden.Remove(handle); });
                continue;
            }
            // Added here.
            if (liveGeometryFailed.Contains(handle) || liveGeometryAsked.ContainsKey(handle)) continue;
            bool made = liveGeometryMade.TryGetValue(handle, out string? madeAs) && string.Equals(madeAs, name, StringComparison.OrdinalIgnoreCase);
            if (made && same && !hidden) continue;
            if (!made)
            {
                if (madeAs != null)
                {
                    // Turned into another chunk here: the one made goes.
                    lines.Add($"editor_stitch_drop {handle:X8}");
                    liveGeometryMade.Remove(handle);
                }
                string chunk = PieceChunk(name);
                if (!liveChunks.Contains(chunk))
                {
                    if (LoadLiveChunk(chunk)) continue;  // made once it is loaded
                    liveGeometryFailed.Add(handle);
                    context.Log($"Live: the level geometry piece {name} was added here, but its chunk can't be loaded in the game as it runs; it shows after Save and reload.");
                    LiveNeedsReload();
                    continue;
                }
            }
            lines.Add(AddStitchCommand(cterrain, handle, name, IntOf(piece, "stitch_flags", 4), now.Position, now.Rotation));
            done.Add(() =>
            {
                liveGeometry[handle] = now;
                liveGeometryHidden.Remove(handle);
                liveGeometryMade[handle] = name;
                if (!made) liveGeometryAsked[handle] = liveTicks;
            });
        }
        // Deleted here: taken away in the game (and put back above if undo brings them back).
        foreach (uint handle in liveGeometry.Keys.Where(h => !here.Contains(h) && !liveGeometryHidden.Contains(h)).ToList())
        {
            if (liveGeometryAt.TryGetValue(handle, out var at))
                lines.Add($"editor_stitch_hide {cterrain} {at.List} {at.Index} {at.Name}");
            else if (liveGeometryMade.ContainsKey(handle))
                lines.Add($"editor_stitch_drop {handle:X8}");
            else
            {
                liveGeometry.Remove(handle);  // the game never had it
                continue;
            }
            done.Add(() => liveGeometryHidden.Add(handle));
        }
        // Added and deleted again before the game made them: forgotten.
        liveGeometryFailed.RemoveWhere(h => !here.Contains(h));
        if (lines.Count == 0) return;
        if (!gameLink.TrySend(lines)) return;  // the link is busy: next time
        foreach (Action note in done) note();
    }

    /// <summary>The game's answers about pieces it was asked to make: made, or not (then they need a reload, or their chunk loaded first).</summary>
    void TakeLiveGeometryAnswers()
    {
        foreach (var (handle, since) in liveGeometryAsked.ToList())
        {
            string? answer = gameLink.Answer($"stitch_{handle:X8}");
            if (answer == null && liveTicks - since < 100) continue;  // (3 seconds)
            liveGeometryAsked.Remove(handle);
            string name = liveGeometryMade.GetValueOrDefault(handle) ?? "";
            if (answer is "made" or "moved")
            {
                if (liveCreates++ < 20) context.Log($"Live: level geometry piece {name} made in the game.");
                continue;
            }
            liveGeometryMade.Remove(handle);
            liveGeometry.Remove(handle);
            string chunk = PieceChunk(name);
            if (answer == "nochunk" && liveGeometryChunksRetried.Add(chunk))
            {
                liveChunks.Remove(chunk);  // the game hasn't its chunk after all: loaded, then made (next time)
                continue;
            }
            liveGeometryFailed.Add(handle);
            context.Log($"Live: the game couldn't make the level geometry piece {name}{(answer == null ? " (no answer)" : "")}; it shows after Save and reload.");
            LiveNeedsReload();
        }
    }

    /// <summary>The chunk a piece is of, named as objects name theirs (chunk.cchk_pc).</summary>
    static string PieceChunk(string piece) => piece.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase) ? piece : piece + ".cchk_pc";

    static string StitchCommand(string cterrain, int list, int index, string name, Vector3 p, Matrix4x4 r) => string.Create(CultureInfo.InvariantCulture,
        $"editor_stitch {cterrain} {list} {index} {name} {p.X:0.####} {p.Y:0.####} {p.Z:0.####} {r.M11:0.######} {r.M12:0.######} {r.M13:0.######} {r.M21:0.######} {r.M22:0.######} {r.M23:0.######} {r.M31:0.######} {r.M32:0.######} {r.M33:0.######}");

    static string AddStitchCommand(string cterrain, uint id, string name, int flags, Vector3 p, Matrix4x4 r) => string.Create(CultureInfo.InvariantCulture,
        $"editor_stitch_add {cterrain} {id:X8} {name} {flags} {p.X:0.####} {p.Y:0.####} {p.Z:0.####} {r.M11:0.######} {r.M12:0.######} {r.M13:0.######} {r.M21:0.######} {r.M22:0.######} {r.M23:0.######} {r.M31:0.######} {r.M32:0.######} {r.M33:0.######}");

    /// <summary>Saves and has the game load the level again, if Auto reload is on and it is two seconds since the last change that needs it.</summary>
    bool LiveAutoReload()
    {
        if (liveReloadWanted is not DateTime wanted || AutoReloadBox.IsChecked != true) return false;
        if ((DateTime.Now - wanted).TotalSeconds < 2 || sculpt != null && strokeBefore != null || carrying ||
            System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed) return false;
        liveReloadWanted = null;
        context.Log("Live: saving and loading the level again in the game, for the changes it can't show otherwise.");
        if (!Save())
        {
            AutoReloadBox.IsChecked = false;
            context.Log("Live: the zone couldn't be saved, so Auto reload is off. Save it yourself, then turn it on again.");
            return true;
        }
        gameLink.Send(["editor_rescan", "editor_reload"]);
        NoteLiveObjects();
        return true;
    }

    void LiveTick()
    {
        if (!GameLink.IsRecent(gameLink.Read())) return;  // the game isn't running: it is told when it is
        WatchLiveGeometry();
        TakeLiveChunkAnswers();
        if (LiveAutoReload()) return;
        var lines = new List<string>();
        var moved = new List<ZoneObject>();
        var made = new List<ZoneObject>();
        var here = new HashSet<uint>();
        liveTicks++;
        // The picked object's properties changed (not just where it is): the game makes objects from
        // their properties, so it is removed there now and made again in a moment (it then counts as
        // one the game hasn't, below). An object is taken to match the game when it is first picked.
        if (selected is { } picked && LiveKind(picked) && zone.Objects.Contains(picked) && liveSent.ContainsKey(picked.Handle))
        {
            int content = LiveContent(picked);
            if (!liveContent.TryGetValue(picked.Handle, out int had)) liveContent[picked.Handle] = content;
            else if (had != content && LiveCanMake(picked) && !gameLink.Waiting && gameLink.TrySend([GameLink.RemoveCommand(picked.Handle)]))
            {
                liveContent[picked.Handle] = content;
                liveSent.Remove(picked.Handle);
                liveHold[picked.Handle] = liveTicks + 3;
                return;
            }
        }
        foreach (ZoneObject obj in zone.Objects)
        {
            if (!LiveKind(obj)) continue;
            here.Add(obj.Handle);
            if (liveHold.TryGetValue(obj.Handle, out int until))
            {
                if (liveTicks < until) continue;
                liveHold.Remove(obj.Handle);
            }
            if (!liveSent.TryGetValue(obj.Handle, out var sent))
            {
                // Added here since Live went on (or put back by undo): made in the game too, if it can be.
                if (liveNotInGame.Contains(obj.Handle)) continue;
                if (LiveCanMake(obj)) made.Add(obj);
                else if (obj.Property("chunk_name")?.Text is string chunk && LoadLiveChunk(chunk)) continue;  // made once it is loaded
                else if (liveNotInGame.Add(obj.Handle))
                {
                    context.Log($"Live: {Label(obj)} was added here, but the level hasn't its model loaded; the game shows it after Save and reload.");
                    LiveNeedsReload();
                }
                continue;
            }
            var now = obj.Placement;
            if (now.Position == sent.Position && now.Rotation == sent.Rotation) continue;
            lines.Add(GameLink.MoveCommand(obj.Handle, now.Position, now.Rotation));
            moved.Add(obj);
        }
        // Objects added and deleted here that the game never had: forgotten, so a new object given the
        // same handle later is made.
        liveNotInGame.RemoveWhere(handle => !here.Contains(handle) && !liveSent.ContainsKey(handle));
        List<uint> removed = liveSent.Keys.Where(handle => !here.Contains(handle)).ToList();
        foreach (uint handle in removed)
            if (!liveNotInGame.Contains(handle)) lines.Add(GameLink.RemoveCommand(handle));
        if (made.Count > 0)
        {
            if (gameLink.Waiting) return;  // the game hasn't taken the last lines yet: next time
            // What an object is attached to before the object itself: the game looks its parent up as it makes it.
            var newHandles = made.Select(o => o.Handle).ToHashSet();
            int Depth(ZoneObject o)
            {
                int depth = 0;
                for (ZoneObject? at = o; at != null && depth < 16 && newHandles.Contains(at.Parent); depth++)
                    at = made.FirstOrDefault(m => m.Handle == at.Parent);
                return depth;
            }
            foreach (ZoneObject obj in made.OrderBy(Depth)) lines.Add(gameLink.CreateCommand(obj.Handle, obj.ToBlock()));
        }
        if (lines.Count == 0 && removed.Count == 0) return;
        if (lines.Count > 0 && !gameLink.TrySend(lines)) return;  // the game hasn't taken the last ones yet: next time
        foreach (ZoneObject obj in moved) liveSent[obj.Handle] = obj.Placement;
        foreach (ZoneObject obj in made)
        {
            liveSent[obj.Handle] = obj.Placement;
            if (liveCreates++ < 20) context.Log($"Live: {Label(obj)} made in the game.");
        }
        foreach (uint handle in removed)
        {
            liveSent.Remove(handle);
            // Removed in the game: if undo puts it back here, the game no longer has it.
            if (!liveNotInGame.Remove(handle)) liveRemoves++;
        }
        liveMoves += moved.Count;
    }

    /// <summary>Saves the zone, then has the running game load the level again, so it shows every change.</summary>
    void OnSaveAndReload(object sender, RoutedEventArgs e)
    {
        if (!Save()) return;
        if (!GameLink.IsRecent(gameLink.Read()))
        {
            context.Log("Save and reload: saved. The game isn't running with the console mod, so there was nothing to reload.");
            return;
        }
        gameLink.Send(["editor_rescan", "editor_reload"]);
        // The game has every object where the saved zone puts it again.
        if (LiveBox.IsChecked == true) NoteLiveObjects();
        context.Log("Save and reload: saved, and the game is loading the level again.");
    }
}
