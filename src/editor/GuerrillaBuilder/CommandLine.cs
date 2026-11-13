using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GuerrillaBuilder;

/// <summary>
/// The builder as a command-line tool: GuerrillaBuilder.exe build | remove | status, for scripts and
/// for building one part with options while working on it. The window does the same with the
/// defaults. The program is a window program, so it writes to the console it was started from (or
/// to the pipe or file its output goes to); in a command prompt the prompt comes back before it ends
/// (start /wait GuerrillaBuilder.exe ... waits), in PowerShell pipe it (| Out-Host) to wait.
/// </summary>
static class CommandLine
{
    public const string Name = @"editor\GuerrillaBuilder.exe";

    const string Usage = """
        GuerrillaBuilder.exe                              opens the builder's window
        GuerrillaBuilder.exe build [<parts>] [<options>]  builds the parts (all of them if none are named)
        GuerrillaBuilder.exe remove [<parts>]             takes them away again (all of them if none are named),
                                                          with the parts built on them
        GuerrillaBuilder.exe status                       says what is built
        GuerrillaBuilder.exe check                        checks the tables built, as the game's reader takes
                                                          them at start-up, and the world's rocks' collision,
                                                          as its physics will (a build ends with this too)
          <parts>: world hud vehicles weapons people cloth, in any order; they're built in that order, with
          the ones they need that aren't built yet.
        Options for build:
          --guerrilla <folder>          Guerrilla's folder for this run (else GuerrillaDir in rfa_mods.ini)
          --zones all | <cols>,<rows>   the world's zones: all (the default) or a block, such as 11-12,10-11
          --props all | none | <files>  the world's destructible objects: all (the default), none, or those
                                        made from the chunk files named, such as con_cardboard_a,con_crate02_a
          --props-in <cols>,<rows>      places them only in a block of zones
          --vehicles all | <names>      Guerrilla's vehicles.xtbl entries, such as Col_Taxi_1 (default all)
          --weapons <names>             Guerrilla's weapons.xtbl entries, such as m16 (default: those ported)
          --characters <names>          Guerrilla's character.xtbl entries, such as EDF_Trooper (default: those ported)
          Giving --zones, --props, --props-in, --vehicles, --weapons or --characters without naming parts
          builds only the parts they belong to.
        """;

    public static int Run(string[] args)
    {
        UseConsole();
        try
        {
            return Command(args);
        }
        finally
        {
            Console.Out.Flush();
            if (ownConsole)
            {
                Console.WriteLine();
                Console.WriteLine("Press Enter to close this window.");
                Console.ReadLine();
            }
        }
    }

