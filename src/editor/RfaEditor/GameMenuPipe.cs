using System.IO;
using System.IO.Pipes;
using System.Text;

namespace RfaEditor;

/// <summary>
/// The game's main menu (rfa_loader's rfa_open_level_editor) reaches an editor that is already open through the pipe
/// \\.\pipe\RFAModEditor instead of starting another: it sends one line, the arguments it would have started the
/// editor with (--dock, --new-level ..., see <see cref="MainWindow.MenuRequestFrom"/>), and the editor answers with
/// its process id (the menu watches it, as it watches an editor it started). Only this user's processes can connect.
/// The first editor open has the pipe; one started while it runs leaves it to that one.
/// </summary>
static class GameMenuPipe
{
    public const string Name = "RFAModEditor";

    public static void Start(MainWindow main)
    {
        var thread = new Thread(() => Serve(main)) { IsBackground = true, Name = "Game menu pipe" };
        thread.Start();
    }

    static void Serve(MainWindow main)
    {
        while (true)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                EditorLog.Write("Another editor is open: the game's menu asks that one for the level editor.");
                return;
            }
            using (pipe)
            {
                try
                {
                    pipe.WaitForConnection();
                    var reader = new StreamReader(pipe, Encoding.ASCII, false, 1024, leaveOpen: true);
                    string? line = reader.ReadLine();
                    var writer = new StreamWriter(pipe, Encoding.ASCII, 64, leaveOpen: true) { AutoFlush = true };
                    writer.Write(Environment.ProcessId + "\n");
                    pipe.WaitForPipeDrain();  // (read before the pipe goes)
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[] args = SplitArguments(line);
                        EditorLog.Write("The game's menu asked this editor: " + line);
                        main.Dispatcher.BeginInvoke(new Action(() => main.FromGameMenu(args)));
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    EditorLog.Write("The game's menu pipe: " + ex.Message);
                }
            }
        }
    }

    /// <summary>A command line's arguments: split at spaces, a "quoted" one kept whole (the menu keeps " and \ out of names).</summary>
    internal static string[] SplitArguments(string line)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (c == ' ' && !quoted)
            {
                if (any) args.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }
        if (any) args.Add(current.ToString());
        return args.ToArray();
    }
}
