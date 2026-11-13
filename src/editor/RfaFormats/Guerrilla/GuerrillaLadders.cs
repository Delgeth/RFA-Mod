using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's ladders in its zones, as Armageddon's zone objects, for the world builder. Both games
/// have the class and the code that climbs it (Armageddon's is Guerrilla's), written the same way by Volition's
/// tools: two properties, ladder_rungs (how many rungs, a quarter of a metre each in both games: the ladder's
/// height) and op (its placement), in that order. A ladder is an invisible volume the player and people climb;
/// what is seen is its building's own mesh. Every one of Guerrilla's 172 (as every one of Armageddon's own) is a
/// child of an rfg_mover, its building, and both games' code needs that: Armageddon's loader switches off a
/// ladder whose parent isn't a mover, and when a building is damaged the game switches its ladders off (see the
/// guerrilla_world mod's "Guerrilla's ladders" for Guerrilla's rule).
///
/// So each ladder is placed only where its building is (the zone file's object made from it, found by Guerrilla's
/// handle and the object's number, as the navpoints on buildings are), as that building's child: written after it,
/// named its parent, and first in its list of children, the building's other children after it (as
/// <see cref="GuerrillaPopulation"/> attaches its navpoints). A building's ladders keep Guerrilla's order among
/// themselves (Guerrilla's rule acts on a building's first ladder; one building has two). Guerrilla's values as
/// they are; the flags 0, as Armageddon's own ladders (the high byte of Guerrilla's is its tools'); the handle
/// Guerrilla's unless taken.
/// </summary>
public static class GuerrillaLadders
{
    const uint None = 0xFFFFFFFF;
    static readonly uint Ladder = NameHashes.ClassHash("ladder");
    static readonly uint RfgMover = NameHashes.ClassHash("rfg_mover"), GeneralMover = NameHashes.ClassHash("general_mover");

    /// <summary>The order Volition's tools wrote a ladder's properties in (Armageddon's own zones, and Guerrilla's).</summary>
    static readonly string[] Order = ["ladder_rungs", "op"];

    /// <summary>How many ladders the game holds at once (its ladder pool, Guerrilla's 128 too).</summary>
    public const int MostLadders = 128;

    /// <summary>
    /// The sounds Guerrilla's code plays as someone slides down a ladder, not an animation's: the loop while sliding
    /// (ladder_start_slide_sound) and, as they land, the loop stopped and the release played (ladder_process_human).
    /// Armageddon's code is the same with its own two events (game_audio.xtbl's Slide_Down_Ladder and
    /// Slide_Down_Ladder_Land); the weapons build makes these with the player's other foley (the events
    /// <see cref="GuerrillaSoundBanks.EventName"/>(cue)) and the guerrilla_world mod plays them in their place in our
    /// worlds.
    /// </summary>
    public static readonly (string Cue, bool Looping)[] SlideCues = [("MOV_FOLEY_LADDER_LOOP_01", true), ("MOV_FOLEY_LADDER_RELEASE_01", false)];

    /// <summary>A zone's ladders (from Guerrilla's zone, <paramref name="zone"/>).</summary>
    public static List<GuerrillaZoneObject> Ladders(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone) =>
        [.. zone.Values.Where(o => o.ClassHash == Ladder)];

    /// <summary>
    /// A zone's ladders as Armageddon's objects (handles not in <paramref name="taken"/>, which gains theirs), each on
    /// its building among <paramref name="placed"/> (the zone file's objects so far, whose list of children it joins);
    /// a ladder whose building isn't placed is left out, counted in <paramref name="leftOut"/>.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<ZoneObject> placed, ISet<uint> taken,
                                           out int leftOut)
    {
        // The buildings placed, by Guerrilla's handle (the same object: its number too).
        var movers = placed.Where(o => o.ClassHash == RfgMover || o.ClassHash == GeneralMover).GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
        ZoneObject? BuildingOf(GuerrillaZoneObject ladder) =>
            movers.TryGetValue(ladder.Parent, out var m) && zone.TryGetValue(ladder.Parent, out var g) && g.Number == m.Number ? m : null;

        leftOut = 0;
        var made = new List<ZoneObject>();
        foreach (var group in Ladders(zone).GroupBy(l => l.Parent))
        {
            if (BuildingOf(group.First()) is not { } building || group.Any(l => l.Value("ladder_rungs") is not { Length: 4 } || l.Value("op") is not { Length: 48 }))
            {
                leftOut += group.Count();
                continue;
            }
            // The building's ladders in the order of Guerrilla's chain of its children (first child, then each one's sibling).
            var inChain = new List<GuerrillaZoneObject>();
            if (zone.TryGetValue(group.Key, out var parent))
                for (uint c = parent.Child; c != None && inChain.Count < zone.Count && zone.TryGetValue(c, out var child); c = child.Sibling)
                    if (child.ClassHash == Ladder && child.Parent == group.Key) inChain.Add(child);
            var ordered = inChain.Concat(group.Where(l => !inChain.Contains(l))).ToList();
            // Put first in the building's list last to first, so they keep that order at its head.
            var objects = new List<ZoneObject>();
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var ladder = ordered[i];
                uint handle = ladder.Handle;
                while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
                var properties = Order.Select(name =>
                {
                    var (type, raw) = ladder.Properties[NameHashes.PropertyHash(name)];
                    return new NewProperty(type, NameHashes.PropertyHash(name), raw);
                });
                var obj = ZoneObject.Create(Ladder, handle, ladder.Number, ladder.BoundsMin, ladder.BoundsMax, [.. properties], 0);
                obj.Parent = building.Handle;
                obj.Sibling = building.Child;
                obj.Child = None;
                building.Child = obj.Handle;
                objects.Add(obj);
            }
            objects.Reverse();
            made.AddRange(objects);
        }
        return made;
    }
}
