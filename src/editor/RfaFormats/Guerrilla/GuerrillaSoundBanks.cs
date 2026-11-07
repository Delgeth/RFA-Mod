using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's sounds as banks of Armageddon's kind, for any part of the builder (the vehicles, the guns):
/// each Guerrilla cue becomes a Wwise event named "RFG_" + the cue's name (<see cref="EventName"/>), which
/// Armageddon's tables can name wherever they name a sound. A part makes one of these, asks for its cues
/// (<see cref="OneShot"/>, or builds its own objects into <see cref="Bank"/>), then <see cref="Write"/>s:
///   - the banks, one for each of Guerrilla's sound banks the cues are in (<see cref="BankPrefix"/> + its
///     name: rfg_veh_civ, ...), new files in overrides\build\pc\cache\ (the bank and its media);
///   - their rows in audio_banks_patch.xtbl (mods\guerrilla_world\, which that mod hands the game; the
///     rows of the other parts and Armageddon's own kept), streaming and loaded at boot;
///   - their loops' stop events, as pairs (loop event, stop event) in an override of the game's
///     audio_info.vad_pc (Armageddon's pairs and the other parts' kept): stopping a looping sound, the game
///     posts its pair's stop event.
/// Each part lists what it wrote in its own file in the mod's folder (<see cref="Remove"/> takes it away).
///
/// A one-shot cue is played as Guerrilla has it: its wave (or one of its waves at random, not the same
/// twice running), its volume (the cue's gain and its XACT sound's and track's dB), at the game object in
/// 3D with Guerrilla's distances for it (foley_settings.xtbl Min_Max_Distances: full volume to the
/// nearer, then falling to nothing at the farther, as a straight line of the gain), as many at once as
/// the cue allows.
/// </summary>
public sealed class GuerrillaSoundBanks
{
    /// <summary>The bank table the game reads after audio_banks.xtbl; written into the guerrilla_world mod's folder.</summary>
    public const string BankTable = "audio_banks_patch.xtbl";

    /// <summary>The game's loops' stop events (Volition's "VWAI"), overridden in its archive.</summary>
    public const string StopPairFile = "audio_info.vad_pc", StopPairArchive = "sounds.vpp_pc";

    /// <summary>A Guerrilla cue's event name in our banks.</summary>
    public static string EventName(string cue) => "RFG_" + cue;

    public GuerrillaSounds Sounds { get; }

    /// <summary>The banks' names start with this (rfg_ for the vehicles; another part its own, so that two parts never write the same file).</summary>
    public string BankPrefix { get; }

    readonly string part;
    readonly List<(float Min, float Max)> distances;
    readonly Dictionary<string, WwiseBankWriter> banks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<uint, uint> stopPairs = [];
    readonly List<string> log = [];

    /// <param name="part">Names the part's list file (<c>&lt;part&gt;_sound_banks.txt</c>).</param>
    public GuerrillaSoundBanks(string guerrillaCache, Packfile guerrillaTables, string part, string bankPrefix)
    {
        this.part = part;
        BankPrefix = bankPrefix;
        Sounds = new GuerrillaSounds(guerrillaCache);
        distances = Distances(guerrillaTables);
    }

    /// <summary>Lines for the builder's log (what couldn't be had).</summary>
    public IReadOnlyList<string> Log => log;

