using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's effect tables for a world ported from it: what its destructible objects
/// show when they break, are hit or stand burning. A destructible object names its entry of
/// level_objects.xtbl (its Props); the entry names effects of effects.xtbl (Death_Effect,
/// Collision_impact, Bullet_impact, Custom_Effect, effect_name, Effect; and Death_Explosion, of
/// explosions.xtbl); an effect names its visual (vfx.xtbl) and sound; the visual names its file
/// (Filename, "&lt;file&gt;.effectx") and how it is loaded (Streaming_Category: Preload, from
/// effects.vpp_pc at start-up; Environmental, with the level, from files its archives hold). Both
/// games have all four tables alike, and Armageddon's level_objects.xtbl still names most of
/// Guerrilla's entries, with its own settings and effects.
///
/// So the four tables are Armageddon's own with Guerrilla's entries added under "rfg_" names: each
/// level object the world's objects name, with its effect names made the added ones (or left out
/// where the effect isn't added) and its explosions the added ones (see below; else Armageddon's of
/// that name, or left out where it has none; its physical materials are all Armageddon's too); each
/// explosion they die in, as many as the game has room for (<see cref="ExplosionLimit"/>), the most
/// placed objects' first: Armageddon plays its own explosion of the name with its own effects, which
/// it loads only with its own levels, so ours went off unseen; each effect they name (an explosion's
/// too) that has a visual or a sound (the sound an event made of Guerrilla's cue: see
/// <see cref="GuerrillaSoundBanks.EffectSound"/>; many of Guerrilla's effects are a sound alone); each visual, Environmental, its file the
/// converted effect "rfg_&lt;file&gt;" (see <see cref="GuerrillaEffects"/>), which the world's
/// containers hold. Every added entry is fitted to the shape of Armageddon's own (see <see cref="Fit"/>:
/// the fields Armageddon's all have added, the lists left without what they must hold gone).
/// Armageddon's own entries stay as they are, so its own levels are as before.
///
/// Room: the game keeps the visuals in an array of 950 (0x1200BF4 its count, 0x121FDE8 the entries,
/// 0xB4 bytes each; one past it is left out), which Armageddon's 904 nearly fill; the guerrilla_world
/// mod gives it one of <see cref="VisualLimit"/>. Its effects are in a table of 1225, found by name
/// through a lookup of 1225 (Armageddon's own are 1112), which the mod makes <see cref="EffectLimit"/>. The visuals added are those the most
/// placed objects show, as many as fit; the rest, and the effects showing them, wait. Level objects
/// are made as many as the table has. The game reads all three at start-up from its table cache, where
/// the guerrilla_world mod hands it these.
/// </summary>
public static class GuerrillaEffectTables
{
    /// <summary>How many visual effects (vfx.xtbl entries) the game keeps; how many effects (effects.xtbl entries), with the guerrilla_world mod.</summary>
    public const int VisualLimit = 2048, EffectLimit = 4096;

    /// <summary>
    /// How many explosions (explosions.xtbl entries) the game keeps: its own reader (0x6E9C70) stops at
    /// 0x84 (132), counting at 0x1893498, the base table's first and then each DLC's
    /// (dlcNN_explosions.xtbl); the guerrilla_world mod gives it a table of 256.
    /// </summary>
    public const int ExplosionLimit = 256;

    /// <summary>
    /// The flags of Guerrilla's added explosions that Armageddon's explosion table has no name for
    /// ("explosion: flag"), left out by the last <see cref="Build"/>: its reader stops the game on a flag it
    /// doesn't know ("\"nano dissolve\" is not a valid flag name!", xml_table_tools.cpp 1381). Guerrilla's
    /// nano rifle's explosion dissolves people with "nano dissolve"; that waits for the nano rifle's port.
    /// </summary>
    public static readonly List<string> FlagsLeftOut = [];

    /// <summary>The name an added entry (or a converted effect's file) has: Guerrilla's with "rfg_" before it.</summary>
    public static string Added(string guerrillaName) => "rfg_" + guerrillaName;

    /// <summary>
    /// The longest name a level object may have: Armageddon's reader copies Name into a 32-byte buffer with a
    /// checked copy, which ends the game when it doesn't fit (its own longest is 31 characters).
    /// </summary>
    const int MostObjectName = 31;

