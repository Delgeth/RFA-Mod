using System.Text;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's sounds, read from the player's own copy (only read). Guerrilla plays XACT 3:
/// its cues by name are in <c>foley.aud_pc</c> (sounds_r.vpp_pc; Volition's compiled foley.xtbl, "VFD PC"
/// version 6), each naming its XACT sound bank (<c>&lt;bank&gt;.xsb_pc</c>), its index there and its wave
/// bank, found by the CRC-32 of the cue's name (the zlib table, from 0, no final inversion; the banks' by
/// their lower-case names); the sound bank leads from the cue to a sound and from it to waves (one, or one
/// of several at random) in its wave banks (<c>&lt;bank&gt;.xwb_pc</c>, "WBND" version 44), whose waves are
/// MS-ADPCM, each looping over the whole of it (64-sample blocks).
/// </summary>
public sealed class GuerrillaSounds
{
    readonly Packfile sounds;
    readonly Dictionary<uint, Cue> cues = [];
    readonly Dictionary<string, Xsb> soundBanks = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> waveBanks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A cue of foley.aud_pc: its XACT sound bank (by its name's CRC), its index there, its volume (a gain), loop
    /// count (255: for ever) and channels, its distances (an index into foley_settings.xtbl's Min_Max_Distances,
    /// in order), how many may play at once (0: any) and its rank: Guerrilla's priority for it, 0 (footsteps, small
    /// debris), 4 (other vehicles' engines, middling things), 7 (large destruction, most vehicle sounds) or 10 (the
    /// player's engine and guns, the hammer's hits); its music has 11.
    /// </summary>
    public sealed record Cue(string Name, uint SoundBankCrc, uint WaveBankCrc, int Index, float Volume, int LoopCount, int Channels,
                             int DistanceIndex = 0, int MostAtOnce = 0, int Rank = 0);

    /// <summary>A wave: 16-bit samples (channels interleaved), its rate, and whether Guerrilla loops it.</summary>
    public sealed record Wave(string Bank, int Index, short[] Samples, int Channels, int Rate, bool Loops, int LoopStart = 0);

    public GuerrillaSounds(string guerrillaCache)
    {
        sounds = Packfile.Open(Path.Combine(guerrillaCache, "sounds_r.vpp_pc"));
        ReadCues(sounds.Read(sounds.Find("foley.aud_pc") ?? throw new InvalidDataException("no foley.aud_pc in sounds_r.vpp_pc")));
    }

    /// <summary>The CRC-32 Guerrilla finds cues by (of the name as written) and banks (of the lower-case name).</summary>
    public static uint Crc(string text)
    {
        uint h = 0;
        foreach (byte b in Encoding.Latin1.GetBytes(text))
        {
            h ^= b;
            for (int k = 0; k < 8; k++) h = (h & 1) != 0 ? 0xEDB88320 ^ (h >> 1) : h >> 1;
        }
        return h;
    }

    // foley.aud_pc: at 0x14 the categories' start and size, the cue and variation counts, the cues' start
    // (228 bytes each), ...; a cue: its name's CRC (+0), its name (+4, in a compressed string block, not
    // needed), its category's, sound bank's and wave bank's CRCs (+8, +12, +16), its volume (+20), its
    // index in the sound bank (+24), priority, its distances' number, how many at once, ..., its loop
    // count (+30), its channels (+31), its variations, flags.
    void ReadCues(byte[] b)
    {
        int count = BitConverter.ToUInt16(b, 0x1C), start = BitConverter.ToInt32(b, 0x24);
        for (int i = 0; i < count; i++)
        {
            int o = start + 228 * i;
            uint crc = BitConverter.ToUInt32(b, o);
            cues[crc] = new Cue("", BitConverter.ToUInt32(b, o + 12), BitConverter.ToUInt32(b, o + 16), BitConverter.ToUInt16(b, o + 24),
                                BitConverter.ToSingle(b, o + 20), b[o + 30], (sbyte)b[o + 31], b[o + 27], b[o + 28], b[o + 26]);
        }
    }

    /// <summary>A cue by its name, or null.</summary>
    public Cue? FindCue(string name) => cues.TryGetValue(Crc(name), out var c) ? c with { Name = name } : null;

    /// <summary>
    /// What a cue's XACT sound plays: its waves as (wave bank, wave), one, or several it picks one of at
    /// random (<see cref="Picks"/>); its volume (the sound's and its track's, in dB) and pitch (in cents).
    /// The cue's own volume (<see cref="Cue.Volume"/>, a gain) and loop count come on top.
    /// </summary>
    public sealed record Sound(IReadOnlyList<(string Bank, int Wave)> Waves, bool Picks, float VolumeDb, float PitchCents);

    /// <summary>What a cue plays (it must be in <paramref name="soundBank"/>, as foley_*.xtbl names it).</summary>
    public Sound SoundOf(Cue cue, string soundBank)
    {
        if (Crc(soundBank.ToLowerInvariant()) != cue.SoundBankCrc)
            throw new InvalidDataException($"{cue.Name} is in another sound bank than {soundBank}");
        return SoundBank(soundBank).SoundOf(cue.Index);
    }

    /// <summary>What a cue plays, its sound bank found by the CRC the cue keeps (for cues no table names a bank for: doors, ...).</summary>
    public Sound SoundOf(Cue cue) => SoundOf(cue, SoundBankName(cue));

    /// <summary>The name of the XACT sound bank a cue is in (one of sounds_r.vpp_pc's .xsb_pc files).</summary>
    public string SoundBankName(Cue cue)
    {
        bankNames ??= sounds.Entries.Where(e => e.Name.EndsWith(".xsb_pc", StringComparison.OrdinalIgnoreCase))
                                    .Select(e => e.Name[..^".xsb_pc".Length])
                                    .ToDictionary(n => Crc(n.ToLowerInvariant()), n => n);
        return bankNames.TryGetValue(cue.SoundBankCrc, out var n) ? n : throw new InvalidDataException($"{cue.Name}'s sound bank isn't in sounds_r.vpp_pc");
    }
    Dictionary<uint, string>? bankNames;

    /// <summary>The waves a cue plays, as (wave bank, wave): one, or the ones it picks between.</summary>
    public IReadOnlyList<(string Bank, int Wave)> Waves(Cue cue, string soundBank) => SoundOf(cue, soundBank).Waves;

    /// <summary>
    /// XACT's volume byte in dB: 0xB4 is 0 dB, 0xFF +6, 0x5A −12, 0 −96 (the curve fitted to XACT's own
    /// table, as MonoGame's XACT reader has it).
    /// </summary>
    public static float XactDecibels(byte volume) =>
        (float)((-96.0 - 67.7385212334047) / (1 + Math.Pow(volume / 80.1748600297963, 0.432254984608615)) + 67.7385212334047);

    /// <summary>One wave of a wave bank, decoded to 16-bit samples; a looping one up to its loop's end.</summary>
    public Wave ReadWave(string bank, int index)
    {
        if (!waveBanks.TryGetValue(bank, out var b))
            waveBanks[bank] = b = sounds.Read(sounds.Find(bank + ".xwb_pc") ?? throw new InvalidDataException($"no {bank}.xwb_pc in sounds_r.vpp_pc"));
        return Xwb.Read(b, bank, index);
    }

    /// <summary>A NUL-ended name in a field of <paramref name="size"/> bytes.</summary>
    static string Text(byte[] b, int at, int size) => Encoding.ASCII.GetString(b, at, size).Split('\0')[0];

    Xsb SoundBank(string name)
    {
        if (!soundBanks.TryGetValue(name, out var x))
            soundBanks[name] = x = new Xsb(sounds.Read(sounds.Find(name + ".xsb_pc") ?? throw new InvalidDataException($"no {name}.xsb_pc in sounds_r.vpp_pc")));
        return x;
    }

    /// <summary>
    /// An XACT sound bank ("SDBK" version 44): its simple cues (5 bytes: flags, the sound's offset), then its
    /// complex ones (a sound, or variations), each cue's index counting the simple ones first; a sound: flags
    /// (1: tracks of events, else one wave), category, volume, pitch, priority, size, then one wave (its
    /// index and wave bank's number) or its tracks (each: volume, its events' offset, filter); an event: its
    /// count, then each its info (type &amp; 0x1F), a random offset (2 bytes), 1 byte, then by type: 1 (play a
    /// wave) or 4 (with variation): flags, wave (2 bytes), wave bank, loop count, ...; 3 (play one of several):
    /// flags, loop count, pan (4 bytes), the waves' count (2), the variation type (2), 4 bytes, then each wave
    /// (2), wave bank, its weight's range (2). Guerrilla's vehicles' sounds all have one track of one event.
    /// </summary>
    sealed class Xsb(byte[] b)
    {
        readonly string[] waveBanks = Enumerable.Range(0, b[27]).Select(i => Text(b, BitConverter.ToInt32(b, 58) + 64 * i, 64)).ToArray();

        public Sound SoundOf(int cue)
        {
            int simple = BitConverter.ToUInt16(b, 19), complex = BitConverter.ToUInt16(b, 21);
            if (cue < simple) return FromSound(BitConverter.ToInt32(b, BitConverter.ToInt32(b, 34) + 5 * cue + 1));
            int c = BitConverter.ToInt32(b, 38) + 15 * (cue - simple);
            if (cue - simple >= complex) throw new InvalidDataException($"no cue {cue}");
            if ((b[c] & 4) != 0) return FromSound(BitConverter.ToInt32(b, c + 1));
            throw new NotSupportedException($"cue {cue} picks between variations (not read yet)");
        }

        Sound FromSound(int at)
        {
            // A sound: flags, category (2), volume, pitch (2, 1/10 cent: unverified, Guerrilla's vehicles' are 0), priority, size (2).
            byte flags = b[at];
            float volume = XactDecibels(b[at + 3]), pitch = BitConverter.ToInt16(b, at + 4) / 10f;
            int o = at + 9;
            if ((flags & 1) == 0) return new([(waveBanks[b[o + 2]], BitConverter.ToUInt16(b, o))], false, volume, pitch);
            int tracks = b[o++];
            if ((flags & 0x0E) != 0) o += BitConverter.ToUInt16(b, o);
            if ((flags & 0x10) != 0) o += 3 + 4 * b[o + 2];
            if (tracks != 1) throw new NotSupportedException($"sound at {at}: {tracks} tracks at once (not read yet)");
            volume += XactDecibels(b[o]);
            int e = BitConverter.ToInt32(b, o + 1);
            if (b[e] != 1) throw new NotSupportedException($"sound at {at}: {b[e]} events (one read)");
            e++;
            int type = (int)(BitConverter.ToUInt32(b, e) & 0x1F);
            e += 7;
            switch (type)
            {
                case 1 or 4:
                    return new([(waveBanks[b[e + 3]], BitConverter.ToUInt16(b, e + 1))], false, volume, pitch);
                case 3:
                {
                    int count = BitConverter.ToUInt16(b, e + 6), w = e + 14;
                    var waves = Enumerable.Range(0, count).Select(k => (waveBanks[b[w + 5 * k + 2]], (int)BitConverter.ToUInt16(b, w + 5 * k))).ToList();
                    return new(waves, true, volume, pitch);
                }
                default:
                    throw new NotSupportedException($"sound at {at}: event type {type} (not read yet)");
            }
        }
    }

    /// <summary>
    /// An XACT wave bank ("WBND" version 44): five segments (offset, size) from 12: the bank's data (flags,
    /// count, name, an entry's size), the entries (flags and length, format, where its data is and how long,
    /// its loop's start and length in samples), seek tables, names, the waves' data. A format: tag (0 = PCM, 2 = ADPCM),
    /// channels, rate, block size (ADPCM: bytes a channel's block, less 22), 8 or 16 bits.
    /// </summary>
    static class Xwb
    {
        public static Wave Read(byte[] b, string bank, int index)
        {
            if (Encoding.ASCII.GetString(b, 0, 4) != "WBND") throw new InvalidDataException($"{bank}.xwb_pc isn't a wave bank");
            int data = BitConverter.ToInt32(b, 12), entries = BitConverter.ToInt32(b, 20), waves = BitConverter.ToInt32(b, 12 + 8 * 4);
            int count = BitConverter.ToInt32(b, data + 4), entrySize = BitConverter.ToInt32(b, data + 72);
            if (index < 0 || index >= count) throw new InvalidDataException($"{bank} has no wave {index}");
            int e = entries + entrySize * index;
            uint format = BitConverter.ToUInt32(b, e + 4);
            int offset = BitConverter.ToInt32(b, e + 8), length = BitConverter.ToInt32(b, e + 12);
            int loopStart = BitConverter.ToInt32(b, e + 16), loopLength = BitConverter.ToInt32(b, e + 20);
            int tag = (int)(format & 3), channels = (int)((format >> 2) & 7), rate = (int)((format >> 5) & 0x3FFFF), block = (int)((format >> 23) & 0xFF);
            short[] samples;
            if (tag == 2) samples = MsAdpcm.Decode(b.AsSpan(waves + offset, length), channels, (block + 22) * channels);
            else if (tag == 0)
            {
                // Plain PCM (a few of Guerrilla's waves: wep_rfg's gun loops): the format's top bit says 16 bits, else 8 (unsigned).
                var pcm = b.AsSpan(waves + offset, length);
                if ((format >> 31) != 0)
                {
                    samples = new short[length / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(pcm[(2 * i)..]);
                }
                else
                {
                    samples = new short[length];
                    for (int i = 0; i < length; i++) samples[i] = (short)((pcm[i] - 128) << 8);
                }
            }
            else throw new NotSupportedException($"{bank} wave {index}: format {tag} (only PCM and ADPCM read)");
            bool loops = loopLength > 0;
            if (loops && (loopStart + loopLength) * channels <= samples.Length) samples = samples[..((loopStart + loopLength) * channels)];
            return new Wave(bank, index, samples, channels, rate, loops, loops && loopStart * channels < samples.Length ? loopStart : 0);
        }
    }
}

/// <summary>Microsoft's ADPCM (XACT's): blocks of a header a channel (predictor, delta, two samples) then 4-bit codes.</summary>
public static class MsAdpcm
{
    static readonly int[] Coef1 = [256, 512, 0, 192, 240, 460, 392], Coef2 = [0, -256, 0, 64, 0, -208, -232];
    static readonly int[] Adapt = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

    public static short[] Decode(ReadOnlySpan<byte> data, int channels, int blockAlign)
    {
        int perBlock = 2 + (blockAlign - 7 * channels) * 2 / channels;
        var output = new List<short>(data.Length / blockAlign * perBlock * channels + perBlock * channels);
        var predictor = new int[channels];
        var delta = new int[channels];
        var s1 = new int[channels];
        var s2 = new int[channels];
        for (int at = 0; at + blockAlign <= data.Length; at += blockAlign)
        {
            var block = data.Slice(at, blockAlign);
            int o = 0;
            for (int c = 0; c < channels; c++) predictor[c] = Math.Min((int)block[o++], 6);
            for (int c = 0; c < channels; c++, o += 2) delta[c] = BitConverter.ToInt16(block[o..]);
            for (int c = 0; c < channels; c++, o += 2) s1[c] = BitConverter.ToInt16(block[o..]);
            for (int c = 0; c < channels; c++, o += 2) s2[c] = BitConverter.ToInt16(block[o..]);
            for (int c = 0; c < channels; c++) output.Add((short)s2[c]);
            for (int c = 0; c < channels; c++) output.Add((short)s1[c]);
            int c2 = 0;
            for (; o < blockAlign; o++)
                foreach (int nibble in new[] { block[o] >> 4, block[o] & 15 })
                {
                    int code = nibble >= 8 ? nibble - 16 : nibble;
                    int sample = ((s1[c2] * Coef1[predictor[c2]]) + (s2[c2] * Coef2[predictor[c2]])) / 256 + code * delta[c2];
                    sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
                    output.Add((short)sample);
                    s2[c2] = s1[c2];
                    s1[c2] = sample;
                    delta[c2] = Math.Max(16, Adapt[nibble] * delta[c2] / 256);
                    c2 = (c2 + 1) % channels;
                }
        }
        return [.. output];
    }
}

/// <summary>
/// Sound banks of Armageddon's own kind, for Guerrilla's sounds. Armageddon plays Wwise (banks version 48):
/// a bank (<c>&lt;name&gt;.bnk_pc</c>) is its header (BKHD: version, the bank's id), its objects (HIRC: a count,
/// then each a type, a size and an id, all 4 bytes) and its name (STID); its sounds' media are streamed from
/// Volition's container beside it (<c>&lt;name&gt;_media.bnk_pc</c>: "VWSBPC  ", 0, 2, 1, the bank's id, the
/// header's size + 16, a count, then (media id, offset, 0, size) a medium, sorted, each a .wem on a 2 KB
/// boundary). The game registers a bank listed in audio_banks.xtbl or audio_banks_patch.xtbl (its name, its
/// id = the name's Wwise hash, streaming, loaded at boot) and opens both files by name, from overrides\ for
/// new ones; any event is posted by its name's hash, no list needed. Ids are Wwise's: FNV-1 of the
/// lower-case name.
///
/// The objects are written as Armageddon's are laid out (every one of its banks' sounds, containers,
/// actions, events and attenuations reads and writes back byte for byte this way): a sound (its medium,
/// streamed, then the node's settings: output bus and parent, volume and pitch, positioning, voice
/// settings, RTPC curves; then its loop count), a random container (picks one child a time), a layer
/// container (plays all its children at once), an attenuation (volume against distance), play and stop
/// actions, and events (a list of actions). Every object hanging from nothing has its own bus and
/// positioning (3D game-defined with an attenuation, or 2D). The media are PCM .wem files (16-bit, laid out
/// as the game's own PCM ones), since its Vorbis needs Audiokinetic's encoder.
/// </summary>
public sealed class WwiseBankWriter
{
    const int BankVersion = 48;
    const uint PcmPlugin = 0x00010001;  // Wwise's PCM codec (Armageddon's own media are Vorbis, 0x00040001)

    /// <summary>
    /// Armageddon's bus (in init.bnk_pc) for its walkers' and flyers' sounds: −4 dB, −8 dB with its parents', and no
    /// limit on how many sounds play on it at once. Guerrilla's vehicles' sounds all go here, the engines too.
    /// A bus may limit its sounds: past the limit the game ends the oldest one (or the newest) for good. The
    /// limits are set for Armageddon's own sounds, so check a bus's before sending Guerrilla's sounds to it.
    /// </summary>
    public const uint VehicleEffectsBus = 0x1BDA12A2;

    /// <summary>
    /// How much quieter Armageddon's own engines' bus (0x03ECF678: −12 dB, −16 dB with its parents') is than
    /// <see cref="VehicleEffectsBus"/>. Guerrilla's engines are turned down by this much, to sound as loud as they
    /// would on Armageddon's engines' bus. They can't be on that bus itself: it lets 10 sounds play at once and
    /// ends the oldest for the next, and one of Guerrilla's engines is five or six loops, twice (the player's
    /// stereo ones and the others' mono ones). On it, an engine went silent for good as soon as another one started.
    /// </summary>
    public const float EngineBusDifference = -8f;

    /// <summary>
    /// Armageddon's bus for its effects' sounds (what its own effects.xtbl names: its objects' deaths and hums, and,
    /// on buses under it, its explosions and fires): −4 dB, −8 dB with its parents'. It lets 15 sounds play at once,
    /// its buses' under it counted in; for one more, the one of the lowest priority ends, the oldest of those.
    /// Guerrilla's effects' sounds that play once go here. Not on its explosions' own bus under it (0x332BCA77),
    /// which lets 4 play: Guerrilla sets off more than that at once.
    /// </summary>
    public const uint EffectsBus = 0x2785E1AB;

    /// <summary>
    /// The bus of Armageddon's destruction sounds: 0 dB, −4 dB with its parents', and no limit of its own (the
    /// effects' top bus over it lets 128 play). Guerrilla's effects' looping sounds (a fire burning, a machine
    /// humming) go here, turned down by <see cref="EffectLoopBusDifference"/> to the level of <see cref="EffectsBus"/>:
    /// on that bus a burst of 15 sounds would end a loop for good, and 15 loops would leave no room for anything else.
    /// </summary>
    public const uint EffectLoopsBus = 0x3108D6B1;

    /// <summary>How much quieter <see cref="EffectsBus"/> is than <see cref="EffectLoopsBus"/>.</summary>
    public const float EffectLoopBusDifference = -4f;

    /// <summary>The game parameters the game sets on a vehicle's engine sound each frame, and the mod's own (1 while the player is in that vehicle).</summary>
    public static readonly uint Rpm = Id("RPM"), EngineLoad = Id("ENGINE_LOAD"), Speed = Id("SPEED"), Damage = Id("DAMAGE"),
                                PlayerInVehicle = Id("RFG_PLAYER_IN_VEHICLE");

    /// <summary>Wwise's silence, in dB (as Armageddon's curves fall to).</summary>
    public const float Silent = -96.3f;

    /// <summary>What a curve changes: volume (dB), pitch (cents), low-pass (0-100).</summary>
    public enum Property { Volume = 0, Pitch = 2, LowPass = 3 }

    /// <summary>A curve: a game parameter's value (x) → a property (y), straight lines between the points.</summary>
    public sealed record Curve(uint Parameter, Property Property, IReadOnlyList<(float X, float Y)> Points);

    /// <summary>
    /// A sound's or container's settings: its volume (dB) and pitch (cents), its curves, how many may play at once
    /// (0 any), its priority (Armageddon's sounds have 50; where a bus is full, the lowest goes first), and whether
    /// one too quiet to hear goes virtual: it stops playing, takes no voice, and starts again from its beginning
    /// when it can be heard (as Guerrilla stops an engine's layer out of its range and starts it again).
    /// </summary>
    public sealed class Settings
    {
        public float Volume, Pitch;
        public List<Curve> Curves = [];
        public ushort MostAtOnce;
        public byte Priority = 50;

        /// <summary>The priority is this object's own though it hangs from another (else it takes its parent's).</summary>
        public bool OwnPriority;
        public bool VirtualWhenQuiet;

        /// <summary>
        /// One too quiet to hear is ended, for good (a sound that plays once, out of earshot): it then holds none of
        /// the places the game's buses count, where one playing on unheard held its place for its whole length.
        /// Armageddon sets a good part of its own one-shot sounds so (700 in the banks loaded at start-up).
        /// </summary>
        public bool EndsWhenQuiet;

        /// <summary>
        /// With <see cref="MostAtOnce"/> playing, one more doesn't start (Wwise's "kill newest"; else the oldest is
        /// ended for it). Guerrilla's own rule for a cue's number at once (its audiolib_cue_play gives up with an
        /// error where the cue's count has reached max_simultaneous), and what Armageddon's own banks set on most
        /// of their limited sounds. A sound that doesn't start opens no stream, where one started in the oldest's
        /// place reads its first piece from the disk only to be ended by the next.
        /// </summary>
        public bool RefusesWhenFull;
    }

    /// <summary>Where an object hanging from nothing goes: its bus, and 3D with an attenuation, or 2D.</summary>
    public sealed record Output(uint Bus, uint Attenuation, bool ThreeD);

    readonly string name;
    readonly uint bankId;
    readonly List<(uint Type, uint Id, byte[] Body)> attenuations = [], objects = [];
    readonly SortedDictionary<uint, byte[]> media = [];
    readonly HashSet<uint> taken = [];

    /// <summary>Wwise's id of a name: FNV-1 (32 bits) of its lower-case letters.</summary>
    public static uint Id(string text)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.ASCII.GetBytes(text.ToLowerInvariant())) h = (h * 16777619) ^ b;
        return h;
    }

