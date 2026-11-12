using System.IO;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;

namespace RfaEditor;

/// <summary>
/// Remembers where a workspace's panels are: which side each is docked to, which are tabs of one
/// another, which float or are pinned, and how big they are. It is kept in a file beside the program
/// (layout\&lt;workspace&gt;.xml), written when the editor closes and read when it starts. Which
/// documents were open isn't remembered. A file that can't be read (one from another version, say)
/// is ignored: the panels start where the workspace's XAML puts them.
/// </summary>
sealed class DockLayout
{
    readonly DockingManager manager;
    readonly Func<string, object?> viewOf;  // a panel's view, by the panel's id
    readonly string file;
    readonly string asDesigned;             // the layout the XAML gives, to go back to

    public DockLayout(DockingManager manager, string workspace, Func<string, object?> viewOf)
    {
        this.manager = manager;
        this.viewOf = viewOf;
        file = Path.Combine(AppContext.BaseDirectory, "layout", workspace + ".xml");
        var text = new StringWriter();
        new XmlLayoutSerializer(manager).Serialize(text);
        asDesigned = text.ToString();
    }

    /// <summary>Puts the panels where they were when the editor last closed. Call it before anything is opened.</summary>
    public void Restore()
    {
        if (!File.Exists(file)) return;
        try
        {
            Read(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            // Whatever is wrong with the file, the editor has to start: back to the designed layout.
            EditorLog.Write($"The saved layout {Path.GetFileName(file)} couldn't be used ({ex.GetType().Name}: {ex.Message}); the panels are where they start.");
            Read(asDesigned);
        }
    }

    /// <summary>Writes down where the panels are now.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            new XmlLayoutSerializer(manager).Serialize(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EditorLog.Write($"The layout couldn't be saved to {file}: {ex.Message}");
        }
    }

    /// <summary>Puts every panel back where the workspace's XAML has it.</summary>
    public void Reset() => Read(asDesigned);

    void Read(string layout)
    {
        var serializer = new XmlLayoutSerializer(manager);
        serializer.LayoutSerializationCallback += (_, e) =>
        {
            // A panel gets its view back by its id. Anything else in the file is dropped: a document
            // that was open, or a panel this version no longer has.
            object? view = e.Model is LayoutAnchorable && e.Model.ContentId is { } id ? viewOf(id) : null;
            if (view == null) e.Cancel = true;
            else e.Content = view;
        };
        serializer.Deserialize(new StringReader(layout));
    }
}
