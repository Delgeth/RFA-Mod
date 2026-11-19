using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's shape cutters in its zones (its pre-made damage), as Armageddon's zone objects, for the
/// world builder. Guerrilla's zones hold 319 (the Mohole's broken pipes in rfg_10_09 alone 54), Armageddon's own 492.
/// Both games have the class and the same code (Guerrilla's shape_cutter::serialize and do_cut; Armageddon's cut
/// 0x67A5D0): a placed cutter is a sphere, its two properties oP (where: a 48-byte placement, world coordinates) and
/// outer_radius. Once it streams in it cuts away every piece of the rfg_movers its sphere reaches and breaks their
/// links (general_movers in it take damage), once, and is gone; it is made again, and cuts again, each time its zone
/// streams in. Without them Guerrilla's pipes and buildings stand whole where it shows them broken: the Mohole's
/// pipes end in clean, open sections in mid-air instead of broken off.
///
/// Written as Armageddon's own: oP then outer_radius (Guerrilla's tools wrote them the other way round), Guerrilla's
/// values as they are, the flags 0 (the high byte of Guerrilla's is its tools'), the handle Guerrilla's unless taken.
/// 285 of Guerrilla's are children of the rfg_mover they cut (as Armageddon's attached ones are): each is placed only
/// where that building is (found by Guerrilla's handle, else by its number and box where its handle was taken), as its child (written after it, first in its list of children, as
/// <see cref="GuerrillaLadders"/> are). The other 34 are attached to nothing, and placed so.
///
/// The game holds 128 at once (Armageddon's pool, registered at 0xF25E73; Guerrilla's 103); a zone holds 60 at most.
/// </summary>
public static class GuerrillaShapeCutters
{
    const uint None = 0xFFFFFFFF;
    static readonly uint ShapeCutter = NameHashes.ClassHash("shape_cutter");
    static readonly uint RfgMover = NameHashes.ClassHash("rfg_mover"), GeneralMover = NameHashes.ClassHash("general_mover");

    /// <summary>The order Volition's tools wrote a shape cutter's properties in Armageddon's zones.</summary>
    static readonly string[] Order = ["oP", "outer_radius"];

    /// <summary>How many shape cutters the game holds at once (Armageddon's pool).</summary>
    public const int MostShapeCutters = 128;

    /// <summary>A zone's shape cutters (from Guerrilla's zone, <paramref name="zone"/>).</summary>
    public static List<GuerrillaZoneObject> ShapeCutters(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone) =>
        [.. zone.Values.Where(o => o.ClassHash == ShapeCutter)];

    /// <summary>
    /// A zone's shape cutters as Armageddon's objects (handles not in <paramref name="taken"/>, which gains theirs), each
    /// on its building among <paramref name="placed"/> (the zone file's objects so far, whose list of children it joins),
    /// or on nothing as in Guerrilla's zone; one whose building isn't placed is left out, counted in <paramref name="leftOut"/>.
    /// </summary>
    public static List<ZoneObject> Convert(IReadOnlyDictionary<uint, GuerrillaZoneObject> zone, IReadOnlyList<ZoneObject> placed, ISet<uint> taken,
                                           out int leftOut)
    {
        // The buildings placed: by Guerrilla's handle (the same object: its number too), else (its handle was taken in
        // our zone, so it has another: 3C000004 in rfg_03_12, 96000005 in rfg_09_06) by its class, number and box,
        // which it keeps.
        var placedMovers = placed.Where(o => o.ClassHash == RfgMover || o.ClassHash == GeneralMover).ToList();
        var movers = placedMovers.GroupBy(o => o.Handle).ToDictionary(g => g.Key, g => g.First());
        var moved = placedMovers.GroupBy(o => (o.ClassHash, o.Number, o.BoundsMin, o.BoundsMax)).ToDictionary(g => g.Key, g => g.First());
        ZoneObject? BuildingOf(GuerrillaZoneObject cutter)
        {
            if (!zone.TryGetValue(cutter.Parent, out var g)) return null;
            if (movers.TryGetValue(cutter.Parent, out var m) && g.Number == m.Number && g.ClassHash == m.ClassHash) return m;
            return moved.GetValueOrDefault((g.ClassHash, g.Number, g.BoundsMin, g.BoundsMax));
        }

        leftOut = 0;
        var made = new List<ZoneObject>();
        ZoneObject Make(GuerrillaZoneObject cutter)
        {
            uint handle = cutter.Handle;
            while (!taken.Add(handle)) handle = (handle & 0xFFFF0000) | ((handle + 1) & 0xFFFF);
            var properties = Order.Select(name =>
            {
                var (type, raw) = cutter.Properties[NameHashes.PropertyHash(name)];
                return new NewProperty(type, NameHashes.PropertyHash(name), raw);
            });
            return ZoneObject.Create(ShapeCutter, handle, cutter.Number, cutter.BoundsMin, cutter.BoundsMax, [.. properties], 0);
        }
        bool Complete(GuerrillaZoneObject cutter) => cutter.Value("oP") is { Length: 48 } && cutter.Value("outer_radius") is { Length: 4 };

        foreach (var group in ShapeCutters(zone).GroupBy(c => c.Parent))
        {
            if (group.Key == None)
            {
                foreach (var cutter in group)
                {
                    if (!Complete(cutter)) { leftOut++; continue; }
                    var obj = Make(cutter);
                    obj.Parent = obj.Sibling = obj.Child = None;
                    made.Add(obj);
                }
                continue;
            }
            if (BuildingOf(group.First()) is not { } building || group.Any(c => !Complete(c)))
            {
                leftOut += group.Count();
                continue;
            }
            // The building's cutters in the order of Guerrilla's chain of its children (first child, then each one's sibling).
            var inChain = new List<GuerrillaZoneObject>();
            if (zone.TryGetValue(group.Key, out var parent))
                for (uint c = parent.Child; c != None && inChain.Count < zone.Count && zone.TryGetValue(c, out var child); c = child.Sibling)
                    if (child.ClassHash == ShapeCutter && child.Parent == group.Key) inChain.Add(child);
            var ordered = inChain.Concat(group.Where(c => !inChain.Contains(c))).ToList();
            // Put first in the building's list last to first, so they keep that order at its head.
            var objects = new List<ZoneObject>();
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var obj = Make(ordered[i]);
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
