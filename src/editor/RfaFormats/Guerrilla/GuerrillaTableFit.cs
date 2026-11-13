using System.Globalization;
using RfaFormats.Tables;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Fits one of Guerrilla's table entries (or one made from it) to the shape Armageddon's table describes,
/// as Armageddon's table reader needs it: what Armageddon's description hasn't is left out, numbers it reads
/// as whole are rounded, and what it requires is added with its default. The same rules as the vehicles'
/// (<see cref="GuerrillaArsenal"/>), shared here for the other Guerrilla tables.
/// </summary>
static class GuerrillaTableFit
{
    /// <summary>
    /// Leaves out of an entry (or a group in it) every field Armageddon's description hasn't, and every
    /// Selection value it has no choice for; <paramref name="dropped"/> names them. A field named in
    /// <paramref name="keep"/> stays at the top level even so: some descriptions are older than the
    /// game's tables (aim_drift.xtbl's has no MinTime or MaxTime, which all of Armageddon's entries
    /// carry), so what all of Armageddon's entries have (<see cref="CommonFields"/>) is kept.
    /// </summary>
    public static void Prune(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> dropped, IReadOnlySet<string>? keep = null)
    {
        foreach (var child in element.Elements.ToList())
        {
            if (child.Name.Equals("_Editor", StringComparison.OrdinalIgnoreCase)) continue;
            var field = fields.FirstOrDefault(f => f.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
            if (field == null && path.Length == 0 && keep != null && keep.Contains(child.Name)) continue;
            if (field == null) { child.Remove(); dropped.Add(path + child.Name); continue; }
            switch (field.Type)
            {
                case FieldType.Element:
                    Prune(child, field.Children, path + child.Name + "/", dropped);
                    break;
                case FieldType.List or FieldType.Grid when field.Item is { Type: FieldType.Element } item:
                    foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)).ToList())
                        Prune(one, item.Children, path + child.Name + "/", dropped);
                    break;
                case FieldType.Selection when field.Choices.Count > 0 && !child.HasElements &&
                                              !field.Choices.Contains(child.Value.Trim(), StringComparer.OrdinalIgnoreCase):
                    dropped.Add($"{path}{child.Name} {child.Value.Trim()}");
                    child.Remove();
                    break;
                case FieldType.Flags when field.Flags.Count > 0:
                    foreach (var flag in child.Elements.ToList())
                        if (!field.Flags.Contains(flag.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                        {
                            dropped.Add($"{path}{child.Name} {flag.Value.Trim()}");
                            flag.Remove();
                        }
                    break;
            }
        }
    }

