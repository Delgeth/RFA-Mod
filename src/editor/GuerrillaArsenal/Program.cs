// GuerrillaArsenal.exe [<Guerrilla folder>] [--vehicles <names> | all]
// GuerrillaArsenal.exe --remove          takes the vehicles away again (all of what it wrote)
//
// Makes Red Faction: Guerrilla's vehicles usable in Armageddon, for the guerrilla_world mod, from your
// own copy of Guerrilla (which is only read): see RfaFormats.Guerrilla.GuerrillaArsenal. What it makes
// stays on your machine. It lives in bin\editor\, next to the editor, and finds everything the way the
// editor does:
//   - the mod folder: the folder above it, the one with rfa_mods.ini;
//   - the game: GameDir in rfa_mods.ini, or the mod folder itself when the mod was copied into the
//     game folder;
//   - Guerrilla: the folder given, or GuerrillaDir under [Launcher] in rfa_mods.ini (the one with
//     build\pc\cache\vehicles_r.vpp_pc; the Steam Edition's is ...\Red Faction Guerrilla).
// It writes the vehicles into overrides\build\pc\cache\ and their table into mods\guerrilla_world\.
// --vehicles names Guerrilla's vehicles.xtbl entries to build (comma-separated, such as Col_Taxi_1 for
// the taxi), or all (the default: every one that converts). In the game: drop_car <the vehicle's
// display name> in the developer console (Taxi for the taxi).
using RfaFormats;
using RfaFormats.Guerrilla;

string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
string? parent = Path.GetDirectoryName(here);
string modFolder = parent != null && File.Exists(Path.Combine(parent, "rfa_mods.ini")) ? parent : here;
string ini = Path.Combine(modFolder, "rfa_mods.ini");
string overrides = Path.Combine(modFolder, "overrides"), worldFolder = Path.Combine(modFolder, "mods", "guerrilla_world");

string? guerrillaArgument = null;
var vehicles = new List<string>();
bool remove = false;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--vehicles" && i + 1 < args.Length)
        vehicles.AddRange(args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    else if (args[i].Equals("--remove", StringComparison.OrdinalIgnoreCase)) remove = true;
    else if (args[i].StartsWith("--")) return Fail($"Unknown option {args[i]}. Use: GuerrillaArsenal.exe [<Guerrilla folder>] [--vehicles <names> | all], or --remove");
    else guerrillaArgument = args[i];
}
if (vehicles.Count == 0) vehicles.AddRange(GuerrillaArsenal.DefaultVehicles);

string? gameDir = IniValue(ini, "Launcher", "GameDir")?.TrimEnd('\\');
string? game = GameFolder.IsGameFolder(gameDir) ? gameDir : GameFolder.IsGameFolder(modFolder) ? modFolder : null;
if (game == null)
    return Fail($"Can't find Red Faction: Armageddon: set GameDir in {ini} to the folder with {GameFolder.ExeName}.");

if (remove)
{
    foreach (string line in GuerrillaArsenal.Remove(game, overrides, worldFolder)) Console.WriteLine(line);
    return 0;
}

string? guerrilla = guerrillaArgument ?? IniValue(ini, "Launcher", "GuerrillaDir");
if (string.IsNullOrWhiteSpace(guerrilla))
    return Fail($"Which copy of Red Faction: Guerrilla? Give its folder, or set GuerrillaDir under [Launcher] in {ini}.");
guerrilla = guerrilla.Trim().Trim('"').TrimEnd('\\');
if (!File.Exists(Path.Combine(guerrilla, "build", "pc", "cache", "vehicles_r.vpp_pc")))
    return Fail(File.Exists(Path.Combine(guerrilla, "data", "vehicles_r.vpp_pc"))
        ? $"{guerrilla} is Red Faction: Guerrilla Re-MARS-tered, which this doesn't read yet (its files are newer versions). Use the original Steam Edition, Red Faction: Guerrilla."
        : $"{guerrilla} isn't a Red Faction: Guerrilla folder (it has no build\\pc\\cache\\vehicles_r.vpp_pc).");

Console.WriteLine($"Guerrilla:  {guerrilla}");
Console.WriteLine($"Armageddon: {game}");
try
{
    foreach (string line in GuerrillaArsenal.Build(guerrilla, game, overrides, worldFolder, vehicles)) Console.WriteLine(line);
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
{
    return Fail(error.Message);
}
Console.WriteLine("Done. In the game, open the developer console and type: drop_car Taxi");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

// A key's value in an ini file, as the editor reads it: [section], key=value, ; comments.
static string? IniValue(string path, string section, string key)
{
    if (!File.Exists(path)) return null;
    string current = "";
    foreach (string raw in File.ReadLines(path))
    {
        string line = raw.Trim();
        if (line.StartsWith('[') && line.EndsWith(']'))
            current = line[1..^1].Trim();
        else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && !line.StartsWith(';'))
        {
            int equals = line.IndexOf('=');
            if (equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(equals + 1)..].Trim();
        }
    }
    return null;
}
