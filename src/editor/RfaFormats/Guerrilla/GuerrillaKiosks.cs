using System.Globalization;
using System.Text;
using RfaFormats.Tables;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's kiosks for the guerrilla_kiosks mod: the EDF's news broadcast, part of Guerrilla's public
/// address system (kiosk.cpp), which Armageddon no longer has.
///
/// A kiosk is a mover whose gameplay properties have the chunk flag <c>kiosk</c> (Guerrilla's kiosk_mover_is_kiosk):
/// the EDF tech kiosk (tech_kiosk_EDF_A, level object EDFKiosk, four screens) and the bars' radio
/// (tech_radio_industrial, no screens). Guerrilla's code, not its tables, plays the pictures on a kiosk's screens:
/// its script points flagged "Kiosk Large Screen" or "Kiosk Small Screen" carry no effect of their own, and the code
/// plays kiosk_screens.xtbl's Large_Effect or Small_Effect on each (kiosk_start_effect_on_screen). So:
/// - <see cref="Effects"/>: the screens' effects (28, env_news_kiosk_*), for the world's effect tables to add as
///   effects the world plays everywhere (Guerrilla preloads them at start-up: Streaming_Category Preload);
/// - <see cref="Add"/> and <see cref="Write"/>: the mod's folder, mods\guerrilla_kiosks\: Guerrilla's own
///   pa_defaults.xtbl, kiosk_screens.xtbl and kiosk_lines.xtbl as they are (only the mod reads them, with its own
///   reader, as guerrilla_campaign reads its tables), and the kiosks the world places (<see cref="ListFile"/>).
/// </summary>
public sealed class GuerrillaKiosks
{
    /// <summary>The mod's folder name, beside mods\guerrilla_world\.</summary>
    public const string ModName = "guerrilla_kiosks";

    /// <summary>
    /// The kiosks the world places, in the mod's folder, a line each: <c>zone handle parent gameplay_props props x y z</c>
    /// (handles hex, FFFFFFFF none; the place in metres). The mod finds each by its handle as its zone loads.
    /// </summary>
    public const string ListFile = "kiosks.txt";

    /// <summary>Guerrilla's tables the mod reads, copied as they are from Guerrilla's misc.vpp_pc.</summary>
    public static readonly string[] Tables = ["pa_defaults.xtbl", "kiosk_screens.xtbl", "kiosk_lines.xtbl"];

    /// <summary>The chunk flag that makes a mover a kiosk (Guerrilla's CHUNK_FLAG_KIOSK, 0x4000; Armageddon's reader keeps the name).</summary>
    public const string KioskFlag = "kiosk";

    static readonly uint RfgMover = NameHashes.ClassHash("rfg_mover"), GeneralMover = NameHashes.ClassHash("general_mover");

    readonly Packfile guerrillaTables;
    readonly HashSet<string> kioskEntries;
    readonly List<string> lines = [];