    /// <summary>
    /// An added level object's name: <see cref="Added"/>'s, or, where that's longer than a level object's name
    /// may be (Guerrilla's 0101machine_big_mining_a_engine and two others), its start and four hex digits of a
    /// hash of Guerrilla's whole name (FNV-1a), so each stays one of its own. The world's objects name it
    /// through <see cref="Tables.PropsOf"/>.
    /// </summary>
    public static string ObjectName(string guerrillaName)
    {
        string name = Added(guerrillaName);
        if (name.Length <= MostObjectName) return name;
        uint hash = 2166136261;
        foreach (char c in guerrillaName.ToLowerInvariant()) hash = (hash ^ c) * 16777619;
        return $"{name[..(MostObjectName - 5)].TrimEnd('_')}_{hash & 0xFFFF:x4}";
    }

    /// <summary>
    /// The tables (Armageddon's with Guerrilla's entries added), each of Guerrilla's level object names
    /// the world uses to its added entry, each to the effect files (Guerrilla's names, without
    /// extension) its added effects show, every effect file to convert, the files of the effects the
    /// world plays everywhere, and what didn't fit.
    /// </summary>
    public sealed record Tables(byte[] Visuals, byte[] Effects, byte[] LevelObjects, byte[] Explosions, IReadOnlyDictionary<string, string> PropsOf,
                                IReadOnlyDictionary<string, IReadOnlyList<string>> FilesOf, IReadOnlyList<string> Files, IReadOnlyList<string> WorldFiles,
                                int VisualsAdded, int EffectsAdded, int ExplosionsAdded, IReadOnlyList<string> VisualsLeft, IReadOnlyList<string> ExplosionsWaiting,
                                IReadOnlyList<string> ExplosionsLeft)
    {
        /// <summary>How many of the added effects have a sound; how many have a sound alone; the cues of added effects that couldn't be had.</summary>
        public int SoundsAdded { get; init; }
        public int SoundOnly { get; init; }
        public IReadOnlyList<string> SoundsLeft { get; init; } = [];
    }

    /// <summary>Guerrilla's explosion fields under the names Armageddon's table gives them.</summary>
    static readonly (string Guerrilla, string Armageddon)[] ExplosionFieldNames = [("Impulse", "Mover_Impulse"), ("Structural_Damage", "StructuralDamage")];

    /// <summary>The fields of a level object naming an effect (each holding just the name).</summary>
    static readonly string[] EffectFields =
        ["Death_Effect", "Collision_impact", "Bullet_impact", "Melee_impact", "Custom_Effect", "Prefix_Effect", "Release_Effect", "Startup_Effect", "effect_name", "Effect"];

    /// <summary>Fields naming another entry (an effect, an explosion, a visual, its file, the entry itself), never given a value of Armageddon's (see <see cref="Fit"/>).</summary>
    static readonly HashSet<string> References = [.. EffectFields, "Death_Explosion", "Name", "Filename", "visual"];

