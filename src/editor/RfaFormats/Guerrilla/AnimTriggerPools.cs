using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Room for the animations' own lists. While the game reads its animation sets it copies each entry's
/// lists in anim_files.xtbl (its effects, voice lines, sounds, props and hand IKs) into five fixed pools,
/// one record each, and never checks their room: one more than a pool holds spoils whatever follows it
/// in the game's memory (an animation's prop trigger with a sound's numbers in it stopped the game).
/// Armageddon's own fill them nearly full; the guerrilla_gear mod gives the game bigger ones of its own.
/// The tools that write anim_files.xtbl (the vehicles', people's and weapons' builds) check it here
/// first and refuse to write one that holds more than the mod's room.
/// </summary>
public static class AnimTriggerPools
{
    /// <summary>Each pool: its entries' list and element in anim_files.xtbl, the game's room, the guerrilla_gear mod's.</summary>
    public static readonly IReadOnlyList<(string List, string Element, int Game, int Mod)> Pools =
    [
        ("Effects", "Effect", 64, 1024),
        ("Voice_Lines", "Voice_Line", 320, 4096),
        ("Sounds", "Sound", 960, 16384),
        ("Props", "Prop", 256, 4096),
        ("IKs", "IK", 1300, 16384),
    ];

    /// <summary>How many records each pool would take for this anim_files.xtbl (its Files/Anim_files entries).</summary>
    public static int[] Count(XtblFile fileList)
    {
        var counts = new int[Pools.Count];
        var entries = fileList.Table?.Element("Files")?.Element("Anim_files")?.Elements ?? [];
        foreach (var entry in entries)
            for (int i = 0; i < Pools.Count; i++)
                counts[i] += entry.Element(Pools[i].List)?.ElementsNamed(Pools[i].Element).Count() ?? 0;
        return counts;
    }

    /// <summary>
    /// Refuses (InvalidDataException) an anim_files.xtbl with more records for a pool than the
    /// guerrilla_gear mod's room; else a line for the log, saying where it needs the mod.
    /// </summary>
    public static string Check(XtblFile fileList)
    {
        int[] counts = Count(fileList);
        var over = Enumerable.Range(0, Pools.Count).Where(i => counts[i] > Pools[i].Mod).ToList();
        if (over.Count > 0)
            throw new InvalidDataException("anim_files.xtbl would hold more than the game has room for: " +
                string.Join(", ", over.Select(i => $"{Pools[i].List} {counts[i]} of {Pools[i].Mod}")) +
                " (the guerrilla_gear mod's room); nothing written");
        var needMod = Enumerable.Range(0, Pools.Count).Where(i => counts[i] > Pools[i].Game).Select(i => Pools[i].List).ToList();
        return "animation lists: " + string.Join(", ", Enumerable.Range(0, Pools.Count).Select(i => $"{Pools[i].List} {counts[i]} of {Pools[i].Mod}")) +
               (needMod.Count > 0 ? $" (more {string.Join(", ", needMod)} than the game's own room: guerrilla_gear must be on)" : "");
    }
}
