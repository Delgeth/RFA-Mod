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
///   an object deleted here is removed there.
/// Ten times a second the zone's objects are compared with what the game was last sent, so whatever
/// changed them (the gizmo, the inspector, the controller, magnets, undo and redo) is sent alike.
/// What the game can't be shown this way needs the zone saved and the level loaded again (Save and
/// reload): objects added here (the game has none with their handle), property changes, the ground
/// and level geometry. Nothing is saved by Live itself.
/// </summary>
public partial class LevelEditor
{
    readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly Dictionary<uint, (Vector3 Position, Matrix4x4 Rotation)> liveSent = [];  // what the game was last sent (or had)
    readonly HashSet<uint> liveNotInGame = [];  // added here since: the game has no such object
    bool liveHooked;
    int liveMoves, liveRemoves;

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
            liveSent.Clear();
            liveNotInGame.Clear();
            foreach (ZoneObject obj in zone.Objects.Where(LiveKind)) liveSent[obj.Handle] = obj.Placement;
            liveMoves = liveRemoves = 0;
            liveTimer.Start();
            context.Log(GameLink.IsRecent(gameLink.Read())
                ? "Live: on. Objects you move, turn or delete here are moved, turned and removed in the running game. Added objects and other changes need Save and reload."
                : "Live: on, once the game runs (start it with RFAModLauncher or the Game panel, and go to this level).");
        }
        else
        {
            liveTimer.Stop();
            context.Log($"Live: off ({liveMoves} moves and {liveRemoves} removals were sent to the game).");
        }
    }

    /// <summary>Objects the game has as objects of its own (level geometry pieces are parts of one mesh there).</summary>
    static bool LiveKind(ZoneObject obj) => !IsGeometry(obj);

    void LiveTick()
    {
        if (!GameLink.IsRecent(gameLink.Read())) return;  // the game isn't running: it is told when it is
        var lines = new List<string>();
        var moved = new List<ZoneObject>();
        var here = new HashSet<uint>();
        foreach (ZoneObject obj in zone.Objects)
        {
            if (!LiveKind(obj)) continue;
            here.Add(obj.Handle);
            if (!liveSent.TryGetValue(obj.Handle, out var sent))
            {
                // Added here since Live went on (or put back by undo): the game can't be given it.
                if (liveNotInGame.Add(obj.Handle))
                    context.Log($"Live: {Label(obj)} was added here; the game shows it after Save and reload.");
                continue;
            }
            if (liveNotInGame.Contains(obj.Handle)) continue;
            var now = obj.Placement;
            if (now.Position == sent.Position && now.Rotation == sent.Rotation) continue;
            lines.Add(GameLink.MoveCommand(obj.Handle, now.Position, now.Rotation));
            moved.Add(obj);
        }
        List<uint> removed = liveSent.Keys.Where(handle => !here.Contains(handle)).ToList();
        foreach (uint handle in removed)
            if (!liveNotInGame.Contains(handle)) lines.Add(GameLink.RemoveCommand(handle));
        if (lines.Count == 0 && removed.Count == 0) return;
        if (lines.Count > 0 && !gameLink.TrySend(lines)) return;  // the game hasn't taken the last ones yet: next time
        foreach (ZoneObject obj in moved) liveSent[obj.Handle] = obj.Placement;
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
        if (LiveBox.IsChecked == true)
        {
            liveSent.Clear();
            liveNotInGame.Clear();
            foreach (ZoneObject obj in zone.Objects.Where(LiveKind)) liveSent[obj.Handle] = obj.Placement;
        }
        context.Log("Save and reload: saved, and the game is loading the level again. (A zone saved for the first time while the game runs is only used from the game's next start.)");
    }
}
