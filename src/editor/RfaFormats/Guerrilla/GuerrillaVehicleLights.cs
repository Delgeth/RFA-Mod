using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's vehicles' lights, as Armageddon links them. Armageddon links each light of a vehicle's file
/// (by the name hash in the word it added to each light) to a row of its vehicle_lights.xtbl, through the
/// vehicle's Lights map (vehicles.xtbl: LightName → VehLightType); a light with none never turns on.
/// Guerrilla has no such table: every vehicle's lights are of seven types (the type hash each light
/// starts with: Headlight_Mesh, ...), each with Guerrilla's vehicle defaults' effect (vehicle_defaults.xtbl
/// Effects; no vehicle names its own), played at the light's "on" tags (no lamp: a glow and its coronas),
/// and broken lights play veh_broken_light_fx_a (Guerrilla's code names it). So the converter writes each
/// light's type as its name (<see cref="GuerrillaVehicles.LightTypes"/>), and this adds a row for each type
/// to Armageddon's table (<see cref="Rows"/>: its kind as Armageddon's nearest, its effects Guerrilla's under
/// the names the world builder adds them by, breakable) and the map to each vehicle. When each one is on is
/// Guerrilla's rule, which the guerrilla_vehicles mod plays (headlights by the time of day, the tail lights
/// as brake lights while braking, emergency lights blinking).
/// </summary>
public static class GuerrillaVehicleLights
{
    public const string Table = "vehicle_lights.xtbl";

    /// <summary>The effect Guerrilla's code plays where a light breaks.</summary>
    public const string BrokenLight = "veh_broken_light_fx_a";

    /// <summary>Each of Guerrilla's light types: our row, Armageddon's kind for it, and the vehicle defaults' effect fields it plays.</summary>
    public static readonly (string Type, string Row, string Kind, string Primary, string? Secondary)[] Rows =
    [
        ("Headlight_Mesh", "rfg_Head_light", "Headlight", "Head_light_Primary", "Head_Light_Secondary"),
        ("Foglight_Mesh", "rfg_Fog_Light", "Headlight", "Fog_Light", null),
        ("Taillight_Mesh", "rfg_Tail_Light", "Taillight", "Tail_Light", null),
        ("Brakelight_Mesh", "rfg_Brake_light", "Brakelight", "Brake_light", null),
        ("Reverselight_Mesh", "rfg_Reverse_light", "Reverse Light", "Reverse_light", null),
        ("Emergencylight_Mesh", "rfg_Emergency_light", "Running Light", "Emergency_light", null),
        ("Strobelight_Mesh", "rfg_Strobe_Light", "Running Light", "Strobe_Light", null),
    ];

    /// <summary>Our row for one of Guerrilla's light types.</summary>
    public static string RowFor(string type) => Rows.First(r => r.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).Row;

    static XtblElement? Defaults(Packfile guerrillaTables) =>
        XtblFile.FromBytes(guerrillaTables.Read(guerrillaTables.Find("vehicle_defaults.xtbl") ?? throw new InvalidDataException("no vehicle_defaults.xtbl in Guerrilla's misc.vpp_pc")))
            .Entries.FirstOrDefault()?.Element("Effects");

    /// <summary>Guerrilla's light effects (its names), for the world builder to add to the effects table.</summary>
    public static IReadOnlyList<string> Effects(Packfile guerrillaTables)
    {
        var effects = Defaults(guerrillaTables);
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { BrokenLight };
        foreach (var row in Rows)
            foreach (string? field in new[] { row.Primary, row.Secondary })
                if (field != null && effects?.ValueOf(field)?.Trim() is { Length: > 0 } effect && !effect.Equals("null", StringComparison.OrdinalIgnoreCase)) names.Add(effect);
        return [.. names];
    }

    /// <summary>
    /// Writes vehicle_lights.xtbl (Armageddon's rows with ours) into the mod's folder; an effect the world's
    /// tables haven't yet (<paramref name="worldEffects"/>) is left out of its row and named in the line it gives.
    /// </summary>
    public static string Write(Packfile guerrillaTables, Packfile gameTables, string modFolder, ISet<string> worldEffects)
    {
        var table = XtblFile.FromBytes(gameTables.Read(gameTables.Find(Table) ?? throw new InvalidDataException($"no {Table} in Armageddon's misc.vpp_pc")));
        var effects = Defaults(guerrillaTables);
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string? Ported(string? effect)
        {
            if (string.IsNullOrEmpty(effect) || effect.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            string name = GuerrillaEffectTables.Added(effect);
            if (worldEffects.Contains(name)) return name;
            missing.Add(name);
            return null;
        }
        foreach (var (_, rowName, kind, primary, secondary) in Rows)
        {
            var row = new XtblElement("VehicleLights");
            row.AddElement("Name", rowName);
            row.AddElement("Type", kind);
            if (Ported(effects?.ValueOf(primary)?.Trim()) is { } p) row.AddElement("Primary_Glare", p);
            if (secondary != null && Ported(effects?.ValueOf(secondary)?.Trim()) is { } s) row.AddElement("Secondary_Glare", s);
            row.AddElement("Breakable", "True");
            if (Ported(BrokenLight) is { } broken) row.AddElement("Death_Effect", broken);
            table.Table!.AddElement(row);
        }
        Directory.CreateDirectory(modFolder);
        File.WriteAllBytes(Path.Combine(modFolder, Table), table.ToBytes());
        return $"Lights: Guerrilla's {Rows.Length} light types in {Table}" +
               (missing.Count > 0 ? $"; effects the world's tables haven't yet (build the world again, then the vehicles): {string.Join(", ", missing)}" : "");
    }
}
