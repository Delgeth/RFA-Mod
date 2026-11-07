using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's vehicles' own sounds, as Guerrilla plays them, in banks of Armageddon's kind (see
/// <see cref="GuerrillaSoundBanks"/>), named in each vehicle's Foley fields as Armageddon's are.
///
/// The engine (Guerrilla's foley_engine.xtbl entry the vehicle names; its audio code,
/// audiolib_process_vehicle_engines): up to four on-load and four off-load looping layers, each a slot by
/// its sound's name (IDLE 0, RPM1 1, RPM2 2, RPM3 3), each heard between its two RPMs (its volume from the
/// one end's to the other's), crossfading with the next slot's where they overlap (Guerrilla's fixed
/// "lower" and "higher" crossover curves), pitched from its one end's to the other's (Guerrilla's "Pitch"
/// −1..1 is 2 to the power of it: ±1200 cents; the IDLE layer never), the off-load layers fading out as the
/// engine's load rises (its Offload_Envelope's points, a gain against the load) and the on-load layers of a
/// slot that has an off-load layer fading in (Onload_Envelope); a damage loop at its Volume_Scalar once the
/// vehicle is damaged. The player's own vehicle plays the stereo loops in 2D, the others the mono ones in 3D
/// at the vehicle (Guerrilla's NPC engine distances, 3-100 m). In Wwise: two layer containers (the player's,
/// 2D; the others', 3D), their loops each with curves on the game parameters the game sets on the engine's
/// sound every frame (RPM, ENGINE_LOAD, DAMAGE) and the mod's RFG_PLAYER_IN_VEHICLE (guerrilla_vehicles: 1
/// while the player is in that vehicle; the two containers fade on it).
///
/// The vehicle's events: Armageddon's Foley Engine_Start (posted when the engine starts, stopped when it
/// stops) is the vehicle's engine event: Guerrilla's starter sound (its Engine_Start cue), then the
/// engine's layers after its Idle_time_start_offset; its stop pair: the layers fade out (200 ms) and the
/// stopping sound (Engine_Off) plays. The vehicle's other Foley cues (horn, impacts, scrapes, landings, the
/// pass-by, gear shifts, wheel peel, a body hit) are played once at the vehicle, by Armageddon's fields of
/// the same names; Guerrilla's horn plays once a press (Horn_Is_One_Shot). The doors' sounds are the
/// interactions' (Door_open / Door_close; GuerrillaArsenal names them through <see cref="GuerrillaSoundBanks.OneShot"/>).
///
/// Not as Guerrilla: Armageddon's RPM is its own (eased differently, no gears); its DAMAGE is 0 or 1 (the
/// damage loop is off or at its full volume); Guerrilla starts only the IDLE layer late, here the whole
/// engine starts after the delay; a layer out of its range keeps playing silently (Guerrilla stops it and
/// starts it again from the top); Guerrilla's gains are straight lines between points, Wwise's curves
/// straight lines of dB (sampled finely).
/// </summary>
public static class GuerrillaVehicleSounds
{
    /// <summary>The part's name and its banks' prefix (rfg_veh_civ, ...).</summary>
    public const string Part = "vehicles", BankPrefix = "rfg_";

    /// <summary>Armageddon's vehicle Foley fields that take a Guerrilla cue of the same field, played once.</summary>
    static readonly string[] OneShotFields =
        ["Engine_Peel", "Gear_Shift", "Horn", "Low_Impact", "High_Impact", "Scraping", "Corpse_Impact", "Chassis_Land_Low", "Chassis_Land_High", "Pass_By"];

    /// <summary>Guerrilla's Foley fields Armageddon has none for.</summary>
    static readonly string[] NoField = ["Engine_Damage_Release"];

    // Guerrilla's crossover curves (audiolib_rolloff_curve_lower/higher_range_crossover_entries), a gain at t = 0, 0.1, ..., 1.
    static readonly (float X, float Y)[] Lower = [(0, 1), (.1f, 1), (.2f, .9f), (.3f, .8f), (.4f, .7f), (.5f, .5f), (.6f, .3f), (.7f, .2f), (.8f, .1f), (.9f, 0), (1, 0)];
    static readonly (float X, float Y)[] Higher = [(0, 0), (.1f, .1f), (.2f, .2f), (.3f, .3f), (.4f, .5f), (.5f, .7f), (.6f, .8f), (.7f, .9f), (.8f, 1), (.9f, 1), (1, 1)];