    /// <param name="guerrillaTables">Guerrilla's misc.vpp_pc (its tables).</param>
    /// <param name="gameplayTable">The world's built gameplay_properties.xtbl: the entries with the kiosk flag are the kiosks'.</param>
    public GuerrillaKiosks(Packfile guerrillaTables, byte[] gameplayTable)
    {
        this.guerrillaTables = guerrillaTables;
        kioskEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in XtblFile.FromBytes(gameplayTable).Entries)
            if (entry.Element("chunk_flags")?.ElementsNamed("Flag").Any(f => f.Value.Trim().Equals(KioskFlag, StringComparison.OrdinalIgnoreCase)) == true)
                kioskEntries.Add(XtblFile.EntryName(entry));
    }

    /// <summary>
    /// The effects kiosk_screens.xtbl names (each screen's Large_Effect and Small_Effect), Guerrilla's effects.xtbl names:
    /// Guerrilla's code plays them, so no level object names them.
    /// </summary>
    public static IEnumerable<string> Effects(Packfile guerrillaTables)
    {
        if (guerrillaTables.Find("kiosk_screens.xtbl") is not { } index) return [];
        return XtblFile.FromBytes(guerrillaTables.Read(index)).Entries
            .SelectMany(e => new[] { e.ValueOf("Large_Effect"), e.ValueOf("Small_Effect") })
            .Select(n => n?.Trim() ?? "").Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>A zone's kiosks: its movers whose gameplay properties have the kiosk flag.</summary>
    public void Add(string zone, IEnumerable<ZoneObject> objects)
    {
        foreach (var o in objects.Where(o => o.ClassHash == RfgMover || o.ClassHash == GeneralMover))
        {
            string gameplay = o.Property("gameplay_props")?.Text?.Trim() ?? "";
            if (gameplay.Length == 0 || !(kioskEntries.Contains(gameplay) || kioskEntries.Contains(GuerrillaEffectTables.Added(gameplay)))) continue;
            string props = o.Property("Props")?.Text?.Trim() is { Length: > 0 } p ? p : "-";
            var at = o.Placement.Position;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{zone} {o.Handle:X8} {o.Parent:X8} {gameplay} {props} {at.X:0.##} {at.Y:0.##} {at.Z:0.##}"));
        }
    }

    /// <summary>The mod's folder, beside the world's (mods\guerrilla_world\).</summary>
    public static string Folder(string worldModFolder) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(worldModFolder))!, ModName);

    /// <summary>
    /// What <see cref="Write"/> wrote, deleted (the world removed): the tables, the lists and the broadcast's banks
    /// (their files, rows and stop pairs: <see cref="GuerrillaSoundBanks.Remove"/>); the mod's own files (its save) stay.
    /// </summary>
    public static IEnumerable<string> Remove(string game, string overrides, string worldModFolder)
    {
        string folder = Folder(worldModFolder);
        foreach (string file in Tables.Append(ListFile).Append(SoundList).Select(f => Path.Combine(folder, f)).Where(File.Exists))
        {
            File.Delete(file);
            yield return $"Deleted {file}";
        }
        foreach (string line in GuerrillaSoundBanks.Remove(game, overrides, worldModFolder, SoundPart)) yield return line;
        foreach (string line in GuerrillaSoundBanks.Remove(game, overrides, worldModFolder, VoicePart)) yield return line;
    }

    /// <summary>
    /// Writes the mod's folder (beside <paramref name="worldModFolder"/>, mods\guerrilla_world\): Guerrilla's three
    /// tables, the kiosk list, and the broadcast's sounds (<see cref="WriteSounds"/>). Returns what to log.
    /// </summary>
    public IEnumerable<string> Write(string worldModFolder, string guerrillaCache, string game, string overrides)
    {
        string folder = Folder(worldModFolder);
        Directory.CreateDirectory(folder);
        var missing = new List<string>();
        foreach (string table in Tables)
        {
            if (guerrillaTables.Find(table) is { } index) File.WriteAllBytes(Path.Combine(folder, table), guerrillaTables.Read(index));
            else missing.Add(table);
        }
        File.WriteAllLines(Path.Combine(folder, ListFile), lines, new UTF8Encoding(false));
        var byKind = lines.GroupBy(l => l.Split(' ')[3], StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Count()} {g.Key}");
        yield return $"{lines.Count} written to mods\\{ModName}\\{ListFile} ({string.Join(", ", byKind)}), movers whose gameplay properties have the " +
                     $"kiosk flag ({string.Join(", ", kioskEntries.Order(StringComparer.OrdinalIgnoreCase))}); Guerrilla's " +
                     $"{string.Join(", ", Tables.Except(missing))} copied beside it for the mod" +
                     (missing.Count > 0 ? $"; not in Guerrilla's misc.vpp_pc: {string.Join(", ", missing)} (the mod's broadcast stays off)" : "");
        if (missing.Count > 0) yield break;
        foreach (string line in WriteSounds(folder, guerrillaCache, game, overrides, worldModFolder)) yield return line;
    }

    // ---- The broadcast's sounds

    /// <summary>The sound part's name (its list in mods\guerrilla_world\: kiosks_sound_banks.txt) and its banks' prefix.</summary>
    public const string SoundPart = "kiosks", SoundBankPrefix = "rfgk_";

    /// <summary>
    /// The newsreader's own part (kiosk_voices_sound_banks.txt): its bank is Wwise ADPCM (<see cref="VoicesAdpcm"/>),
    /// which a part sets for all its banks, while the music's loop must stay PCM (the writer has no ADPCM loop yet).
    /// </summary>
    public const string VoicePart = "kiosk_voices";

    /// <summary>
    /// The newsreader's lines as Wwise ADPCM (36 bytes for 64 samples: 33 MB, where PCM is 109 MB), the first of
    /// Guerrilla's voices in that codec. Armageddon's sound engine has it built in (its source factory 0xEB0470 makes
    /// an ADPCM source for codec 2 as it does a PCM one for codec 1), but no bank of the game's own uses it, so this is
    /// its first test in the game; false goes back to PCM.
    /// </summary>
    public const bool VoicesAdpcm = true;

    /// <summary>
    /// The broadcast's sounds for the mod, in its folder, a line each (lengths in milliseconds):
    /// <c>music intro|loop|outro &lt;event&gt; &lt;stop event or -&gt; &lt;ms&gt;</c> and
    /// <c>line &lt;voice line&gt; &lt;event&gt; &lt;stop event&gt; &lt;ms&gt;</c>.
    /// </summary>
    public const string SoundList = "kiosk_sounds.txt";

    /// <summary>
    /// Armageddon's bus for spoken lines (init.bnk_pc): the parent of the buses its voice banks' sounds play on
    /// (0x20F80116 its missions' radio and orders, 0x3E205B1F its heroes', ...), -1 dB, no limit on how many play at once.
    /// </summary>
    public const uint DialogueBus = 0x36306EDD;

    /// <summary>
    /// A test (2026-11-13): in game every 3D sound of ours plays at full volume and centred, while the game places them
    /// right. So the newsreader's lines take Armageddon's own attenuation, that of its large fire (efx.bnk_pc, loaded at
    /// boot: 0 dB at 0 m to -96.3 dB at 25 m), and the music keeps ours (5-70 m). If the newsreader is gone by 25 m and
    /// the music isn't, our attenuations are what Wwise ignores; if neither fades, it isn't the attenuation.
    /// </summary>
    public const uint TestArmageddonAttenuation = 0x2CBFCEE4;

    /// <summary>
    /// Builds the broadcast's sounds (Guerrilla's pa_defaults.xtbl names them) into banks of Armageddon's kind:
    /// - its music (Kiosk_Music_Stinger, _Loop, _Out: foley.aud_pc's AMB_EDF_KIOSK_INTRO, _LOOP, _OUTRO), 3D at the
    ///   kiosk with Guerrilla's distances: the intro and outro once on Armageddon's effects' bus, the loop on its
    ///   effects' loops' bus with its stop event, as the world's effect sounds (<see cref="GuerrillaSoundBanks.EffectSound"/>);
    /// - its voice: the kiosks' persona's lines (kiosk_lines.xtbl's and the station ID), each an event (RFG_ + the
    ///   line) and its stop event, 3D at the kiosk with the line's distances and volume, on <see cref="DialogueBus"/>.
    /// The banks' media are streamed from their files (every sound streamed, the table's ram_bank_pc False: the
    /// game reads a bank's media into memory only for ram_bank_pc True, 0xB90450 → +0x57 bit 3 → 0xB90890), so
    /// only the small banks of events stay in memory.
    /// </summary>
    IEnumerable<string> WriteSounds(string folder, string guerrillaCache, string game, string overrides, string worldModFolder)
    {
        var defaults = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("pa_defaults.xtbl")!)).Entries.FirstOrDefault();
        string? Setting(string name) => defaults == null ? null : Deep(defaults, name);
        var banks = new GuerrillaSoundBanks(guerrillaCache, guerrillaTables, SoundPart, SoundBankPrefix);
        var list = new List<string>();
        var problems = new List<string>();

        // The music.
        void Music(string kind, string field, bool loops)
        {
            if (Setting(field) is not { } cue) { problems.Add($"pa_defaults.xtbl has no {field}"); return; }
            string? played;
            if (loops)
                played = banks.Loop(cue, WwiseBankWriter.EffectLoopsBus, 100, "_KIOSK",
                                    new WwiseBankWriter.Settings { Volume = WwiseBankWriter.EffectLoopBusDifference, VirtualWhenQuiet = true,
                                                                   Priority = banks.Sounds.FindCue(cue) is { } c ? GuerrillaSoundBanks.Priority(c, true) : (byte)70 });
            else
                played = banks.OneShot(cue, WwiseBankWriter.EffectsBus, "_KIOSK");
            if (played == null) { problems.Add($"{field} {cue} couldn't be had"); return; }
            int ms = 0;
            if (!loops && banks.Sounds.FindCue(cue) is { } once)
                try
                {
                    var (waveBank, wave) = banks.Sounds.SoundOf(once).Waves[0];
                    var w = banks.Sounds.ReadWave(waveBank, wave);
                    ms = (int)Math.Round(1000.0 * w.Samples.Length / w.Channels / w.Rate);
                }
                catch (Exception e) when (e is InvalidDataException or NotSupportedException) { problems.Add($"{cue}: {e.Message}"); }
            list.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"music {kind} {played} {(loops ? played + "_STOP" : "-")} {ms}"));
        }
        Music("intro", "Kiosk_Music_Stinger", false);
        Music("loop", "Kiosk_Music_Loop", true);
        Music("outro", "Kiosk_Music_Out", false);

        // The voice: its own part, in Wwise ADPCM (VoicesAdpcm).
        var voiceBanks = new GuerrillaSoundBanks(guerrillaCache, guerrillaTables, VoicePart, SoundBankPrefix) { Adpcm = VoicesAdpcm };
        string persona = Setting("Kiosk_persona") ?? "KIOSK";
        var voices = GuerrillaVoices.Open(guerrillaCache, persona);
        int lineCount = 0;
        double seconds = 0;
        WwiseBankWriter? voiceBank = null;
        if (voices == null) problems.Add($"no persona {persona} in Guerrilla's personas.vpp_pc / voices_r.vpp_pc");
        else
        {
            var names = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("kiosk_lines.xtbl")!)).Entries
                .Select(e => e.ValueOf("Name")?.Trim() ?? "").Where(n => n.Length > 0).ToList();
            if (Setting("Kiosk_Station_ID") is { } stationId) names.Add(stationId);
            foreach (string name in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                GuerrillaVoices.Line? line;
                GuerrillaSounds.Wave wave;
                try
                {
                    line = voices.Find(name);
                    if (line == null) { problems.Add($"{persona} has no line {name}"); continue; }
                    wave = voices.ReadWave(line);
                }
                catch (Exception e) when (e is InvalidDataException or NotSupportedException) { problems.Add($"{name}: {e.Message}"); continue; }
                voiceBank ??= voiceBanks.Bank(line.WaveBank);
                string key = "line:" + name, eventName = GuerrillaSoundBanks.EventName(name.ToUpperInvariant());
                var settings = new WwiseBankWriter.Settings
                {
                    Volume = GuerrillaSoundBanks.Db(line.Volume) + line.SoundDb, Priority = GuerrillaSoundBanks.Priority(line.Rank, false),
                };
                uint sound = voiceBank.Sound(key, voiceBank.Medium(wave), false, settings, 0,
                                             new WwiseBankWriter.Output(DialogueBus, TestArmageddonAttenuation, true));
                voiceBank.Event(eventName, voiceBank.Play(key, sound));
                voiceBank.Event(eventName + "_STOP", voiceBank.Stop(key, sound, 200));
                double length = (double)wave.Samples.Length / wave.Channels / wave.Rate;
                seconds += length;
                lineCount++;
                list.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                                       $"line {name} {eventName} {eventName}_STOP {(int)Math.Round(length * 1000)}"));
            }
        }
        File.WriteAllLines(Path.Combine(folder, SoundList), list, new UTF8Encoding(false));
        // The music's part first: it takes away the banks it wrote before and doesn't now (an earlier build's PCM
        // newsreader bank, of the same name), then the voices' part writes theirs.
        foreach (string line in banks.Write(game, overrides, worldModFolder)) yield return "sounds: " + line;
        foreach (string line in voiceBanks.Write(game, overrides, worldModFolder)) yield return "voice sounds: " + line;
        var check = voiceBank?.AdpcmCheck ?? (0d, 0d, 0);
        string voiceSize = voiceBank == null ? "" :
            $"; {voiceBank.Name}: {lineCount} lines, {seconds / 60:0.#} min, media {voiceBank.MediaBytes.Written / 1048576.0:0.#} MB " +
            (voiceBank.Adpcm
                ? $"(Wwise ADPCM, {voiceBank.MediaBytes.Pcm / 1048576.0:0.#} MB as PCM; decoded again they are {check.Sum / Math.Max(1, check.Count):0.#} dB " +
                  $"from Guerrilla's waves on average, the worst {check.Worst:0.#} dB)"
                : "(PCM)") +
            ", every sound streamed from the media file (ram_bank_pc False): only its events' bank stays in memory";
        yield return $"broadcast sounds: {list.Count(l => l.StartsWith("music "))} music cues and {lineCount} voice lines (persona {persona}) in " +
                     $"mods\\{ModName}\\{SoundList}{voiceSize}" + (problems.Count > 0 ? $"; left out: {string.Join("; ", problems)}" : "") +
                     (banks.Log.Count + voiceBanks.Log.Count > 0 ? $" ({string.Join("; ", banks.Log.Concat(voiceBanks.Log).Distinct())})" : "");
    }

    /// <summary>A field's text anywhere under an element (null if it isn't there or is empty).</summary>
    static string? Deep(XtblElement e, string name)
    {
        foreach (var c in e.Elements)
        {
            if (c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !c.HasElements) return c.Value?.Trim() is { Length: > 0 } text ? text : null;
            if (c.HasElements && Deep(c, name) is { } found) return found;
        }
        return null;
    }
}
