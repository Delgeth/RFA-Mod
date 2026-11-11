using System.Globalization;
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
///   an object added here (a duplicate, a new chunk, a delete undone) is made there from its zone
///     data, as the game makes a level's objects, if the game has its model loaded: that is taken
///     to be so when an object with the same chunk was in the zone when Live went on.
/// Ten times a second the zone's objects are compared with what the game was last sent, so whatever
/// changed them (the gizmo, the inspector, the controller, magnets, undo and redo) is sent alike.
/// What the game can't be shown this way needs the zone saved and the level loaded again (Save and
/// reload): objects whose model the level hasn't loaded, property changes, the ground and level
/// geometry. Nothing is saved by Live itself.
/// </summary>
public partial class LevelEditor
{
    readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Dictionary<uint, (Vector3 Position, Matrix4x4 Rotation)> liveSent = [];  // what the game was last sent (or had)
    readonly HashSet<uint> liveNotInGame = [];  // added here since, and not made there: the game has no such object
    readonly HashSet<string> liveChunks = new(StringComparer.OrdinalIgnoreCase);  // the chunks the game has loaded for this zone
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

    /// <summary>Objects the game has as objects of its own (level geometry pieces are parts of one mesh there).</summary>
    static bool LiveKind(ZoneObject obj) => !IsGeometry(obj);

    void LiveTick()
    {
        if (!GameLink.IsRecent(gameLink.Read())) return;  // the game isn't running: it is told when it is
        var lines = new List<string>();
        var moved = new List<ZoneObject>();
        var made = new List<ZoneObject>();
        var here = new HashSet<uint>();
        foreach (ZoneObject obj in zone.Objects)
        {
            if (!LiveKind(obj)) continue;
            here.Add(obj.Handle);
            if (!liveSent.TryGetValue(obj.Handle, out var sent))
            {
                // Added here since Live went on (or put back by undo): made in the game too, if it can be.
                if (liveNotInGame.Contains(obj.Handle)) continue;
                if (LiveCanMake(obj)) made.Add(obj);
                else if (liveNotInGame.Add(obj.Handle))
                    context.Log($"Live: {Label(obj)} was added here, but the level hasn't its model loaded; the game shows it after Save and reload.");
                continue;
            }
            var now = obj.Placement;
            if (now.Position == sent.Position && now.Rotation == sent.Rotation) continue;
            lines.Add(GameLink.MoveCommand(obj.Handle, now.Position, now.Rotation));
            moved.Add(obj);
        }
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
        gameLink.Send(["reload_level"]);
        // The game has every object where the saved zone puts it again.
        if (LiveBox.IsChecked == true) NoteLiveObjects();
        context.Log("Save and reload: saved, and the game is loading the level again. (A zone saved for the first time while the game runs is only used from the game's next start.)");
    }
}
