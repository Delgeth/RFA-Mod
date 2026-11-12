using System.Windows;
using RfaFormats.Zones;

namespace RfaEditor;

// Copy and paste an object's properties (Ctrl+Shift+C, Ctrl+Shift+V, or the inspector's buttons): the
// picked object's properties, all but where it is (its position and turn: "oP", a navpoint's
// "just_pos"), kept in the editor (any level's tab can paste them), and given to another object. To an
// object of the same class every one is given (those it lacks added); to one of another class only
// those it has too, of the same type. One step to undo.
public partial class LevelEditor
{
    sealed record CopiedProperties(string Class, uint ClassHash, string From, IReadOnlyList<(int Type, uint Hash, byte[] Value)> Properties);

    static CopiedProperties? copiedProperties;  // (shared by every level's tab)

    static readonly uint[] PlacementProperties = [NameHashes.PropertyHash("oP"), NameHashes.PropertyHash("just_pos")];

    void OnCopyPropertiesClicked(object sender, RoutedEventArgs e) => CopyProperties();

    void OnPastePropertiesClicked(object sender, RoutedEventArgs e) => PasteProperties();

    void CopyProperties()
    {
        if (selected == null) return;
        var properties = selected.Properties.Where(p => !PlacementProperties.Contains(p.NameHash)).Select(p => (p.Type, p.NameHash, p.Value)).ToList();
        copiedProperties = new CopiedProperties(names.Class(selected.ClassHash), selected.ClassHash, Label(selected), properties);
        context.Log($"Copied {properties.Count} properties of {Label(selected)} ({copiedProperties.Class}): pick another object and paste them (Ctrl+Shift+V).");
        UpdatePasteButton();
    }

    void PasteProperties()
    {
        if (copiedProperties is not { } copied || selected == null) return;
        if (!Editable(selected))
        {
            context.Log("Paste properties: the picked object is another file's; open that file to change it.");
            return;
        }
        bool sameClass = selected.ClassHash == copied.ClassHash;
        int set = 0, added = 0, same = 0;
        var skipped = new List<string>();
        try
        {
            foreach (var (type, hash, value) in copied.Properties)
            {
                // (Read afresh each time: a value of another size lays the object out again.)
                if (selected.Property(hash) is { } have)
                {
                    if (have.Type != type) { skipped.Add(names.Property(hash)); continue; }
                    if (have.Value.AsSpan().SequenceEqual(value)) { same++; continue; }
                    have.SetValue((byte[])value.Clone());
                    set++;
                }
                else if (sameClass)
                {
                    selected.AddProperty(type, hash, (byte[])value.Clone());
                    added++;
                }
                else skipped.Add(names.Property(hash));
            }
        }
        catch (InvalidOperationException ex)
        {
            context.Log($"Paste properties: stopped ({ex.Message}).");
        }
        if (set + added > 0)
        {
            OnPropertyChanged();
            CommitEdit("Paste properties");
            ShowPropertyRows();
        }
        context.Log($"Pasted {copied.From}'s properties onto {Label(selected)}: {set} changed, {added} added, {same} already the same" +
                    (skipped.Count > 0 ? $"; {skipped.Count} left out ({(sameClass ? "of another type" : $"it's a {names.Class(selected.ClassHash)}, not a {copied.Class}")}: " +
                                         string.Join(", ", skipped.Take(12)) + (skipped.Count > 12 ? "..." : "") + ")" : "") + ".");
    }

    void UpdatePasteButton()
    {
        CopyPropertiesButton.IsEnabled = selected != null;
        PastePropertiesButton.IsEnabled = selected != null && copiedProperties != null && Editable(selected);
        PastePropertiesButton.ToolTip = copiedProperties is { } c
            ? $"Paste properties: give the picked object {c.From}'s properties ({c.Class}; all of them to another {c.Class}, else those it has too) (Ctrl+Shift+V)"
            : "Paste properties: copy an object's first (Ctrl+Shift+C)";
    }
}