    /// <summary>
    /// Writes an entry's numbers as Armageddon reads them: a decimal in a field Armageddon's description makes
    /// a whole number (which would stop its table reader: "Could not parse '.25' into an integer") is
    /// rounded, and a number outside the description's range (MinValue, MaxValue: Guerrilla's NPC refire
    /// delay of 0 where Armageddon's least is 1) is brought to its nearest end; each is named in
    /// <paramref name="rounded"/>. Inside the groups, the lists' items and a combo's chosen group too.
    /// </summary>
    public static void FitValues(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> rounded)
    {
        foreach (var child in element.Elements)
        {
            var field = fields.FirstOrDefault(f => f.Name.Equals(child.Name, StringComparison.OrdinalIgnoreCase));
            if (field == null) continue;
            string at = path + field.Name;
            if (field.Type == FieldType.Element) { FitValues(child, field.Children, at + "/", rounded); continue; }
            if (field.Type is FieldType.List or FieldType.Grid && field.Item is { Type: FieldType.Element } item)
            {
                foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)))
                    FitValues(one, item.Children, at + "/", rounded);
                continue;
            }
            if (field.Type == FieldType.ComboElement)
            {
                foreach (var (chosen, choice) in Chosen(child, field)) FitValues(chosen, choice.Children, at + "/" + choice.Name + "/", rounded);
                continue;
            }
            if (child.HasElements || field.Type is not (FieldType.Int or FieldType.Float)) continue;
            if (!double.TryParse(child.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;
            double fitted = field.Type == FieldType.Int ? Math.Round(value) : value;
            if (field.Min is { } min && fitted < min) fitted = min;
            if (field.Max is { } max && fitted > max) fitted = max;
            if (fitted == value && (field.Type != FieldType.Int || value == Math.Floor(value))) continue;
            rounded.Add($"{at} {child.Value.Trim()}");
            child.Value = fitted.ToString(field.Type == FieldType.Int ? "0" : "0.0###", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A combo's chosen groups (the elements in it named as one of its choices) with their descriptions.</summary>
    static IEnumerable<(XtblElement Chosen, FieldDescription Choice)> Chosen(XtblElement combo, FieldDescription field)
    {
        foreach (var chosen in combo.Elements)
            if (field.Children.FirstOrDefault(c => c.Name.Equals(chosen.Name, StringComparison.OrdinalIgnoreCase)) is { Type: FieldType.Element } choice)
                yield return (chosen, choice);
    }

    /// <summary>
    /// Adds to an entry (or a group in it, or each item of a list in it) every field Armageddon's description
    /// marks as required that it hasn't, with the description's default: the game's table reader stops on a
    /// missing one. A missing group is added with its own required fields. <paramref name="unfilled"/> names
    /// the required fields with no default.
    /// </summary>
    public static void FillRequired(XtblElement element, IReadOnlyList<FieldDescription> fields, string path, List<string> added, List<string> unfilled)
    {
        foreach (var field in fields)
        {
            var child = element.Element(field.Name);
            if (child == null && field.Required)
            {
                if (field.Type == FieldType.Element) child = element.AddElement(field.Name);
                else if (field.Default != null) element.AddElement(field.Name, field.Default);
                else if (field.Type == FieldType.Flags) element.AddElement(field.Name);
                else { unfilled.Add(path + field.Name); continue; }
                added.Add(path + field.Name);
            }
            if (child == null) continue;
            if (field.Type == FieldType.Element) FillRequired(child, field.Children, path + field.Name + "/", added, unfilled);
            else if (field.Type is FieldType.List or FieldType.Grid && field.Item is { Type: FieldType.Element } item)
                foreach (var one in child.Elements.Where(e => e.Name.Equals(field.ItemElementName, StringComparison.OrdinalIgnoreCase)))
                    FillRequired(one, item.Children, path + field.Name + "/", added, unfilled);
            else if (field.Type == FieldType.ComboElement)  // the chosen group's own (the arc welder's Electric_Beam: Beam_Spline_Info)
                foreach (var (chosen, choice) in Chosen(child, field))
                    FillRequired(chosen, choice.Children, path + field.Name + "/" + choice.Name + "/", added, unfilled);
        }
    }

    /// <summary>The top-level fields every entry of a table has (case aside).</summary>
    public static IReadOnlySet<string> CommonFields(XtblFile table)
    {
        HashSet<string>? common = null;
        foreach (var entry in table.Entries)
        {
            var names = entry.Elements.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (common == null) common = names;
            else common.IntersectWith(names);
        }
        return common ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The description of a field by its path (names joined by '/'), or null.</summary>
    public static FieldDescription? Field(FieldDescription description, string path)
    {
        FieldDescription? at = description;
        foreach (string part in path.Split('/'))
        {
            if (at == null) return null;
            var children = at.Type is FieldType.List or FieldType.Grid && at.Item is { } item && !item.Name.Equals(part, StringComparison.OrdinalIgnoreCase)
                ? item.Children : at.Children;
            at = children.FirstOrDefault(f => f.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
        }
        return at;
    }

    /// <summary>Sets a field of an entry, adding it if it isn't there.</summary>
    public static XtblElement Set(XtblElement entry, string field, string value)
    {
        if (entry.Element(field) is { } existing) { existing.Value = value; return existing; }
        return entry.AddElement(field, value);
    }
}
