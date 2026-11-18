using System.IO;
using System.Windows;
using Microsoft.Win32;
using RfaFormats;

namespace RfaEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Started by the game's main menu (rfa_loader's rfa_delete_custom_level) to delete a level made with New level,
        // with no window: RfaEditor.exe --delete-level <name>. Its exit code says how it went.
        int delete = Array.FindIndex(e.Args, a => a.Equals("--delete-level", StringComparison.OrdinalIgnoreCase));
        if (delete >= 0)
        {
            Shutdown(DeleteLevel(delete + 1 < e.Args.Length ? e.Args[delete + 1] : ""));
            return;
        }
        FluentLook.Load(this, new Uri("pack://application:,,,/RfaEditor;component/Theme.xaml"));
        // Nothing here is tied to one PC: the mod folder is wherever the editor was put (next to
        // rfa_mods.ini), and the game is wherever its GameDir says, or that folder itself.
        string modFolder = Workspace.FindModFolder();
        EditorLog.Start(modFolder);
        DispatcherUnhandledException += (_, args) =>
        {
            EditorLog.Crash(args.Exception);
            MessageBox.Show($"The editor ran into a problem it can't recover from and has to close:\n\n{args.Exception.Message}\n\n" +
                            $"What happened is written in {EditorLog.FilePath ?? "the log"}.",
                            "RFA Editor", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception error) EditorLog.Crash(error); };
        HangWatch.Start(Dispatcher);  // and if the window stops answering, that is written down too
        string? gameFolder = Workspace.FindGameFolder(modFolder) ?? AskForGameFolder(modFolder);
        if (gameFolder == null)
        {
            MessageBox.Show($"The editor needs the game folder (the one with {GameFolder.ExeName}). " +
                            "Set GameDir in rfa_mods.ini, or choose the folder when the editor starts.",
                            "RFA Editor", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        // --new-level and the Create level page's choices (see MainWindow.MenuRequestFrom).
        var (asked, request) = MainWindow.MenuRequestFrom(e.Args);
        var main = new MainWindow(new Workspace(gameFolder, modFolder))
        {
            NewLevelAtStart = asked,
            NewLevelMadeAtStart = request,
        };
        main.Show();
        // The game's main menu reaches this editor through a pipe from now on, instead of starting another (GameMenuPipe).
        GameMenuPipe.Start(main);
        // --dock (the game's main menu starts the editor with it): the game that started it is docked in the
        // Game panel, which is brought forward for it (it docks itself as soon as the panel shows).
        if (e.Args.Any(a => a.Equals("--dock", StringComparison.OrdinalIgnoreCase)))
            main.Dispatcher.BeginInvoke(new Action(main.ShowGameToDock), System.Windows.Threading.DispatcherPriority.Loaded);
        if (main.NewLevelAtStart != null) EditorLog.Write("New level opens once the editor has read the game's files (a minute or so).");
        if (request != null) EditorLog.Write($"New level {request.Name} ({request.Mode}) is made once the editor has read the game's files (a minute or so).");
    }

    /// <summary>
    /// Deletes a level New level made (all of it, as Delete level does), with no window. Exit code: 0 deleted,
    /// 1 no such level of ours, 2 the game folder wasn't found, 3 it couldn't be deleted. What was done goes to the editor's log.
    /// </summary>
    static int DeleteLevel(string name)
    {
        string modFolder = Workspace.FindModFolder();
        EditorLog.Start(modFolder);
        string? gameFolder = Workspace.FindGameFolder(modFolder);
        if (gameFolder == null)
        {
            EditorLog.Write($"Delete level {name} (from the game's menu): the game folder wasn't found (GameDir in rfa_mods.ini).");
            return 2;
        }
        var workspace = new Workspace(gameFolder, modFolder);
        if (name.Length == 0 || !NewLevel.Made(workspace).Any(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            EditorLog.Write($"Delete level {name} (from the game's menu): it isn't a level made with New level.");
            return 1;
        }
        try
        {
            EditorLog.Write($"Deleted level {name} (from the game's menu):");
            foreach (string line in new NewLevel(workspace, new GameAssets(workspace)).Remove(name)) EditorLog.Write("  " + line);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            EditorLog.Write($"Delete level {name} (from the game's menu): it couldn't be deleted: {ex.Message}");
            return 3;
        }
    }

    /// <summary>
    /// Asks where the game is (any copy: Steam, GOG, ...), and keeps the answer as GameDir in
    /// rfa_mods.ini, so neither the editor nor the launcher has to ask again.
    /// </summary>
    static string? AskForGameFolder(string modFolder)
    {
        var dialog = new OpenFolderDialog { Title = $"Where is Red Faction: Armageddon? Choose the folder with {GameFolder.ExeName}" };
        if (dialog.ShowDialog() != true || !GameFolder.IsGameFolder(dialog.FolderName)) return null;
        string ini = Path.Combine(modFolder, "rfa_mods.ini");
        if (File.Exists(ini)) IniFile.Write(ini, "Launcher", "GameDir", dialog.FolderName);
        return dialog.FolderName;
    }
}