    /// <summary>
    /// The tables for the level objects named in <paramref name="used"/> (Guerrilla's names, each with how
    /// many objects name it), from Armageddon's and Guerrilla's misc.vpp_pc; <paramref name="hasFile"/>
    /// says whether Guerrilla's effect file (its name without extension) can be had. The effects in
    /// <paramref name="worldEffects"/> (effects.xtbl names: those the world plays everywhere, such as
    /// its dust and its vehicles' fire, smoke, exhaust and sparks) are added too, their visuals before
    /// any object's; so are the explosions in <paramref name="worldExplosions"/> (its vehicles' and weapons'), before
    /// any object's, with their effects as the world's own.
    /// </summary>
    public static Tables Build(Packfile armageddonMisc, Packfile guerrillaMisc, IReadOnlyDictionary<string, int> used, Func<string, bool> hasFile,
                               IReadOnlyCollection<string> worldEffects, int dlcExplosions, IReadOnlyCollection<string>? worldExplosions = null,
                               Func<string, bool, Sound?>? soundOf = null)
    {
        string Text(Packfile archive, string table) =>
            Encoding.Latin1.GetString(archive.Read(archive.Find(table) ?? throw new InvalidDataException($"no {table} in {archive.Name}")));
        string ourVisuals = Text(armageddonMisc, "vfx.xtbl"), ourEffects = Text(armageddonMisc, "effects.xtbl"), ourObjects = Text(armageddonMisc, "level_objects.xtbl");
        string ourExplosionText = Text(armageddonMisc, "explosions.xtbl"), theirExplosionText = Text(guerrillaMisc, "explosions.xtbl");
        var ourExplosions = Entries(ourExplosionText, "Explosion");
        var explosions = ourExplosions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var theirExplosions = Entries(theirExplosionText, "Explosion");
        var objects = Entries(Text(guerrillaMisc, "level_objects.xtbl"), "Level_Object");
        var effects = Entries(Text(guerrillaMisc, "effects.xtbl"), "Effect");
        var visuals = Entries(Text(guerrillaMisc, "vfx.xtbl"), "Effect");
        var takenVisuals = Entries(ourVisuals, "Effect").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenEffects = Entries(ourEffects, "Effect").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenObjects = Entries(ourObjects, "Level_Object").Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Each used level object's effects, and each effect's visual (where it has one in Guerrilla's vfx.xtbl and its file can be had).
        string? VisualOf(string effect) =>
            effects.TryGetValue(effect, out var e) && Field(e, "visual") is { Length: > 0 } v && visuals.TryGetValue(v, out var x) && hasFile(FileOf(x)) ? v : null;

        // Guerrilla's explosions the used level objects die in, the most placed objects' first, as many as
        // the game has room for after Armageddon's and its DLCs' (the rest keep Armageddon's of that
        // name where it has one); each one's effects (Guerrilla's Effects list) are its objects' too.
        var explosionsOf = used.Keys.Where(objects.ContainsKey).ToDictionary(p => p,
            p => Leaves(objects[p], "Death_Explosion").Where(theirExplosions.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var explosionUse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, list) in explosionsOf)
            foreach (string x in list) explosionUse[x] = explosionUse.GetValueOrDefault(x) + used[p];
        foreach (string x in worldExplosions ?? []) if (theirExplosions.ContainsKey(x)) explosionUse[x] = int.MaxValue;  // played everywhere: first
        var asked = (worldExplosions ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var takenExplosions = explosions;
        int explosionRoom = Math.Max(0, ExplosionLimit - Count(ourExplosionText, "Explosion") - dlcExplosions);
        var rankedExplosions = explosionUse.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key)
                                           .Where(x => !takenExplosions.Contains(Added(x)) && DamageFrom(x) != null).ToList();

        // Where an added explosion takes how it damages buildings from (the fields only Armageddon's
        // have, which the game requires: see ExplosionEntry): Armageddon's explosion of its name, else
        // that of the one Guerrilla's own table gives the same damage (the same radii, structural
        // damage and impulse; the nearest human damage, then by name): chem_tank_a_demo is chem_tank_a
        // without salvage, sewage_tank_a hits as gas_pump and generator_a do. An object's explosion with neither
        // is left out, and its objects die without it, as before. One the world's vehicles and weapons
        // set off themselves (worldExplosions) is never left out: with neither, the nearest of
        // Armageddon's own by those numbers (Armageddon's, under its names) lends it the fields, its
        // kind of damage to buildings; the numbers stay Guerrilla's. The NPCs' gauss rifle shot
        // (gauss_rifle_npc) takes Armageddon's gauss_rifle so (whose radii are Guerrilla's NPC one's).
        string? DamageFrom(string explosion)
        {
            if (ourExplosions.TryGetValue(explosion, out string? same)) return same;
            string theirs = theirExplosions[explosion];
            double? Number(string entry, string field) =>
                double.TryParse(Field(entry, field), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : null;
            string[] damage = ["Radius", "CrumbleRadius", "KnockdownRadius", "FlinchRadius", "Structural_Damage", "Impulse"];
            double human = Number(theirs, "Human_Damage_Max") ?? 0;
            string? alike = theirExplosions.Where(t => !t.Key.Equals(explosion, StringComparison.OrdinalIgnoreCase) && ourExplosions.ContainsKey(t.Key) &&
                                                       damage.All(f => Number(t.Value, f) == Number(theirs, f)))
                                           .OrderBy(t => Math.Abs((Number(t.Value, "Human_Damage_Max") ?? 0) - human)).ThenBy(t => t.Key, StringComparer.Ordinal)
                                           .Select(t => ourExplosions[t.Key]).FirstOrDefault();
            if (alike != null || !asked.Contains(explosion)) return alike;
            string[] armageddons = ["Radius", "CrumbleRadius", "KnockdownRadius", "FlinchRadius", "StructuralDamage", "Mover_Impulse"];
            double Apart(string ourEntry)
            {
                double apart = 0;
                for (int i = 0; i < damage.Length; i++)
                {
                    double g = Number(theirs, damage[i]) ?? 0, a = Number(ourEntry, armageddons[i]) ?? 0, larger = Math.Max(Math.Abs(g), Math.Abs(a));
                    if (larger > 0) apart += Math.Abs(g - a) / larger;
                }
                return apart;
            }
            return ourExplosions.OrderBy(t => Apart(t.Value)).ThenBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Value).FirstOrDefault();
        }
        var addedExplosions = rankedExplosions.Take(explosionRoom).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> ExplosionEffects(string explosion) => Leaves(theirExplosions[explosion], "Element");

        // Each effect's sound (Guerrilla's cue, made an event by soundOf: once a cue), told whether one of the
        // added explosions plays that cue.
        var explosionCues = addedExplosions.SelectMany(ExplosionEffects).Where(effects.ContainsKey).Select(e => Field(effects[e], "sound")).OfType<string>()
                                           .Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sounds = new Dictionary<string, Sound?>(StringComparer.OrdinalIgnoreCase);
        Sound? SoundOf(string effect)
        {
            if (soundOf == null || !effects.TryGetValue(effect, out string? entry)) return null;
            // Its cue, or with none its attack-loop-release set (sound_alr: a thruster, a generator), asked for by the set's name.
            string? cue = Field(entry, "sound") is { Length: > 0 } named ? named
                        : Field(entry, "sound_alr") is { Length: > 0 } set ? GuerrillaSoundBanks.AlrPrefix + set : null;
            if (cue == null) return null;
            if (!sounds.TryGetValue(cue, out var sound)) sounds[cue] = sound = soundOf(cue, explosionCues.Contains(cue));
            return sound;
        }

        // An effect is added for what it shows or for what it sounds (many of Guerrilla's have a sound alone).
        var effectsOf = used.Keys.Where(objects.ContainsKey).ToDictionary(p => p,
            p => EffectFields.SelectMany(f => Leaves(objects[p], f))
                             .Concat(explosionsOf[p].Where(addedExplosions.Contains).SelectMany(ExplosionEffects))
                             .Where(n => VisualOf(n) != null || SoundOf(n) != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            StringComparer.OrdinalIgnoreCase);

        // The visuals the most placed objects show, as many as the game has room for (the world's own
        // effects' first: shown everywhere).
        var placed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (p, list) in effectsOf)
            foreach (string v in list.Select(VisualOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
                placed[v] = placed.GetValueOrDefault(v) + used[p];
        var everywhere = worldEffects.Concat((worldExplosions ?? []).Where(addedExplosions.Contains).SelectMany(ExplosionEffects))
                                     .Where(e => VisualOf(e) != null || SoundOf(e) != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string e in everywhere)
            if (VisualOf(e) is { } v) placed[v] = int.MaxValue;
        // (The entries of its <Table>: the two in <TableTemplates> are the editor's.)
        int visualRoom = Math.Max(0, VisualLimit - Count(ourVisuals, "Effect"));
        var ranked = placed.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key).ToList();
        var addedVisuals = ranked.Take(visualRoom).Where(v => !takenVisuals.Contains(Added(v))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Whether an effect's visual is added (one past the game's room isn't: the effect then only sounds).
        bool Shown(string effect) => VisualOf(effect) is { } v && addedVisuals.Contains(v);
        var addedEffects = everywhere.Concat(effectsOf.Values.SelectMany(l => l)).Where(e => (Shown(e) || SoundOf(e) != null) && !takenEffects.Contains(Added(e)))
                                     .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int effectRoom = EffectLimit - Count(ourEffects, "Effect");
        if (addedEffects.Count > effectRoom) throw new InvalidDataException($"{addedEffects.Count} effects to add; the game has room for {effectRoom}");
        var effectSet = addedEffects.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The entries: Guerrilla's, renamed, with the fields above made Armageddon's, then fitted to the
        // shape of Armageddon's own (see Fit).
        var visualShape = new Shape(ourVisuals, "Effect", Text(guerrillaMisc, "vfx.xtbl"));
        var visualText = new StringBuilder();
        foreach (string v in ranked.Where(addedVisuals.Contains))
        {
            string body = Rename(visuals[v], Added(v));
            body = Replace(body, "Filename", Added(FileOf(visuals[v])) + ".effectx");
            body = Replace(body, "Streaming_Category", "Environmental");
            // Guerrilla's Start_time is seconds with a fraction; Armageddon's reader takes whole seconds (it cuts the
            // fraction off): the nearest whole second (Guerrilla's three fires' 3.5 → 4).
            body = Regex.Replace(body, @"<Start_time>\s*([^<]*?)\s*</Start_time>", m =>
                double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double seconds)
                    ? $"<Start_time>{(long)Math.Round(seconds, MidpointRounding.AwayFromZero)}</Start_time>" : m.Value);
            visualText.Append($"\t<Effect>{Fit(body, "Effect", visualShape)}</Effect>\n");
        }
        var effectShape = new Shape(ourEffects, "Effect", Text(guerrillaMisc, "effects.xtbl"));
        var effectText = new StringBuilder();
        foreach (string e in addedEffects)
        {
            string body = Rename(effects[e], Added(e));
            body = Shown(e) ? Replace(body, "visual", Added(VisualOf(e)!)) : Regex.Replace(body, @"\s*<visual>[^<]*</visual>", "");
            // Its sound: the event made of Guerrilla's cue (left out only where the cue can't be had). A looping
            // one is heard as far as Guerrilla's cue is (Armageddon starts and stops an effect's loop at a
            // distance: 30 m unless the effect names its own).
            var sound = SoundOf(e);
            body = sound == null ? Regex.Replace(body, @"\s*<sound>[^<]*</sound>", "")
                 : Field(body, "sound") != null ? Replace(body, "sound", sound.Event) : body + $"<sound>{sound.Event}</sound>";
            if (sound?.HeardTo is { } far)
                body += $"<Max_Sound_Play_Distance_Override_Meters>{Math.Round(far).ToString(CultureInfo.InvariantCulture)}</Max_Sound_Play_Distance_Override_Meters>";
            effectText.Append($"\t<Effect>{Fit(body, "Effect", effectShape)}</Effect>\n");
        }
        var objectShape = new Shape(ourObjects, "Level_Object", Text(guerrillaMisc, "level_objects.xtbl"));
        var objectText = new StringBuilder();
        var propsOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var filesOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var explosionsLeft = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in used.Keys.Where(objects.ContainsKey).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string name = ObjectName(p);
            if (takenObjects.Contains(name)) throw new InvalidDataException($"Armageddon's level_objects.xtbl already has an entry {name}");
            if (propsOf.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException($"two of Guerrilla's level objects would both be named {name}");
            string body = Rename(objects[p], name);
            foreach (string field in EffectFields)
                body = Regex.Replace(body, $@"(\s*)<{field}>([^<]*)</{field}>", m =>
                {
                    string effect = m.Groups[2].Value.Trim();
                    return effect.Length == 0 ? m.Value : effectSet.Contains(effect) ? $"{m.Groups[1].Value}<{field}>{Added(effect)}</{field}>" : "";
                });
            // Guerrilla's explosions where added, else Armageddon's of that name, else left out.
            body = Regex.Replace(body, @"(\s*)<Death_Explosion>([^<]*)</Death_Explosion>", m =>
            {
                string explosion = m.Groups[2].Value.Trim();
                if (addedExplosions.Contains(explosion)) return $"{m.Groups[1].Value}<Death_Explosion>{Added(explosion)}</Death_Explosion>";
                if (explosion.Length == 0 || explosions.Contains(explosion)) return m.Value;
                explosionsLeft.Add(explosion);
                return "";
            });
            objectText.Append($"\t<Level_Object>{Fit(body, "Level_Object", objectShape)}</Level_Object>\n");
            propsOf[p] = name;
            filesOf[p] = effectsOf[p].Where(e => effectSet.Contains(e) && Shown(e)).Select(e => FileOf(visuals[VisualOf(e)!])).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        // The explosions: Guerrilla's settings in Armageddon's fields (see ExplosionEntry), new unique
        // numbers after Armageddon's own.
        var explosionShape = new Shape(ourExplosionText, "Explosion", theirExplosionText);
        // The explosion flags Armageddon's table names (its description; its reader knows no others).
        var knownFlags = XtblFile.FromBytes(Encoding.Latin1.GetBytes(ourExplosionText)).Description?.Children
                             .FirstOrDefault(f => f.Name.Equals("Flags", StringComparison.OrdinalIgnoreCase))?.Flags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (FlagsLeftOut) FlagsLeftOut.Clear();
        var explosionText = new StringBuilder();
        int uniqueId = ourExplosions.Values.Select(x => int.TryParse(Field(x, "Unique_ID"), out int id) ? id : 0).DefaultIfEmpty(0).Max();
        foreach (string x in rankedExplosions.Where(addedExplosions.Contains))
        {
            string body = Fit(ExplosionEntry(theirExplosions[x], Added(x), DamageFrom(x), effectSet, ++uniqueId, knownFlags), "Explosion", explosionShape);
            // The game stops while it starts on an explosion without a field all of Armageddon's have
            // ("Could not find required attribute Explosion:Structural_Damage_Type"), so check here.
            var fields = XElement.Parse($"<Explosion>{body}</Explosion>").Elements().Select(e => e.Name.LocalName).ToHashSet();
            if (explosionShape.Always.TryGetValue("Explosion", out var always) && always.Where(f => !fields.Contains(f)).ToList() is { Count: > 0 } missing)
                throw new InvalidDataException($"the added explosion {Added(x)} has no {string.Join(", ", missing)}, which all of Armageddon's have");
            explosionText.Append($"\t<Explosion>{body}</Explosion>\n");
        }

        var files = addedVisuals.Select(v => FileOf(visuals[v])).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var worldFiles = everywhere.Where(e => effectSet.Contains(e) && Shown(e)).Select(e => FileOf(visuals[VisualOf(e)!])).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new Tables(Append(ourVisuals, visualText), Append(ourEffects, effectText), Append(ourObjects, objectText), Append(ourExplosionText, explosionText),
                          propsOf, filesOf, files, worldFiles, addedVisuals.Count, addedEffects.Count, addedExplosions.Count, ranked.Skip(visualRoom).ToList(),
                          rankedExplosions.Skip(explosionRoom).ToList(), [.. explosionsLeft])
        {
            SoundsAdded = addedEffects.Count(e => SoundOf(e) != null),
            SoundOnly = addedEffects.Count(e => !Shown(e)),
            SoundsLeft = [.. addedEffects.Select(e => Field(effects[e], "sound")).OfType<string>().Where(c => c.Length > 0 && sounds.GetValueOrDefault(c) == null)
                                         .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)],
        };
    }

    /// <summary>
    /// An effect's sound as Armageddon's table names it: the event's name, and for a looping one how far off (m)
    /// it is heard (the effect's Max_Sound_Play_Distance_Override_Meters).
    /// </summary>
    public sealed record Sound(string Event, float? HeardTo);

    /// <summary>
    /// One of Guerrilla's explosions as an Armageddon entry named <paramref name="name"/>: Guerrilla's
    /// fields (Impulse and Structural_Damage under Armageddon's names, see <see cref="ExplosionFieldNames"/>),
    /// its effects in Armageddon's form (each an Effect with its Name, the added one, and Play_Style
    /// Always; those not added left out), and the fields only Armageddon's have (how it damages
    /// buildings: Material_Damage_Class_Inner and _Outer, the Structural_Damage_Type shape cutters, its
    /// radii, Electrical_Explosion) from <paramref name="ours"/>: Armageddon's entry of the same name,
    /// or of the explosion Guerrilla's table gives the same damage (see DamageFrom in Build).
    /// </summary>
    static string ExplosionEntry(string theirs, string name, string? ours, ISet<string> addedEffects, int uniqueId, ISet<string>? knownFlags)
    {
        var entry = XElement.Parse($"<Explosion>{Rename(theirs, name)}</Explosion>");
        // Its flags Armageddon's table names, the others left out (see FlagsLeftOut).
        if (knownFlags != null && entry.Element("Flags") is { } flags)
            foreach (var flag in flags.Elements().Where(f => !knownFlags.Contains(f.Value.Trim())).ToList())
            {
                lock (FlagsLeftOut) FlagsLeftOut.Add($"{name}: {flag.Value.Trim()}");
                flag.Remove();
            }
        foreach (var (guerrilla, armageddon) in ExplosionFieldNames)
            foreach (var e in entry.Elements(guerrilla).ToList())
                e.Name = armageddon;
        if (entry.Element("Effects") is { } list)
            list.ReplaceNodes(list.Elements("Element").Select(e => e.Value.Trim()).Where(addedEffects.Contains)
                                  .Select(e => new XElement("Effect", new XElement("Name", Added(e)), new XElement("Play_Style", "Always"))));
        if (entry.Element("Unique_ID") is { } id) id.Value = uniqueId.ToString();
        if (ours != null)
            foreach (var e in XElement.Parse($"<Explosion>{ours}</Explosion>").Elements())
                if (entry.Element(e.Name) == null && e.Name.LocalName != "Effects") entry.Add(e);  // (never Armageddon's effects)
        return string.Concat(entry.Nodes().Select(n => Write(n, 2)));
    }

    /// <summary>
    /// The effect files an object needs in its level, by its level object (Props): the files of the
    /// visuals its effects show (its Death_Effect, impacts, custom effects...) and its explosions'
    /// effects show (Death_Explosion), those loaded with the level (Streaming_Category other than
    /// Preload: Preload ones come from effects.vpp_pc at start-up, everywhere). From the four tables
    /// as the game reads them (Armageddon's, or the guerrilla_world mod's with Guerrilla's added).
    /// For the level editor: an object brought into another level brings its effects' files.
    /// </summary>
    public sealed class EffectLookup(string levelObjects, string effects, string visuals, string explosions)
    {
        readonly Dictionary<string, string> objects = Entries(levelObjects, "Level_Object"), effectEntries = Entries(effects, "Effect"),
                                            visualEntries = Entries(visuals, "Effect"), explosionEntries = Entries(explosions, "Explosion");

        /// <summary>The effect files (names without extension, as vfx.xtbl's Filename) a level object's effects need in the level.</summary>
        public IEnumerable<string> FilesOf(string levelObject)
        {
            if (!objects.TryGetValue(levelObject, out string? entry)) yield break;
            var names = EffectFields.SelectMany(f => Leaves(entry, f)).ToList();
            foreach (string explosion in Leaves(entry, "Death_Explosion"))
                if (explosionEntries.TryGetValue(explosion, out string? x))
                    names.AddRange(Leaves(x, "Name").Skip(1).Concat(Leaves(x, "Element")));  // its effects (the first Name is its own)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string effect in names)
            {
                if (!effectEntries.TryGetValue(effect, out string? e) || Field(e, "visual") is not { Length: > 0 } visual ||
                    !visualEntries.TryGetValue(visual, out string? v) || Field(v, "Filename") is not { Length: > 0 } file) continue;
                if (string.Equals(Field(v, "Streaming_Category"), "Preload", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (seen.Add(name)) yield return name;
            }
        }
    }

    /// <summary>A visual's file (its Filename without ".effectx").</summary>
    static string FileOf(string visualEntry) =>
        Path.GetFileNameWithoutExtension(Field(visualEntry, "Filename") ?? throw new InvalidDataException("a visual effect without a file"));

    /// <summary>A table's entries (its &lt;Table&gt;'s elements of that tag), by name, as written (the first of a name).</summary>
    static Dictionary<string, string> Entries(string text, string tag)
    {
        string table = Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline) is { Success: true } t ? t.Groups[1].Value
            : throw new InvalidDataException("a table file without a table");
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(table, $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline))
            if (Field(m.Groups[1].Value, "Name") is { Length: > 0 } name) entries.TryAdd(name, m.Groups[1].Value);
        return entries;
    }

    /// <summary>
    /// What a table's own entries hold, element by element: for each element's tag, the children every
    /// element of that tag has, and each such child's commonest text where it holds one. Tags Armageddon's
    /// entries never have (Guerrilla's Melee_impact, which the game still reads and requires a definition
    /// in) take their shape from Guerrilla's table.
    /// </summary>
    sealed class Shape
    {
        public readonly Dictionary<string, HashSet<string>> Always = [];
        public readonly Dictionary<(string Parent, string Child), string> Commonest = [];

        public Shape(string table, string tag, string guerrillaTable)
        {
            Learn(table, tag);
            var guerrilla = new Shape();
            guerrilla.Learn(guerrillaTable, tag);
            foreach (var (name, always) in guerrilla.Always) Always.TryAdd(name, always);
            foreach (var (key, value) in guerrilla.Commonest) Commonest.TryAdd(key, value);
        }

        Shape() { }

        void Learn(string table, string tag)
        {
            var seen = new Dictionary<string, int>();
            var children = new Dictionary<string, Dictionary<string, int>>();
            var texts = new Dictionary<(string, string), Dictionary<string, int>>();
            foreach (string body in Regex.Matches(Regex.Match(table, @"<Table>(.*?)</Table>", RegexOptions.Singleline).Groups[1].Value,
                                                  $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline).Select(m => m.Groups[1].Value))
                foreach (var e in XElement.Parse($"<{tag}>{body}</{tag}>").DescendantsAndSelf())
                {
                    string name = e.Name.LocalName;
                    seen[name] = seen.GetValueOrDefault(name) + 1;
                    var mine = children.TryGetValue(name, out var c) ? c : children[name] = [];
                    foreach (var child in e.Elements().GroupBy(x => x.Name.LocalName))
                    {
                        mine[child.Key] = mine.GetValueOrDefault(child.Key) + 1;
                        if (!child.First().HasElements)
                        {
                            var counts = texts.TryGetValue((name, child.Key), out var t) ? t : texts[(name, child.Key)] = [];
                            counts[child.First().Value.Trim()] = counts.GetValueOrDefault(child.First().Value.Trim()) + 1;
                        }
                    }
                }
            foreach (var (name, count) in seen)
                Always[name] = [.. children[name].Where(c => c.Value == count).Select(c => c.Key)];
            foreach (var (key, counts) in texts)
                Commonest[key] = counts.MaxBy(c => c.Value).Key;
        }
    }

    /// <summary>
    /// An entry's text fitted to the shape of the table's own entries, which the game may require (a
    /// custom_effects without an effect stopped it while it started: "Could not find required attribute
    /// custom_effects:effect"): where every one of Armageddon's elements of a tag has a child this one
    /// lacks, a child holding a word (Kill_On_Detach, a bullet impact's Bullet_group, an effect's
    /// ignore_gpu_occluded) is added with Armageddon's commonest, and an element missing one that names
    /// another entry (an effect left out, an explosion Armageddon hasn't) or holds others goes, until
    /// none lacks anything. Empty elements are written open and closed, as the game's own.
    /// </summary>
    static string Fit(string body, string tag, Shape shape)
    {
        var entry = XElement.Parse($"<{tag}>{body}</{tag}>");
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var e in entry.DescendantsAndSelf().Reverse().ToList())
            {
                if (e != entry && e.Parent == null) continue;  // gone with its parent
                if (!shape.Always.TryGetValue(e.Name.LocalName, out var always)) continue;
                foreach (string child in always.Where(c => e.Element(c) == null).ToList())
                {
                    if (!References.Contains(child) && shape.Commonest.TryGetValue((e.Name.LocalName, child), out string? value))
                        e.Add(new XElement(child, value));
                    else if (e != entry)
                    {
                        e.Remove();
                        break;
                    }
                    else continue;
                    changed = true;
                }
                if (e != entry && e.Parent == null) changed = true;
            }
        }
        return string.Concat(entry.Nodes().Select(n => Write(n, 2)));
    }

