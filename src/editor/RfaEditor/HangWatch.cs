using System.Diagnostics;
using System.Windows.Threading;

namespace RfaEditor;

/// <summary>
/// Notes in logs\editor.log when the window stops answering (its thread busy or stuck for
/// <see cref="Limit"/> or more), with what the editor last said it was doing, and when it answers
/// again; so a hang can be looked into afterwards, as a crash can (see <see cref="EditorLog"/>).
/// A thread of its own asks the window's thread to answer twice a second, after any input waiting
/// for it, and writes to the file itself, as the window can't. A long task (opening a big zone,
/// saving) can be noted too: the note then says what it was, and how long it took.
///
/// The places that may wait on something outside the editor, or take long, say what they do:
/// <c>using (HangWatch.While("drawing ...")) { ... }</c>.
/// </summary>
static class HangWatch
{
    static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    static long answeredAt = Environment.TickCount64;  // when the window's thread last answered
    static int asking;                                  // 1 while a question is waiting for it
    static volatile string doing = "";

    /// <summary>Says what the editor is doing until the result is disposed (then what it said before again).</summary>
    public static Note While(string what)
    {
        var note = new Note(doing);
        doing = what;
        return note;
    }

    /// <summary>What was said before; disposing it says that again.</summary>
    public readonly struct Note(string before) : IDisposable
    {
        public void Dispose() => doing = before;
    }

    /// <summary>Starts watching the window's thread.</summary>
    public static void Start(Dispatcher window) =>
        new Thread(() => Watch(window)) { IsBackground = true, Name = "Hang watch", Priority = ThreadPriority.BelowNormal }.Start();

    static void Watch(Dispatcher window)
    {
        long stuckSince = 0;  // when it stopped answering, if it has (and that was written down)
        while (!window.HasShutdownStarted)
        {
            Thread.Sleep(500);
            if (Interlocked.CompareExchange(ref asking, 1, 0) == 0)
                window.BeginInvoke(DispatcherPriority.Input, () =>
                {
                    Interlocked.Exchange(ref answeredAt, Environment.TickCount64);
                    Interlocked.Exchange(ref asking, 0);
                });
            long last = Interlocked.Read(ref answeredAt), now = Environment.TickCount64;
            if (stuckSince == 0 && now - last >= Limit.TotalMilliseconds)
            {
                stuckSince = last;
                string what = doing;
                using var me = Process.GetCurrentProcess();
                EditorLog.Write($"{DateTime.Now:HH:mm:ss} The window hasn't answered for {Limit.TotalSeconds:0} s. " +
                                (what.Length > 0 ? $"The editor was {what}." : "The editor wasn't in any of the places that say what they do (so: WPF's own drawing or layout, the docking, or a click's work).") +
                                $" Memory: {me.WorkingSet64 / (1 << 20):N0} MB in use.");
            }
            else if (stuckSince != 0 && last > stuckSince)
            {
                EditorLog.Write($"{DateTime.Now:HH:mm:ss} The window answers again, after {(last - stuckSince) / 1000.0:0.0} s.");
                stuckSince = 0;
            }
        }
    }
}
