// GuerrillaBuilder.exe: makes Red Faction: Guerrilla's world, HUD, vehicles, weapons and people
// usable in Armageddon, for the guerrilla_* mods, from your own copy of Guerrilla (which is only
// read). What it makes stays on your machine, in the mod folder. Started on its own it opens a
// window; given a command (build, remove, status: see CommandLine) it runs that and ends.
// The converting itself is RfaFormats': GuerrillaWorld, GuerrillaHud, GuerrillaArsenal,
// GuerrillaWeapons and GuerrillaPeople; Parts runs them in order.
using System.Windows;

namespace GuerrillaBuilder;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0) return CommandLine.Run(args);
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        RfaEditor.FluentLook.Load(app, new Uri("pack://application:,,,/GuerrillaBuilder;component/Theme.xaml"));
        return app.Run(new MainWindow());
    }
}
