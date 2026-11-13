using System.Text;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's voices: a persona's lines (rfg_voice_personas.xtbl names each persona's file) as waves, for
/// building them into banks of Armageddon's kind. So far the kiosks' persona (KIOSK) uses it; the PA speakers'
/// (ADDRESS), the handbook's and the people's personas are the same kind.
///
/// Read from the player's own copy of Guerrilla, all checked on the KIOSK persona's 141 lines:
/// - personas.vpp_pc, <c>&lt;PERSONA&gt;.rfgvpx.str2_pc</c>: a container of <c>&lt;persona&gt;.rfgvp_pc</c> and
///   <c>&lt;persona&gt;.vfdvp_pc</c>.
/// - rfgvp_pc ("0JXo", version 3, the persona's name in 64 bytes from +8): at +0x48 the lines' count, a second
///   count, 0, the lines' table's place, the second table's place, the end. A line: 12 bytes, its key (Guerrilla's
///   CRC-32 of its name in lower case: <see cref="GuerrillaSounds.Crc"/>), the cue it plays (u16, a number in the
///   vfdvp_pc), 0xFFFF, and 1.
/// - vfdvp_pc ("VFDPPC  ", version 1, the cues' count u16 at +0xA): 48 bytes a cue from +0x10, laid out as
///   foley.aud_pc's: its key, a name's place, its category's, sound bank's and wave bank's CRCs (+8, +12, +16), its
///   volume (a gain, +20), its cue in the XACT sound bank (u16, +24), its rank (+26), its distances' number (+27,
///   foley_settings.xtbl's Min_Max_Distances), how many at once (+28), its loop count (+30), its channels (+31).
/// - voices_r.vpp_pc: the XACT sound banks (<c>.xsb_pc</c>, "SDBK" version 44, as sounds_r.vpp_pc's: a voice's cue is a
///   simple one, a sound of one wave) and wave banks (<c>.xwb_pc</c>, "WBND" version 44: MS-ADPCM, mono, about 32 kHz).
/// </summary>
public sealed class GuerrillaVoices
{
    /// <summary>One of the persona's lines, as Guerrilla plays it.</summary>
    /// <param name="Volume">The cue's gain.</param>
    /// <param name="SoundDb">The XACT sound's own volume, in dB.</param>
    public sealed record Line(string Name, float Volume, float SoundDb, int Rank, int DistanceIndex, string WaveBank, int Wave);

    public string Persona { get; }

    readonly Packfile voices;
    readonly Dictionary<uint, int> cueOfLine = [];
    readonly List<(uint SoundBankCrc, float Volume, int Index, int Rank, int DistanceIndex)> cues = [];
    readonly Dictionary<uint, string> soundBankNames = [];
    readonly Dictionary<string, (byte[] Data, string[] WaveBanks)> soundBanks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> waveBanks = new(StringComparer.OrdinalIgnoreCase);

    GuerrillaVoices(string persona, Packfile voices, byte[] lines, byte[] cueFile)
    {
        Persona = persona;
        this.voices = voices;
        if (Encoding.ASCII.GetString(lines, 0, 4) != "0JXo") throw new InvalidDataException($"{persona}'s rfgvp_pc isn't a persona's line list");
        int count = BitConverter.ToInt32(lines, 0x48), at = BitConverter.ToInt32(lines, 0x54);
        for (int i = 0; i < count; i++) cueOfLine[BitConverter.ToUInt32(lines, at + 12 * i)] = BitConverter.ToUInt16(lines, at + 12 * i + 4);
        if (Encoding.ASCII.GetString(cueFile, 0, 8) != "VFDPPC  ") throw new InvalidDataException($"{persona}'s vfdvp_pc isn't a persona's cue list");
        int cueCount = BitConverter.ToUInt16(cueFile, 0x0A);
        for (int i = 0; i < cueCount; i++)
        {
            int o = 0x10 + 48 * i;
            cues.Add((BitConverter.ToUInt32(cueFile, o + 12), BitConverter.ToSingle(cueFile, o + 20), BitConverter.ToUInt16(cueFile, o + 24), cueFile[o + 26], cueFile[o + 27]));
        }
        foreach (var entry in voices.Entries.Where(e => e.Name.EndsWith(".xsb_pc", StringComparison.OrdinalIgnoreCase)))
        {
            string name = entry.Name[..^".xsb_pc".Length];
            soundBankNames[GuerrillaSounds.Crc(name.ToLowerInvariant())] = name;
        }
    }

    /// <summary>A persona of Guerrilla's (its name in rfg_voice_personas.xtbl: KIOSK, ADDRESS, ...), or null if its files aren't there.</summary>
    public static GuerrillaVoices? Open(string guerrillaCache, string persona)
    {
        string personas = Path.Combine(guerrillaCache, "personas.vpp_pc"), voicesFile = Path.Combine(guerrillaCache, "voices_r.vpp_pc");
        if (!File.Exists(personas) || !File.Exists(voicesFile)) return null;
        var archive = Packfile.Open(personas);
        if (archive.Find(persona.ToUpperInvariant() + ".rfgvpx.str2_pc") is not { } entry) return null;
        var container = archive.OpenContainer(entry);
        string lower = persona.ToLowerInvariant();
        if (container.Find(lower + ".rfgvp_pc") is not { } lines || container.Find(lower + ".vfdvp_pc") is not { } cueFile) return null;
        return new GuerrillaVoices(persona, Packfile.Open(voicesFile), container.Read(lines), container.Read(cueFile));
    }

    /// <summary>The persona's line of this name, or null if it has none.</summary>
    public Line? Find(string lineName)
    {
        if (!cueOfLine.TryGetValue(GuerrillaSounds.Crc(lineName.ToLowerInvariant()), out int cue) || cue < 0 || cue >= cues.Count) return null;
        var (bankCrc, volume, index, rank, distance) = cues[cue];
        if (!soundBankNames.TryGetValue(bankCrc, out string? soundBank)) throw new InvalidDataException($"{lineName}: its sound bank isn't in voices_r.vpp_pc");
        var (data, banks) = SoundBank(soundBank);
        // A simple cue (5 bytes from +34's place: flags, its sound's place); a sound of one wave: flags, category (2),
        // volume, pitch (2), priority, size (2), then the wave (u16) and its wave bank's number.
        int simple = BitConverter.ToUInt16(data, 19);
        if (index >= simple) throw new NotSupportedException($"{lineName}: cue {index} of {soundBank} isn't a simple one (not read yet)");
        int sound = BitConverter.ToInt32(data, BitConverter.ToInt32(data, 34) + 5 * index + 1);
        if ((data[sound] & 1) != 0) throw new NotSupportedException($"{lineName}: cue {index} of {soundBank} has tracks (not read yet)");
        int wave = BitConverter.ToUInt16(data, sound + 9), bank = data[sound + 11];
        return new Line(lineName, volume, GuerrillaSounds.XactDecibels(data[sound + 3]), rank, distance, banks[bank], wave);
    }

    (byte[] Data, string[] WaveBanks) SoundBank(string name)
    {
        if (soundBanks.TryGetValue(name, out var found)) return found;
        var data = voices.Read(voices.Find(name + ".xsb_pc") ?? throw new InvalidDataException($"no {name}.xsb_pc in voices_r.vpp_pc"));
        if (Encoding.ASCII.GetString(data, 0, 4) != "SDBK") throw new InvalidDataException($"{name}.xsb_pc isn't a sound bank");
        int at = BitConverter.ToInt32(data, 58);
        var banks = Enumerable.Range(0, data[27]).Select(i => Encoding.ASCII.GetString(data, at + 64 * i, 64).Split('\0')[0]).ToArray();
        return soundBanks[name] = (data, banks);
    }

    /// <summary>
    /// A line's wave, decoded to 16-bit samples (from the wave bank's entry: flags and length, format, where its data
    /// is and how long, its loop: as sounds_r.vpp_pc's; a format: tag (2 MS-ADPCM, 0 PCM), channels, rate, block).
    /// </summary>
    public GuerrillaSounds.Wave ReadWave(Line line)
    {
        if (!waveBanks.TryGetValue(line.WaveBank, out var b))
            waveBanks[line.WaveBank] = b = voices.Read(voices.Find(line.WaveBank + ".xwb_pc") ?? throw new InvalidDataException($"no {line.WaveBank}.xwb_pc in voices_r.vpp_pc"));
        if (Encoding.ASCII.GetString(b, 0, 4) != "WBND") throw new InvalidDataException($"{line.WaveBank}.xwb_pc isn't a wave bank");
        int data = BitConverter.ToInt32(b, 12), entries = BitConverter.ToInt32(b, 20), waves = BitConverter.ToInt32(b, 12 + 8 * 4);
        int count = BitConverter.ToInt32(b, data + 4), entrySize = BitConverter.ToInt32(b, data + 72);
        if (line.Wave < 0 || line.Wave >= count) throw new InvalidDataException($"{line.WaveBank} has no wave {line.Wave}");
        int e = entries + entrySize * line.Wave;
        uint format = BitConverter.ToUInt32(b, e + 4);
        int offset = BitConverter.ToInt32(b, e + 8), length = BitConverter.ToInt32(b, e + 12);
        int tag = (int)(format & 3), channels = (int)((format >> 2) & 7), rate = (int)((format >> 5) & 0x3FFFF), block = (int)((format >> 23) & 0xFF);
        short[] samples;
        if (tag == 2) samples = MsAdpcm.Decode(b.AsSpan(waves + offset, length), channels, (block + 22) * channels);
        else if (tag == 0 && (format >> 31) != 0)
        {
            samples = new short[length / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(b, waves + offset + 2 * i);
        }
        else throw new NotSupportedException($"{line.WaveBank} wave {line.Wave}: format {tag} (only 16-bit PCM and MS-ADPCM read)");
        return new GuerrillaSounds.Wave(line.WaveBank, line.Wave, samples, channels, rate, false);
    }
}