    /// <summary>One of an engine's layers (Guerrilla's audiolib_vehicle_engine_rpm_data).</summary>
    sealed record Layer(int Slot, string Sound, string? Mono, float MinRpm, float MaxRpm, float MinVolume, float MaxVolume, float MinPitch, float MaxPitch);

    /// <summary>An engine made: its bank, its two layer containers and its start delay.</summary>
    sealed record Engine(WwiseBankWriter Bank, uint Player, uint Others, int StartDelay);

    /// <summary>
    /// Builds the sounds of <paramref name="entries"/> (Guerrilla's vehicles.xtbl entries) into
    /// <paramref name="sounds"/>; gives each vehicle's Foley fields (field → event name or value).
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Build(GuerrillaSoundBanks sounds, Packfile guerrillaTables, IEnumerable<XtblElement> entries, List<string> log)
    {
        var foley = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var engineTable = XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("foley_engine.xtbl") ?? throw new InvalidDataException("no foley_engine.xtbl in Guerrilla's misc.vpp_pc"))).Entries
            .GroupBy(XtblFile.EntryName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var engines = new Dictionary<string, Engine?>(StringComparer.OrdinalIgnoreCase);
        int engineEvents = 0;
        var oneShots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            string vehicle = XtblFile.EntryName(entry);
            var rfgFoley = entry.Element("Foley");
            if (rfgFoley == null) continue;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? Cue(string field) => rfgFoley.ValueOf(field)?.Trim() is { Length: > 0 } v ? v : null;

            // The engine.
            if (Cue("Engine") is { } engineName)
            {
                if (!engines.TryGetValue(engineName, out var engine))
                    engines[engineName] = engine = engineTable.TryGetValue(engineName, out var engineEntry) ? MakeEngine(sounds, engineName, engineEntry, log) : null;
                if (engine == null) log.Add($"{vehicle}: no engine {engineName} could be made");
                else
                {
                    var bank = engine.Bank;
                    string eventName = $"RFG_{vehicle.ToUpperInvariant()}_ENGINE", stopName = eventName + "_STOP";
                    var start = new List<uint>();
                    var stop = new List<uint> { bank.Stop($"{vehicle}:player", engine.Player, 200), bank.Stop($"{vehicle}:others", engine.Others, 200) };
                    if (Cue("Engine_Start") is { } startCue && OneShotIn(sounds, bank, startCue) is { } starter) start.Add(bank.Play($"{vehicle}:start", starter));
                    start.Add(bank.Play($"{vehicle}:player", engine.Player, engine.StartDelay, 200));
                    start.Add(bank.Play($"{vehicle}:others", engine.Others, engine.StartDelay, 200));
                    if (Cue("Engine_Off") is { } offCue && OneShotIn(sounds, bank, offCue) is { } stopper) stop.Add(bank.Play($"{vehicle}:stop", stopper));
                    bank.Event(eventName, [.. start]);
                    bank.Event(stopName, [.. stop]);
                    sounds.StopPair(eventName, stopName);
                    fields["Engine_Start"] = eventName;
                    engineEvents++;
                }
            }
            // Its other sounds, once each.
            foreach (string field in OneShotFields)
                if (Cue(field) is { } cue && sounds.OneShot(cue) is { } eventName)
                {
                    fields[field] = eventName;
                    oneShots.Add(cue);
                }
            if (fields.ContainsKey("Horn")) fields["Horn_Is_One_Shot"] = "True";
            foreach (string field in NoField)
                if (Cue(field) is { } cue) log.Add($"{vehicle}: {field} ({cue}): Armageddon's vehicles have no such sound");
            if (fields.Count > 0) foley[vehicle] = fields;
        }
        log.Add($"Engines: {engines.Values.Count(e => e != null)} of Guerrilla's ({engineEvents} vehicles' engine events with their stop events); " +
                $"{oneShots.Count} other cues played once ({string.Join(", ", oneShots)})");
        return foley;
    }

    /// <summary>A cue played once at the vehicle (3D, its distances), made in <paramref name="bank"/> (the engine's events play it from there).</summary>
    static uint? OneShotIn(GuerrillaSoundBanks sounds, WwiseBankWriter bank, string cue) =>
        sounds.CueObject(bank, cue, "once:" + cue, loops: false,
                         output: new(WwiseBankWriter.VehicleEffectsBus, sounds.Attenuation(bank, sounds.DistanceIndex(cue)), true));

    /// <summary>An engine's two layer containers (the player's 2D stereo loops, the others' 3D mono ones), with their damage loops.</summary>
    static Engine? MakeEngine(GuerrillaSoundBanks sounds, string name, XtblElement engine, List<string> log)
    {
        string? soundBank = engine.ValueOf("Soundbank")?.Trim();
        if (string.IsNullOrEmpty(soundBank)) { log.Add($"engine {name}: no sound bank named"); return null; }
        if (!(engine.ValueOf("Playback_type")?.Trim() ?? "Engine").Equals("Engine", StringComparison.OrdinalIgnoreCase)) { log.Add($"engine {name}: not an engine"); return null; }
        var bank = sounds.Bank(soundBank);
        var on = Layers(engine.Element("RPM_ONLOAD_range_list"));
        var off = Layers(engine.Element("RPM_OFFLOAD_range_list"));
        if (on.Count + off.Count == 0) { log.Add($"engine {name}: no layers"); return null; }
        var onEnvelope = Envelope(engine.Element("Onload_Envelope"));
        var offEnvelope = Envelope(engine.Element("Offload_Envelope"));
        int delay = int.TryParse(engine.ValueOf("Idle_time_start_offset")?.Trim(), out int d) ? d : 250;

        uint player = bank.ObjectId($"layer:{name}:player"), others = bank.ObjectId($"layer:{name}:others");
        var playerLoops = new List<uint>();
        var otherLoops = new List<uint>();
        int otherDistances = -1;
        foreach (var (layers, isOn) in new[] { (off, false), (on, true) })
            foreach (var layer in layers.Values)
            {
                var curves = Curves(layer, layers, isOn, isOn ? (off.ContainsKey(layer.Slot) ? onEnvelope : null) : offEnvelope);
                string tag = $"{name}:{(isOn ? "on" : "off")}{layer.Slot}";
                if (sounds.CueObject(bank, layer.Sound, tag + ":player", loops: true, Loop(curves), player) is { } p) playerLoops.Add(p);
                string mono = layer.Mono ?? layer.Sound;
                if (sounds.CueObject(bank, mono, tag + ":others", loops: true, Loop(curves), others) is { } o) otherLoops.Add(o);
                if (otherDistances < 0) otherDistances = sounds.DistanceIndex(mono);
            }
        // The damage loop.
        var damage = engine.Element("Damage_Sounds");
        float scalar = GuerrillaSoundBanks.Number(damage?.ValueOf("Volume_Scalar"));
        var damageCurve = new WwiseBankWriter.Curve(WwiseBankWriter.Damage, WwiseBankWriter.Property.Volume,
            [(0, WwiseBankWriter.Silent), (0.5f, WwiseBankWriter.Silent), (0.51f, GuerrillaSoundBanks.Db(scalar)), (1, GuerrillaSoundBanks.Db(scalar))]);
        // (Guerrilla sets its volume itself, the scalar × the damage, in place of the cue's.)
        WwiseBankWriter.Settings DamageLoop(string cue) =>
            new() { Curves = [damageCurve], Volume = -GuerrillaSoundBanks.Db(sounds.Sounds.FindCue(cue)?.Volume ?? 1) };
        if (damage?.ValueOf("Foley")?.Trim() is { Length: > 0 } damageCue && scalar > 0 &&
            sounds.CueObject(bank, damageCue, $"{name}:damage:player", loops: true, DamageLoop(damageCue), player) is { } dp) playerLoops.Add(dp);
        if ((damage?.ValueOf("Mono_Foley")?.Trim() is { Length: > 0 } m ? m : damage?.ValueOf("Foley")?.Trim()) is { Length: > 0 } monoDamage && scalar > 0 &&
            sounds.CueObject(bank, monoDamage, $"{name}:damage:others", loops: true, DamageLoop(monoDamage), others) is { } dn) otherLoops.Add(dn);

        // The player's 2D, the others' 3D, each heard on its side of RFG_PLAYER_IN_VEHICLE.
        var playerSettings = new WwiseBankWriter.Settings
        {
            Curves = [new(WwiseBankWriter.PlayerInVehicle, WwiseBankWriter.Property.Volume, [(0, WwiseBankWriter.Silent), (1, 0)])],
        };
        var otherSettings = new WwiseBankWriter.Settings
        {
            Curves = [new(WwiseBankWriter.PlayerInVehicle, WwiseBankWriter.Property.Volume, [(0, 0), (1, WwiseBankWriter.Silent)])],
        };
        bank.Layer($"{name}:player", playerLoops, playerSettings, 0, new(WwiseBankWriter.EngineBus, 0, false));
        bank.Layer($"{name}:others", otherLoops, otherSettings, 0, new(WwiseBankWriter.EngineBus, sounds.Attenuation(bank, otherDistances), true));
        return new Engine(bank, player, others, delay);
    }

    // An engine's loops (and its damage loop and layer containers) keep playing while quiet, as every one of
    // Armageddon's own engine loops does (its vehicles.bnk_pc: the below-threshold byte 0). Set to go virtual (2),
    // the game's Wwise ended a loop once its curves made it silent, and the whole engine sound once every loop was
    // at the same moment (Round 2: the Kitsune's, whose RPM1 layer is silent below 1,008 rpm and off-load layer at
    // full load, went silent for good three seconds into a drive, as the RPM left its idle layer's range; the sound's
    // game object 0 from then on). The quiet loops play on unheard, as Armageddon's engines' do.
    static WwiseBankWriter.Settings Loop(List<WwiseBankWriter.Curve> curves) => new() { Curves = curves };

    /// <summary>A range list's layers by slot (from the sound's name: IDLE 0, RPM1 1, RPM2 2, RPM3 3, as Guerrilla's parser).</summary>
    static Dictionary<int, Layer> Layers(XtblElement? list)
    {
        var layers = new Dictionary<int, Layer>();
        foreach (var e in list?.Elements ?? [])
        {
            string? sound = e.ValueOf("Sound")?.Trim();
            if (string.IsNullOrEmpty(sound)) continue;
            int slot = sound.Contains("IDLE") ? 0 : sound.Contains("RPM1") ? 1 : sound.Contains("RPM2") ? 2 : sound.Contains("RPM3") ? 3 : -1;
            if (slot < 0) continue;
            var low = e.Element("Range_minimums");
            var high = e.Element("Range_maximums");
            float N(XtblElement? x, string f) => GuerrillaSoundBanks.Number(x?.ValueOf(f));
            layers[slot] = new Layer(slot, sound, e.ValueOf("Mono_Sound")?.Trim() is { Length: > 0 } mono ? mono : null,
                                     N(low, "RPM"), N(high, "RPM"), N(low, "Volume"), N(high, "Volume"), N(low, "Pitch"), N(high, "Pitch"));
        }
        return layers;
    }

    /// <summary>An envelope's points (each "x,y,..."; x the load, y a gain), with Guerrilla's last point added (1, 1 if the last y is over 0.5, else 0); null if none or more than 15.</summary>
    static List<(float X, float Y)>? Envelope(XtblElement? envelope)
    {
        var points = new List<(float, float)>();
        foreach (var p in envelope?.Elements ?? [])
        {
            var parts = p.Value.Split(',');
            if (parts.Length >= 2) points.Add((GuerrillaSoundBanks.Number(parts[0]), GuerrillaSoundBanks.Number(parts[1])));
        }
        if (points.Count == 0 || points.Count > 15) return null;
        points.Add((1, points[^1].Item2 > 0.5f ? 1 : 0));
        return points;
    }

    /// <summary>Guerrilla's curve lookup: from the first point after t back to the one before, a straight line; past them all, 1.</summary>
    static float Lookup(IReadOnlyList<(float X, float Y)> points, float t)
    {
        for (int i = 1; i < points.Count; i++)
            if (points[i].X > t)
            {
                var (x0, y0) = points[i - 1];
                var (x1, y1) = points[i];
                float f = x1 - x0 > 0 ? (t - x0) / (x1 - x0) : 0.5f;
                return y0 + f * (y1 - y0);
            }
        return 1;
    }

    /// <summary>A layer's gain at an RPM (0 out of its range), its RPM range and crossfades, Guerrilla's way.</summary>
    static float Gain(Layer layer, IReadOnlyDictionary<int, Layer> set, float rpm)
    {
        if (!(rpm >= layer.MinRpm && layer.MaxRpm > rpm)) return 0;
        float crossfade = 1;
        if (layer.Slot < 3 && set.TryGetValue(layer.Slot + 1, out var next) && rpm >= next.MinRpm && layer.MaxRpm > next.MinRpm)
        {
            float t = (rpm - next.MinRpm) / (layer.MaxRpm - next.MinRpm);
            crossfade = t < 0 ? 1 : t >= 1 ? 0 : Lookup(Lower, t);
        }
        if (layer.Slot > 0 && set.TryGetValue(layer.Slot - 1, out var previous) && previous.MaxRpm > rpm && previous.MaxRpm > layer.MinRpm)
        {
            float t = (rpm - layer.MinRpm) / (previous.MaxRpm - layer.MinRpm);
            crossfade = t < 0 ? 0 : t >= 1 ? 1 : Lookup(Higher, t);
        }
        float along = layer.MaxRpm > layer.MinRpm ? (rpm - layer.MinRpm) / (layer.MaxRpm - layer.MinRpm) : 0;
        float volume = layer.MinVolume + (layer.MaxVolume - layer.MinVolume) * along;
        return Math.Clamp(volume * crossfade, 0, 1);
    }

    /// <summary>A layer's curves: volume and pitch on the RPM, volume on the load (<paramref name="envelope"/>: on-load fading in, off-load out; null for none).</summary>
    static List<WwiseBankWriter.Curve> Curves(Layer layer, IReadOnlyDictionary<int, Layer> set, bool onLoad, List<(float X, float Y)>? envelope)
    {
        var curves = new List<WwiseBankWriter.Curve>();
        // Volume on the RPM: every edge, fine steps through the range and the crossfades.
        var xs = new SortedSet<float> { 0, layer.MinRpm, layer.MaxRpm, 20000 };
        if (layer.MinRpm > 0.02f) xs.Add(layer.MinRpm - 0.01f);
        xs.Add(layer.MaxRpm - 0.01f);
        void Steps(float from, float to, int n)
        {
            if (to <= from) return;
            for (int k = 0; k <= n; k++) xs.Add(from + (to - from) * k / n);
        }
        Steps(layer.MinRpm, layer.MaxRpm, 16);
        foreach (var other in set.Values.Where(o => o.Slot != layer.Slot))
        {
            Steps(Math.Max(layer.MinRpm, other.MinRpm), Math.Min(layer.MaxRpm, other.MaxRpm), 20);
            if (other.MinRpm > 0.02f) xs.Add(other.MinRpm - 0.01f);
            xs.Add(other.MinRpm);
        }
        curves.Add(new(WwiseBankWriter.Rpm, WwiseBankWriter.Property.Volume,
                       xs.Where(x => x >= 0).Select(x => (x, GuerrillaSoundBanks.Db(Gain(layer, set, x)))).ToList()));
        // Pitch on the RPM (never the IDLE layer's), held within ±1200 cents.
        if (layer.Slot != 0 && (layer.MinPitch != 0 || layer.MaxPitch != 0))
        {
            var pitch = new List<(float, float)>();
            for (int k = 0; k <= 8; k++)
            {
                float t = k / 8f, rpm = layer.MinRpm + (layer.MaxRpm - layer.MinRpm) * t;
                pitch.Add((rpm, 1200 * Math.Clamp(layer.MinPitch + (layer.MaxPitch - layer.MinPitch) * t, -1, 1)));
            }
            curves.Add(new(WwiseBankWriter.Rpm, WwiseBankWriter.Property.Pitch, pitch));
        }
        // Volume on the load.
        if (envelope != null)
        {
            var loads = new SortedSet<float>(envelope.Select(p => p.X).Where(x => x >= 0 && x <= 1));
            for (int k = 0; k <= 20; k++) loads.Add(k / 20f);
            float LoadGain(float x) => onLoad ? (x < 0 ? 0 : x >= 1 ? 1 : Lookup(envelope, x)) : (x < 0 ? 1 : x >= 1 ? 0 : Lookup(envelope, x));
            curves.Add(new(WwiseBankWriter.EngineLoad, WwiseBankWriter.Property.Volume, loads.Select(x => (x, GuerrillaSoundBanks.Db(LoadGain(x)))).ToList()));
        }
        return curves;
    }
}