    static int Command(string[] args)
    {
        string command = args[0].ToLowerInvariant();
        if (command is "help" or "--help" or "-h" or "/?" or "-?")
        {
            Console.WriteLine(Usage);
            return 0;
        }
        if (command == "check")
        {
            var checkSetup = Setup.Find();
            using var checkLog = new BuildLog(checkSetup.LogFile, Console.WriteLine);
            return TableCheck.Run(checkSetup, checkLog) ? 0 : 1;
        }
        if (command is not ("build" or "remove" or "status"))
            return Fail($"Unknown command {args[0]}.\n\n{Usage}");

        var setup = Setup.Find();
        var parts = new List<Part>();
        var implied = new SortedSet<Part>();
        var options = new Options();
        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];
            string? Value() => i + 1 < args.Length ? args[++i] : null;
            List<string> Names(string value) => [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            if (!arg.StartsWith("--"))
            {
                foreach (string word in Names(arg))
                {
                    var part = Parts.All.Where(p => Parts.Word(p).Equals(word, StringComparison.OrdinalIgnoreCase)).Cast<Part?>().FirstOrDefault();
                    if (part == null) return Fail($"Unknown part {word}: the parts are world, hud, vehicles, weapons, people and cloth.");
                    parts.Add(part.Value);
                }
                continue;
            }
            if (command != "build") return Fail($"{arg} is an option for build only.");
            string? value = Value();
            if (value == null) return Fail($"{arg} needs a value.\n\n{Usage}");
            switch (arg.ToLowerInvariant())
            {
                case "--guerrilla": setup.UseGuerrilla(value); break;
                case "--zones": options.Zones = value; implied.Add(Part.World); break;
                case "--props":
                    options.Props = [.. Names(value).Where(p => !p.Equals("none", StringComparison.OrdinalIgnoreCase))];
                    if (options.Props.Any(p => p.Equals("all", StringComparison.OrdinalIgnoreCase))) options.Props = null;
                    implied.Add(Part.World);
                    break;
                case "--props-in": options.PropsIn = value; implied.Add(Part.World); break;
                case "--vehicles": options.Vehicles = Names(value); implied.Add(Part.Vehicles); break;
                case "--weapons": options.Weapons = Names(value); implied.Add(Part.Weapons); break;
                case "--characters": options.Characters = Names(value); implied.Add(Part.People); break;
                default: return Fail($"Unknown option {arg}.\n\n{Usage}");
            }
        }
        IReadOnlyCollection<Part> chosen = parts.Count > 0 ? parts : implied.Count > 0 ? implied : Parts.All;
        if (setup.Guerrilla == null && setup.FindGuerrilla() is { } found)
        {
            setup.UseGuerrilla(found);
            Console.WriteLine($"GuerrillaDir isn't set in rfa_mods.ini: using the copy of Guerrilla in your Steam library, {found}");
        }

        foreach (string line in Parts.TidyOldTools()) Console.WriteLine(line);
        if (command == "status")
        {
            Console.WriteLine($"Guerrilla:  {setup.Guerrilla ?? "(not set)"}{(setup.GuerrillaProblem is { } g && setup.Guerrilla != null ? $": {g}" : "")}");
            Console.WriteLine($"Armageddon: {setup.Game ?? "(not found)"}");
            foreach (var part in chosen)
                Console.WriteLine($"{Parts.Name(part),-9} {Parts.Status(setup, part).Text}");
            return 0;
        }
        using var log = new BuildLog(setup.LogFile, Console.WriteLine);
        bool ok = command == "build" ? Parts.Build(setup, chosen, options, log) : Parts.Remove(setup, chosen, log);
        return ok ? 0 : 1;
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    // ---- The console. A window program starts with none: its output goes nowhere unless it was
    // started with its output sent somewhere (a pipe or a file), so it joins the console of the
    // command prompt it was started from, or, started from a shortcut, opens one of its own.

    static bool ownConsole;

    static void UseConsole()
    {
        if (IsUsable(GetStdHandle(StdOutput)))
        {
            // Sent to a pipe or a file: as it comes, in UTF-8.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(IsUsable(GetStdHandle(StdError))
                ? new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true }
                : Console.Out);
            return;
        }
        if (!AttachConsole(ParentProcess))
        {
            AllocConsole();
            ownConsole = true;
        }
        // The console's own text, in its code page (characters it hasn't come out as ?).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding;
        try { encoding = Encoding.GetEncoding((int)GetConsoleOutputCP(), new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?")); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { encoding = Encoding.ASCII; }
        var handle = CreateFileW("CONOUT$", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid) return;
        var writer = new StreamWriter(new FileStream(handle, FileAccess.Write), encoding) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
        if (!ownConsole) Console.WriteLine();  // off the line the prompt is on
        if (ownConsole)
        {
            var input = CreateFileW("CONIN$", GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (!input.IsInvalid) Console.SetIn(new StreamReader(new FileStream(input, FileAccess.Read), encoding));
        }
    }

    static bool IsUsable(IntPtr handle) => handle != IntPtr.Zero && handle != new IntPtr(-1);

    const int StdOutput = -11, StdError = -12, ParentProcess = -1;
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareRead = 1, FileShareWrite = 2, OpenExisting = 3;

    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("kernel32.dll")] static extern uint GetConsoleOutputCP();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
}