    /// <summary>Foley_settings.xtbl's Min_Max_Distances, in order (a cue names one by its number).</summary>
    static List<(float, float)> Distances(Packfile guerrillaTables)
    {
        var table = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("foley_settings.xtbl") ?? throw new InvalidDataException("no foley_settings.xtbl in Guerrilla's misc.vpp_pc")));
        var list = new List<(float, float)>();
        void Walk(XtblElement e)
        {
            foreach (var c in e.Elements)
            {
                if (c.Name.Equals("Min_Max", StringComparison.OrdinalIgnoreCase))
                    list.Add((Number(c.ValueOf("Min_Distance")), Number(c.ValueOf("Max_Distance"))));
                else if (c.HasElements) Walk(c);
            }
        }
        foreach (var entry in table.Entries) Walk(entry);
        return list;
    }

    public static float Number(string? text) =>
        float.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 0;

    /// <summary>The bank for a Guerrilla sound bank (made the first time).</summary>
    public WwiseBankWriter Bank(string guerrillaSoundBank)
    {
        string name = BankPrefix + guerrillaSoundBank.ToLowerInvariant();
        if (!banks.TryGetValue(name, out var bank)) banks[name] = bank = new WwiseBankWriter(name);
        return bank;
    }

    /// <summary>Guerrilla's distances number <paramref name="index"/> as an attenuation in <paramref name="bank"/>: full volume to the nearer, the gain falling in a straight line to nothing at the farther.</summary>
    public uint Attenuation(WwiseBankWriter bank, int index)
    {
        var (near, far) = index >= 0 && index < distances.Count ? distances[index] : (3f, 50f);
        if (far <= near) far = near + 1;
        var points = new List<(float, float)> { (0, 0), (near, 0) };
        for (int k = 1; k <= 8; k++)
        {
            float t = k / 8f;
            points.Add((near + (far - near) * t, k == 8 ? WwiseBankWriter.Silent : Db(1 - t)));
        }
        return bank.Attenuation($"{near}-{far}", points);
    }

    /// <summary>A gain as dB (Wwise's silence at most).</summary>
    public static float Db(double gain) => gain <= 0 ? WwiseBankWriter.Silent : (float)Math.Max(WwiseBankWriter.Silent, 20 * Math.Log10(gain));

    /// <summary>
    /// A cue's sound as an object in <paramref name="bank"/> (under <paramref name="key"/>): a sound, or a random
    /// container of its waves' sounds; hanging from <paramref name="parent"/>, or (0) out to
    /// <paramref name="output"/>. <paramref name="settings"/> (curves, how many at once) may be given;
    /// its volume is the cue's added to the one given. Null if the cue or its waves can't be had (logged).
    /// </summary>
    public uint? CueObject(WwiseBankWriter bank, string cueName, string key, bool loops, WwiseBankWriter.Settings? settings = null,
                           uint parent = 0, WwiseBankWriter.Output? output = null)
    {
        var cue = Sounds.FindCue(cueName);
        if (cue == null) { log.Add($"no cue {cueName} in Guerrilla's foley.aud_pc"); return null; }
        GuerrillaSounds.Sound sound;
        try { sound = Sounds.SoundOf(cue); }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException) { log.Add($"{cueName}: {e.Message}"); return null; }
        settings ??= new WwiseBankWriter.Settings();
        settings.Volume += Db(cue.Volume) + sound.VolumeDb;
        settings.Pitch += sound.PitchCents;
        if (settings.MostAtOnce == 0 && cue.MostAtOnce > 0) settings.MostAtOnce = (ushort)cue.MostAtOnce;
        if (sound.Waves.Count == 1 || !sound.Picks)
        {
            var (waveBank, wave) = sound.Waves[0];
            return bank.Sound(key, bank.Medium(Sounds.ReadWave(waveBank, wave)), loops, settings, parent, output);
        }
        uint container = bank.ObjectId("random:" + key);
        var children = new List<uint>();
        int n = 0;
        foreach (var (waveBank, wave) in sound.Waves)
            children.Add(bank.Sound($"{key}#{n++}", bank.Medium(Sounds.ReadWave(waveBank, wave)), loops, new WwiseBankWriter.Settings(), container));
        return bank.Random(key, children, settings, parent, output);
    }

    /// <summary>The cue's distances' number (for <see cref="Attenuation"/>), or -1.</summary>
    public int DistanceIndex(string cueName) => Sounds.FindCue(cueName)?.DistanceIndex ?? -1;

    /// <summary>
    /// A cue played once at the game object it's posted on (3D, its distances), on <paramref name="bus"/>, as
    /// the event <see cref="EventName"/>(cue) in the bank of the cue's own sound bank; that name, or null if
    /// the cue can't be had (logged).
    /// </summary>
    public string? OneShot(string cueName, uint bus = WwiseBankWriter.VehicleEffectsBus)
    {
        string eventName = EventName(cueName);
        var cue = Sounds.FindCue(cueName);
        if (cue == null) { log.Add($"no cue {cueName} in Guerrilla's foley.aud_pc"); return null; }
        string soundBank;
        try { soundBank = Sounds.SoundBankName(cue); }
        catch (InvalidDataException e) { log.Add(e.Message); return null; }
        var bank = Bank(soundBank);
        if (bank.Has(WwiseBankWriter.Id(eventName))) return eventName;
        uint? sound = CueObject(bank, cueName, cueName, loops: false, output: new(bus, Attenuation(bank, cue.DistanceIndex), true));
        if (sound == null) return null;
        bank.Event(eventName, bank.Play(cueName, sound.Value));
        return eventName;
    }

    /// <summary>
    /// A cue played until the game stops it (a turret turning, a gun firing for as long as it's held; looping as the cue does): the
    /// event <see cref="EventName"/>(cue), and its stop event (the name + "_STOP": the loop fades out over
    /// <paramref name="fadeMs"/>), paired so the game posts the stop when it stops the sound. 3D at the object,
    /// the cue's distances. Its name, or null if the cue can't be had (logged).
    /// </summary>
    public string? Loop(string cueName, uint bus = WwiseBankWriter.VehicleEffectsBus, int fadeMs = 100)
    {
        string eventName = EventName(cueName), stopName = eventName + "_STOP";
        var cue = Sounds.FindCue(cueName);
        if (cue == null) { log.Add($"no cue {cueName} in Guerrilla's foley.aud_pc"); return null; }
        string soundBank;
        try { soundBank = Sounds.SoundBankName(cue); }
        catch (InvalidDataException e) { log.Add(e.Message); return null; }
        var bank = Bank(soundBank);
        if (bank.Has(WwiseBankWriter.Id(eventName))) return eventName;
        uint? sound = CueObject(bank, cueName, "loop:" + cueName, loops: cue.LoopCount != 0, output: new(bus, Attenuation(bank, cue.DistanceIndex), true));
        if (sound == null) return null;
        bank.Event(eventName, bank.Play("loop:" + cueName, sound.Value));
        bank.Event(stopName, bank.Stop("loop:" + cueName, sound.Value, fadeMs));
        StopPair(eventName, stopName);
        return eventName;
    }

    /// <summary>Says that posting <paramref name="stopEvent"/> stops the loop <paramref name="loopEvent"/> posted (the game posts it when it stops that sound).</summary>
    public void StopPair(string loopEvent, string stopEvent) => stopPairs[WwiseBankWriter.Id(loopEvent)] = WwiseBankWriter.Id(stopEvent);

    /// <summary>Writes the banks, their table rows and the stop pairs; the lines say what was written.</summary>
    public List<string> Write(string game, string overrides, string modFolder)
    {
        var lines = new List<string>();
        string cache = Path.Combine(overrides, "build", "pc", "cache");
        Directory.CreateDirectory(cache);
        // The banks this part wrote before and doesn't now are taken away.
        var listed = Listed(modFolder, part);
        foreach (string old in listed.Banks.Where(b => !banks.ContainsKey(b)))
            foreach (string file in new[] { old + ".bnk_pc", old + "_media.bnk_pc" })
                if (File.Exists(Path.Combine(cache, file))) File.Delete(Path.Combine(cache, file));
        long total = 0;
        foreach (var (name, bank) in banks.OrderBy(b => b.Key))
        {
            var (bankFile, mediaFile) = bank.Write();
            File.WriteAllBytes(Path.Combine(cache, name + ".bnk_pc"), bankFile);
            File.WriteAllBytes(Path.Combine(cache, name + "_media.bnk_pc"), mediaFile);
            total += bankFile.Length + mediaFile.Length;
            lines.Add($"{name}: {bank.Counts()} ({mediaFile.Length / 1048576.0:0.0} MB)");
        }
        Directory.CreateDirectory(modFolder);
        WriteTable(game, modFolder, listed.Banks, banks.Keys);
        WriteStopPairs(game, overrides, listed.Pairs, stopPairs);
        File.WriteAllLines(ListFile(modFolder, part), [.. banks.Keys.Order().Select(b => "bank " + b), .. stopPairs.Select(p => $"pair {p.Key} {p.Value}")]);
        lines.Add($"{banks.Count} banks ({total / 1048576.0:0} MB) listed in mods\\guerrilla_world\\{BankTable}; {stopPairs.Count} loops' stop events in {StopPairArchive}\\{StopPairFile}");
        return lines;
    }

    static string ListFile(string modFolder, string part) => Path.Combine(modFolder, part + "_sound_banks.txt");

    /// <summary>What a part wrote last: its banks and its stop pairs' loop events.</summary>
    static (HashSet<string> Banks, HashSet<uint> Pairs) Listed(string modFolder, string part)
    {
        var banks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pairs = new HashSet<uint>();
        string list = ListFile(modFolder, part);
        // The first test's list (vehicle_sound_banks.txt: a bank a line) counts as the vehicles' too.
        foreach (string file in new[] { list, part == "vehicles" ? Path.Combine(modFolder, "vehicle_sound_banks.txt") : "" })
        {
            if (file.Length == 0 || !File.Exists(file)) continue;
            foreach (string line in File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words[0] == "pair" && words.Length == 3 && uint.TryParse(words[1], out uint loop)) pairs.Add(loop);
                else if (words[0] == "bank" && words.Length == 2) banks.Add(words[1]);
                else if (words.Length == 1) banks.Add(words[0]);
            }
        }
        return (banks, pairs);
    }

    /// <summary>
    /// audio_banks_patch.xtbl: the one in the mod's folder if there is one (other parts' rows kept), else
    /// Armageddon's, with this part's earlier rows taken out and <paramref name="banks"/> added (streaming,
    /// loaded at boot, as Armageddon's vehicles bank). The game stops on a row whose id isn't its name's
    /// hash ("name_id == bank_id"), so the id is always that.
    /// </summary>
    static void WriteTable(string game, string modFolder, ISet<string> old, IEnumerable<string> banks)
    {
        string path = Path.Combine(modFolder, BankTable);
        var table = File.Exists(path) ? XtblFile.FromBytes(File.ReadAllBytes(path)) : GameTable(game) ?? throw new InvalidDataException($"no {BankTable} in Armageddon's misc.vpp_pc");
        foreach (var entry in table.Entries.ToList())
            if (old.Contains(XtblFile.EntryName(entry)) || banks.Contains(XtblFile.EntryName(entry), StringComparer.OrdinalIgnoreCase)) table.RemoveEntry(entry);
        foreach (string bank in banks.Order())
        {
            var row = new XtblElement("NewEntity");
            row.AddElement("Name", bank);
            row.AddElement("wwise_id", WwiseBankWriter.Id(bank).ToString());
            row.AddElement("streaming", "True");
            row.AddElement("load_at_boot", "True");
            row.AddElement("ram_bank_xbox2", "False");
            row.AddElement("ram_bank_ps3", "False");
            row.AddElement("ram_bank_pc", "False");
            row.AddElement("cacheable_xbox2", "True");
            row.AddElement("cacheable_ps3", "True");
            row.AddElement("cacheable_pc", "True");
            row.AddElement("voice", "False");
            table.Table!.AddElement(row);
        }
        File.WriteAllBytes(path, table.ToBytes());
    }

    static XtblFile? GameTable(string game)
    {
        var misc = Packfile.Open(Path.Combine(game, "build", "pc", "cache", "misc.vpp_pc"));
        return misc.Find(BankTable) is { } entry ? XtblFile.FromBytes(misc.Read(entry)) : null;
    }

    // audio_info.vad_pc: "VWAIPC  ", u32 0, u16 1, u16 0x130, u16 0x18, u16 the pairs' count, u32 0x18 (where they
    // start), then (loop event, stop event) 8 bytes a pair. The game sizes its table at the count + 50.
    static string StopPairPath(string overrides) => Path.Combine(overrides, "build", "pc", "cache", StopPairArchive, StopPairFile);

    static byte[] GameStopPairs(string game)
    {
        var sounds = Packfile.Open(Path.Combine(game, "build", "pc", "cache", StopPairArchive));
        return sounds.Read(sounds.Find(StopPairFile) ?? throw new InvalidDataException($"no {StopPairFile} in Armageddon's {StopPairArchive}"));
    }

    static List<(uint Loop, uint Stop)> ReadStopPairs(byte[] b)
    {
        if (b.Length < 0x18 || System.Text.Encoding.ASCII.GetString(b, 0, 8) != "VWAIPC  ") throw new InvalidDataException($"{StopPairFile} isn't as expected");
        int count = BitConverter.ToUInt16(b, 0x12), at = BitConverter.ToInt32(b, 0x14);
        return Enumerable.Range(0, count).Select(i => (BitConverter.ToUInt32(b, at + 8 * i), BitConverter.ToUInt32(b, at + 8 * i + 4))).ToList();
    }

    static void WriteStopPairs(string game, string overrides, ISet<uint> old, IReadOnlyDictionary<uint, uint> ours)
    {
        string path = StopPairPath(overrides);
        byte[] gameFile = GameStopPairs(game);
        byte[] current = File.Exists(path) ? File.ReadAllBytes(path) : gameFile;
        var pairs = ReadStopPairs(current).Where(p => !old.Contains(p.Loop) && !ours.ContainsKey(p.Loop)).ToList();
        pairs.AddRange(ours.Select(p => (p.Key, p.Value)));
        if (pairs.Count > ushort.MaxValue) throw new InvalidDataException($"{pairs.Count} stop pairs are more than {StopPairFile} holds");
        var gamePairs = ReadStopPairs(gameFile);
        if (pairs.Count == gamePairs.Count && pairs.SequenceEqual(gamePairs))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var b = new List<byte>();
        b.AddRange(gameFile.AsSpan(0, 0x12).ToArray());       // its header as the game's, but the count
        b.AddRange(BitConverter.GetBytes((ushort)pairs.Count));
        b.AddRange(BitConverter.GetBytes(0x18));
        foreach (var (loop, stop) in pairs) { b.AddRange(BitConverter.GetBytes(loop)); b.AddRange(BitConverter.GetBytes(stop)); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [.. b]);
    }

    /// <summary>Takes away a part's banks, its table rows and its stop pairs (the table and the stop pairs' file go once only Armageddon's own are left).</summary>
    public static IEnumerable<string> Remove(string game, string overrides, string modFolder, string part)
    {
        var (banks, pairs) = Listed(modFolder, part);
        foreach (string bank in banks)
            foreach (string file in new[] { bank + ".bnk_pc", bank + "_media.bnk_pc" })
            {
                string path = Path.Combine(overrides, "build", "pc", "cache", file);
                if (File.Exists(path)) { File.Delete(path); yield return $"Deleted {path}"; }
            }
        string tablePath = Path.Combine(modFolder, BankTable);
        if (File.Exists(tablePath))
        {
            var table = XtblFile.FromBytes(File.ReadAllBytes(tablePath));
            foreach (var entry in table.Entries.Where(e => banks.Contains(XtblFile.EntryName(e))).ToList()) table.RemoveEntry(entry);
            List<string> gameRows = GameTable(game) is { } original ? [.. original.Entries.Select(XtblFile.EntryName)] : [];
            if (table.Entries.Select(XtblFile.EntryName).SequenceEqual(gameRows, StringComparer.OrdinalIgnoreCase))
            {
                File.Delete(tablePath);
                yield return $"Deleted {tablePath}";
            }
            else File.WriteAllBytes(tablePath, table.ToBytes());
        }
        string pairPath = StopPairPath(overrides);
        if (File.Exists(pairPath))
        {
            WriteStopPairs(game, overrides, pairs, new Dictionary<uint, uint>());
            if (!File.Exists(pairPath)) yield return $"Deleted {pairPath}";
        }
        foreach (string list in new[] { ListFile(modFolder, part), part == "vehicles" ? Path.Combine(modFolder, "vehicle_sound_banks.txt") : "" })
            if (list.Length > 0 && File.Exists(list)) { File.Delete(list); yield return $"Deleted {list}"; }
    }
}