    public WwiseBankWriter(string bankName)
    {
        name = bankName;
        bankId = Id(bankName);
    }

    public string Name => name;
    public uint BankId => bankId;

    /// <summary>The id this bank gives the object made under <paramref name="key"/> (the same in every build).</summary>
    public uint ObjectId(string key) => Id($"rfg:{name}:{key}");

    /// <summary>Whether an object or event of this id is in the bank already.</summary>
    public bool Has(uint id) => taken.Contains(id);

    /// <summary>A wave as a medium (once), by its id.</summary>
    public uint Medium(GuerrillaSounds.Wave wave)
    {
        uint id = Id($"rfg_media:{wave.Bank}:{wave.Index}");
        if (!media.ContainsKey(id)) media[id] = PcmWem(wave);
        return id;
    }

    /// <summary>An attenuation: volume (dB) against distance (m), no cone, low-pass or spread.</summary>
    public uint Attenuation(string key, IReadOnlyList<(float Distance, float Db)> volume)
    {
        uint id = ObjectId("attenuation:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        w.U8(0);                                               // no cone
        foreach (sbyte slot in new sbyte[] { 0, 0, 0, -1, -1 }) w.S8(slot);  // volume dry, wet (game, user): the curve; no low-pass, spread
        w.U8(1);
        w.U8(2);                                               // dB
        w.U16(volume.Count);
        foreach (var (x, y) in volume) { w.F32(x); w.F32(y); w.U32(4); }
        w.U16(0);                                              // no RTPCs
        Add(attenuations, 14, id, w);
        return id;
    }

    /// <summary>A sound playing a medium, once or for ever (<paramref name="loops"/>), under <paramref name="parent"/> or (0) out to <paramref name="output"/>.</summary>
    public uint Sound(string key, uint medium, bool loops, Settings settings, uint parent = 0, Output? output = null)
    {
        uint id = ObjectId("sound:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        w.U32(PcmPlugin);
        w.U32(1);          // streamed
        w.U32(medium);     // the source
        w.U32(medium);     // its file
        w.U8(0);
        NodeBase(w, settings, parent, output);
        w.S16(loops ? (short)0 : (short)1);
        w.S16(0);
        w.S16(0);
        Add(objects, 2, id, w);
        return id;
    }

    /// <summary>A random container: each time it plays, one of its children (made with this one's id as their parent: <see cref="ObjectId"/>("random:" + key)), not the one before.</summary>
    public uint Random(string key, IReadOnlyList<uint> children, Settings settings, uint parent = 0, Output? output = null)
    {
        uint id = ObjectId("random:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        NodeBase(w, settings, parent, output);
        w.S16(1);                                   // plays once
        w.F32(0); w.F32(0); w.F32(0);               // no transition
        w.U16(children.Count > 1 ? 1 : 0);          // not the same one twice running
        foreach (byte b in new byte[] { 0, 0, 0, 0, 1, 0, 0, 1 }) w.U8(b);  // no transition, normal random, random, ..., reset, ..., not continuous, global
        w.U32((uint)children.Count);
        foreach (uint c in children) w.U32(c);
        w.U16(children.Count);
        foreach (uint c in children) { w.U32(c); w.U8(50); }
        Add(objects, 5, id, w);
        return id;
    }

    /// <summary>
    /// A container that goes on playing while its event plays: one of its children (made with this one's id as their
    /// parent: <see cref="ObjectId"/>("retriggered:" + key)) every <paramref name="intervalMs"/>, each to its end, so
    /// they overlap: a gun's single shot at its rate of fire. A random container laid out as Armageddon's own automatic
    /// guns' (wep_enforcer_pc's: loop count 0 for ever, continuous, transition mode 5 "trigger rate", shuffled), but
    /// with the interval in it; Armageddon's take theirs from the game parameter WEP_REFIRE_DELAY_SEC. A Break action
    /// (<see cref="Break"/>) ends it after the child that is playing.
    /// </summary>
    public uint Retriggered(string key, IReadOnlyList<uint> children, Settings settings, float intervalMs, uint parent = 0, Output? output = null)
    {
        uint id = ObjectId("retriggered:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        NodeBase(w, settings, parent, output);
        w.S16(0);                                   // for ever
        w.F32(intervalMs); w.F32(0); w.F32(0);      // the time between two, never varied
        w.U16(Math.Min(3, children.Count - 1));     // not one of the last few again
        // trigger rate, shuffle (when there are several), random, ..., reset, ..., continuous, global
        foreach (byte b in new byte[] { 5, (byte)(children.Count > 1 ? 1 : 0), 0, 0, 1, 0, 1, 1 }) w.U8(b);
        w.U32((uint)children.Count);
        foreach (uint c in children) w.U32(c);
        w.U16(children.Count);
        foreach (uint c in children) { w.U32(c); w.U8(50); }
        Add(objects, 5, id, w);
        return id;
    }

    /// <summary>A layer container: all its children (made with <see cref="ObjectId"/>("layer:" + key) as their parent) play at once.</summary>
    public uint Layer(string key, IReadOnlyList<uint> children, Settings settings, uint parent = 0, Output? output = null)
    {
        uint id = ObjectId("layer:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        NodeBase(w, settings, parent, output);
        w.U32((uint)children.Count);
        foreach (uint c in children) w.U32(c);
        w.U32(0);  // no crossfade groups: each child's own curves do it
        Add(objects, 9, id, w);
        return id;
    }

    /// <summary>
    /// The switch the game sets on a weapon's sound before it posts the weapon's event, and its values: the same
    /// event is posted for a shot, a pull of the trigger with no ammunition, an overheated gun, and a turret turning
    /// fast (that one is stopped when the turret slows).
    /// </summary>
    /// <summary>
    /// Which of an engine's two sets plays, a switch the guerrilla_vehicles mod sets on each engine's sound object:
    /// the player's own vehicle's (2D, stereo) or another's (3D, mono). Unset, the other's.
    /// </summary>
    public static readonly uint EngineListener = Id("RFG_ENGINE_LISTENER"), ListenerPlayer = Id("player"), ListenerOther = Id("other");

    public static readonly uint WeaponSoundType = Id("WEAPON_SOUND_TYPE"), PrimaryFire = Id("primary_fire"), NoAmmo = Id("no_ammo"),
                                Overheat = Id("overheat"), TurretTurn = Id("turret_turn");

    /// <summary>
    /// A switch container: it plays the child chosen for the value the switch <paramref name="group"/> has on the
    /// game object (children made with this one's id as their parent: <see cref="ObjectId"/>("switch:" + key)), and
    /// nothing for a value with no child or one not listed. Laid out as Armageddon's weapons' own (each of its
    /// weapon events plays one, on WEAPON_SOUND_TYPE).
    /// </summary>
    public uint Switch(string key, uint group, IReadOnlyList<(uint Value, uint? Child)> choices, Settings settings, uint parent = 0, Output? output = null,
                       uint defaultValue = 0, bool followsChanges = false, int fadeMs = 0)
    {
        uint id = ObjectId("switch:" + key);
        if (taken.Contains(id)) return id;
        var children = choices.Where(c => c.Child != null).Select(c => c.Child!.Value).Distinct().ToList();
        var w = new Writer();
        NodeBase(w, settings, parent, output);
        w.U32(0);                                   // a switch (1: a state)
        w.U32(group);
        w.U32(defaultValue);                        // what plays while the switch isn't set on the game object (0: nothing)
        w.U8(followsChanges ? (byte)1 : (byte)0);   // checked again while it plays: a change of the switch stops one child and starts the other
        w.U32((uint)children.Count);
        foreach (uint c in children) w.U32(c);
        w.U32((uint)choices.Count);
        foreach (var (value, child) in choices)
        {
            w.U32(value);
            w.U32(child == null ? 0u : 1u);
            if (child != null) w.U32(child.Value);
        }
        w.U32((uint)children.Count);
        foreach (uint c in children)
        {
            w.U32(c);
            w.U8(0); w.U8(0);                       // not only the first time, not carried on
            w.U32(1);                               // a change of the switch stops it
            w.S32(fadeMs); w.S32(fadeMs);           // fading out, and the one it changes to fading in
        }
        Add(objects, 6, id, w);
        return id;
    }

    /// <summary>An action playing <paramref name="target"/> (an object of this bank) on the event's game object, after a delay, fading in.</summary>
    public uint Play(string key, uint target, int delayMs = 0, int fadeMs = 0)
    {
        uint id = ObjectId("play:" + key);
        if (taken.Contains(id)) return id;
        var w = ActionHead(0x4011, target, delayMs, fadeMs);
        w.U32(bankId);
        Add(objects, 3, id, w);
        return id;
    }

    /// <summary>An action stopping <paramref name="target"/> on the event's game object, fading out.</summary>
    public uint Stop(string key, uint target, int fadeMs = 0)
    {
        uint id = ObjectId("stop:" + key);
        if (taken.Contains(id)) return id;
        Add(objects, 3, id, ActionHead(0x1011, target, 0, fadeMs));
        return id;
    }

    /// <summary>
    /// An action ending <paramref name="target"/>'s repeating on the event's game object: what is playing plays to its
    /// end and nothing follows it. As Armageddon's own automatic guns' stop events have it (type 0x90011: the type, the
    /// target, 16 bytes of zero).
    /// </summary>
    public uint Break(string key, uint target)
    {
        uint id = ObjectId("break:" + key);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        w.U32(0x90011);
        w.U32(target);
        for (int i = 0; i < 4; i++) w.U32(0);
        Add(objects, 3, id, w);
        return id;
    }

    /// <summary>An event (posted by its name) running <paramref name="actions"/>.</summary>
    public uint Event(string eventName, params uint[] actions)
    {
        uint id = Id(eventName);
        if (taken.Contains(id)) return id;
        var w = new Writer();
        w.U32((uint)actions.Length);
        foreach (uint a in actions) w.U32(a);
        Add(objects, 4, id, w);
        return id;
    }

    static Writer ActionHead(uint type, uint target, int delayMs, int fadeMs)
    {
        var w = new Writer();
        w.U32(type);
        w.U32(target);
        w.S32(delayMs); w.S32(0); w.S32(0);
        w.U32(33);
        w.S32(fadeMs); w.S32(0); w.S32(0);
        w.U8(4);                     // linear
        w.U8(1);
        w.U8(0); w.U8(0); w.U8(0);
        w.F32(0); w.F32(0); w.F32(0);
        w.U32(0);
        return w;
    }

    // The settings every sound and container has (Armageddon's layout).
    static void NodeBase(Writer w, Settings s, uint parent, Output? output)
    {
        w.U8(0); w.U8(0);                                        // no effects
        w.U32(parent == 0 ? output?.Bus ?? VehicleEffectsBus : 0);  // its bus, when it hangs from nothing
        w.U32(parent);
        w.U8(s.Priority); w.U8(s.OwnPriority ? (byte)1 : (byte)0); w.U8(0); w.S8(-10);  // priority, its own or its parent's, ..., distance offset
        foreach (float f in new[] { s.Volume, 0f, 0f, 0f, 0f, 0f, s.Pitch, 0f, 0f, 0f, 0f, 0f }) w.F32(f);
        w.U32(0);                                                // no state group
        if (output == null) w.U8(0);                             // positioned as its parent (a child given an output is placed by it: its bus stays its parent's)
        else if (output.ThreeD)
        {
            w.U8(1); w.S32(0); w.F32(0); w.F32(100); w.U8(1);
            w.U32(3);                                            // game-defined (at the game object)
            w.U32(output.Attenuation);
            w.U8(1); w.U8(1);
        }
        else { w.U8(1); w.S32(0); w.F32(0); w.F32(100); w.U8(0); w.U8(0); }
        // When it's too quiet to hear: 0 it plays on, 2 it goes virtual (and starts from its beginning again: the
        // first byte, 0). The byte before the last says these are its own settings, not its parent's: Armageddon's
        // own virtual loops (sfx_streaming.bnk_pc's) have 0, 0, ..., 2, 0, 1, 0.
        // (Or 1: it is ended, EndsWhenQuiet.)
        byte whenQuiet = s.VirtualWhenQuiet ? (byte)2 : s.EndsWhenQuiet ? (byte)1 : (byte)0;
        // (The second byte: with the most at once playing, 0 the oldest is ended for a new one, 1 the new one doesn't start.)
        w.U8(0); w.U8(s.RefusesWhenFull ? (byte)1 : (byte)0); w.U16(s.MostAtOnce); w.U8(whenQuiet); w.U8(0); w.U8(whenQuiet != 0 ? (byte)1 : (byte)0); w.U8(0);
        w.U16(0);                                                // no states
        w.U16(s.Curves.Count);
        int n = 0;
        foreach (var c in s.Curves)
        {
            w.U32(0); w.U8(0xFF);
            w.U32(c.Parameter);
            w.U32((uint)c.Property);
            w.U32(Id($"rfg_curve:{parent}:{c.Parameter}:{c.Property}:{n++}:{c.Points.Count}:{(c.Points.Count > 0 ? c.Points[0].Y : 0)}"));
            w.U8(c.Property == Property.Volume ? (byte)2 : (byte)0);
            w.U16(c.Points.Count);
            foreach (var (x, y) in c.Points) { w.F32(x); w.F32(y); w.U32(4); }
        }
    }

    void Add(List<(uint, uint, byte[])> list, uint type, uint id, Writer w)
    {
        if (!taken.Add(id)) throw new InvalidDataException($"{name}: two objects with the id {id:X8}");
        list.Add((type, id, w.ToArray()));
    }

    /// <summary>The bank (<c>&lt;name&gt;.bnk_pc</c>) and its media (<c>&lt;name&gt;_media.bnk_pc</c>).</summary>
    public (byte[] Bank, byte[] Media) Write()
    {
        var bank = new List<byte>();
        void Chunk(string tag, byte[] body)
        {
            bank.AddRange(Encoding.ASCII.GetBytes(tag));
            bank.AddRange(BitConverter.GetBytes(body.Length));
            bank.AddRange(body);
        }
        Chunk("BKHD", [.. BitConverter.GetBytes(BankVersion), .. BitConverter.GetBytes(bankId), .. new byte[16]]);
        var hirc = new List<byte>(BitConverter.GetBytes(attenuations.Count + objects.Count));
        foreach (var (type, id, body) in attenuations.Concat(objects))  // attenuations first; the rest as made (children, then containers, actions, events)
        {
            hirc.AddRange(BitConverter.GetBytes(type));
            hirc.AddRange(BitConverter.GetBytes(body.Length + 4));
            hirc.AddRange(BitConverter.GetBytes(id));
            hirc.AddRange(body);
        }
        Chunk("HIRC", [.. hirc]);
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        Chunk("STID", [.. BitConverter.GetBytes(1), .. BitConverter.GetBytes(1), .. BitConverter.GetBytes(bankId), (byte)nameBytes.Length, .. nameBytes]);

        int header = 28 + 16 * media.Count, at = Align(header);
        var container = new List<byte>(Encoding.ASCII.GetBytes("VWSBPC  "));
        container.AddRange(BitConverter.GetBytes(0));
        container.AddRange(BitConverter.GetBytes((ushort)2));
        container.AddRange(BitConverter.GetBytes((ushort)1));
        container.AddRange(BitConverter.GetBytes(bankId));
        container.AddRange(BitConverter.GetBytes(header + 16));
        container.AddRange(BitConverter.GetBytes(media.Count));
        foreach (var (id, wem) in media)
        {
            container.AddRange(BitConverter.GetBytes(id));
            container.AddRange(BitConverter.GetBytes(at));
            container.AddRange(BitConverter.GetBytes(0));
            container.AddRange(BitConverter.GetBytes(wem.Length));
            at = Align(at + wem.Length);
        }
        foreach (var wem in media.Values)
        {
            container.AddRange(new byte[Align(container.Count) - container.Count]);
            container.AddRange(wem);
        }
        container.AddRange(new byte[Align(container.Count) - container.Count]);
        return ([.. bank], [.. container]);
    }

    /// <summary>How many objects (by type: 2 sounds, 3 actions, 4 events, 5 random, 6 switch, 9 layer, 14 attenuations) and media the bank has.</summary>
    public string Counts() =>
        string.Join(", ", attenuations.Concat(objects).GroupBy(o => o.Type).OrderBy(g => g.Key).Select(g => $"{g.Count()} {TypeName(g.Key)}")) + $", {media.Count} media";

    static string TypeName(uint type) => type switch { 2 => "sounds", 3 => "actions", 4 => "events", 5 => "random containers", 6 => "switch containers", 9 => "layer containers", 14 => "attenuations", _ => $"type {type}" };

    static int Align(int n) => (n + 2047) & ~2047;

    /// <summary>
    /// A PCM .wem as the game's own: RIFF/WAVE, fmt (extensible, 16-bit, the channels' mask), for a wave Guerrilla
    /// loops a smpl chunk with its loop, a 4-byte JUNK, data. The game streams its sounds from the media banks, and a
    /// streamed source loops by the loop its file names: Armageddon's Vorbis files keep theirs in their vorb chunk (and
    /// one in a smpl chunk, as here: one loop, start, end the last sample, play count 0); a PCM file without one played
    /// its loop once however looping its sound (Round 3: every engine went silent ~3 s after it started, its 1.7-2.0 s
    /// loops played once after the 0.9 s start delay).
    /// </summary>
    static byte[] PcmWem(GuerrillaSounds.Wave wave)
    {
        int channels = wave.Channels, dataSize = wave.Samples.Length * 2, frames = wave.Samples.Length / Math.Max(1, channels);
        bool loop = wave.Loops && frames > 1;
        var w = new List<byte>();
        void U32(int v) => w.AddRange(BitConverter.GetBytes(v));
        void U16(int v) => w.AddRange(BitConverter.GetBytes((ushort)v));
        w.AddRange(Encoding.ASCII.GetBytes("RIFF")); U32(4 + 8 + 24 + (loop ? 8 + 60 : 0) + 8 + 4 + 8 + dataSize);
        w.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        w.AddRange(Encoding.ASCII.GetBytes("fmt ")); U32(24);
        U16(0xFFFE); U16(channels); U32(wave.Rate); U32(wave.Rate * 2 * channels); U16(2 * channels); U16(16);
        U16(6); U16(16); U32(channels == 1 ? 4 : channels == 2 ? 3 : (1 << channels) - 1);
        if (loop)
        {
            // manufacturer, product, sample period, MIDI unity note 60, pitch fraction, SMPTE format and offset,
            // one loop, no sampler data; the loop: cue 0, forward, its start, its end (inclusive), no fraction, for ever.
            w.AddRange(Encoding.ASCII.GetBytes("smpl")); U32(60);
            foreach (int v in new[] { 0, 0, 0, 60, 0, 0, 0, 1, 0 }) U32(v);
            foreach (int v in new[] { 0, 0, Math.Clamp(wave.LoopStart, 0, frames - 1), frames - 1, 0, 0 }) U32(v);
        }
        w.AddRange(Encoding.ASCII.GetBytes("JUNK")); U32(4); U32(0);
        w.AddRange(Encoding.ASCII.GetBytes("data")); U32(dataSize);
        foreach (short s in wave.Samples) w.AddRange(BitConverter.GetBytes(s));
        return [.. w];
    }

    /// <summary>Little-endian bytes.</summary>
    sealed class Writer
    {
        readonly List<byte> bytes = [];
        public void U8(byte v) => bytes.Add(v);
        public void S8(sbyte v) => bytes.Add((byte)v);
        public void U16(int v) => bytes.AddRange(BitConverter.GetBytes((ushort)v));
        public void S16(short v) => bytes.AddRange(BitConverter.GetBytes(v));
        public void U32(uint v) => bytes.AddRange(BitConverter.GetBytes(v));
        public void S32(int v) => bytes.AddRange(BitConverter.GetBytes(v));
        public void F32(float v) => bytes.AddRange(BitConverter.GetBytes(v));
        public byte[] ToArray() => [.. bytes];
    }
}