    /// <summary>A node written as the game's tables are: tabs, each element on its line, an empty one open and closed.</summary>
    static string Write(XNode node, int depth)
    {
        if (node is not XElement e) return "";
        string tabs = "\n" + new string('\t', depth), name = e.Name.LocalName;
        if (!e.HasElements) return $"{tabs}<{name}>{System.Security.SecurityElement.Escape(e.Value)}</{name}>";
        return $"{tabs}<{name}>{string.Concat(e.Elements().Select(c => Write(c, depth + 1)))}{tabs}</{name}>";
    }

    /// <summary>How many entries of that tag a table has (names repeated or not).</summary>
    static int Count(string text, string tag) =>
        Regex.Matches(Regex.Match(text, @"<Table>(.*?)</Table>", RegexOptions.Singleline).Groups[1].Value, $@"<{tag}>.*?</{tag}>", RegexOptions.Singleline).Count;

    /// <summary>The first such field's text in an entry, or null.</summary>
    static string? Field(string entry, string field) =>
        Regex.Match(entry, $@"<{field}>([^<]*)</{field}>", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    /// <summary>Every such field's text in an entry (only those holding text).</summary>
    static IEnumerable<string> Leaves(string entry, string field) =>
        Regex.Matches(entry, $@"<{field}>([^<]*)</{field}>").Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0);

    /// <summary>An entry with its (first) name made <paramref name="name"/>.</summary>
    static string Rename(string entry, string name) => new Regex(@"<Name>[^<]*</Name>").Replace(entry, $"<Name>{name}</Name>", 1);

    /// <summary>An entry with its first such field's text made <paramref name="value"/>.</summary>
    static string Replace(string entry, string field, string value) =>
        new Regex($@"<{field}>[^<]*</{field}>", RegexOptions.IgnoreCase).Replace(entry, $"<{field}>{value}</{field}>", 1);

    /// <summary>A table with entries added as its last, before its closing tag, in its own line ends.</summary>
    static byte[] Append(string table, StringBuilder entries)
    {
        int end = table.IndexOf("</Table>", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("a table file without a table");
        int at = table.LastIndexOf('\n', end) + 1;
        string newline = table.Contains("\r\n") ? "\r\n" : "\n";
        return Encoding.Latin1.GetBytes(table[..at] + entries.ToString().Replace("\r\n", "\n").Replace("\n", newline) + table[at..]);
    }
}
