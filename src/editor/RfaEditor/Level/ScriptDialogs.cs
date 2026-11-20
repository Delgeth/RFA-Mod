using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RfaFormats.Zones;

namespace RfaEditor;

/// <summary>
/// Chooses one of the game's script functions: a search box over their names, categories and comments,
/// the list grouped by category, and the chosen one's description and parameters.
/// </summary>
static class FunctionPicker
{
    public static LogicFunctions.Function? Choose(Window owner, LogicFunctions functions, bool tests, LogicFunctions.Function? current)
    {
        var all = functions.All.Where(f => f.IsTest == tests)
                           .OrderBy(f => Category(f), StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var window = new Window
        {
            Title = tests ? "Choose a test" : "Choose a function", Owner = owner, Width = 760, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Brush("WindowBrush"), Foreground = Theme.Brush("TextBrush"),
        };
        var search = new TextBox { Margin = new Thickness(8, 8, 8, 4), ToolTip = "Type words to find: in names, categories and descriptions" };
        var list = new ListBox { Margin = new Thickness(8, 0, 4, 8), Background = Theme.Brush("InputBrush"), Foreground = Theme.Brush("TextBrush") };
        var about = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 8, 8) };
        LogicFunctions.Function? chosen = null;

        void Fill()
        {
            string[] words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Items.Clear();
            string? category = null;
            ListBoxItem? select = null;
            foreach (LogicFunctions.Function f in all)
            {
                string haystack = $"{f.Name} {f.Code} {f.Category} {f.Comments}";
                if (!words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
                if (Category(f) != category)
                {
                    category = Category(f);
                    list.Items.Add(new ListBoxItem
                    {
                        Content = category, FontWeight = FontWeights.Bold, IsEnabled = false, Foreground = Theme.Brush("DimTextBrush"),
                        Margin = new Thickness(0, 6, 0, 0),
                    });
                }
                var item = new ListBoxItem { Content = "    " + f.Name, Tag = f, ToolTip = f.Code };
                item.MouseDoubleClick += (_, _) => { chosen = f; window.DialogResult = true; };
                list.Items.Add(item);
                if (f == current || select == null && words.Length > 0) select ??= item;
                if (f == current) select = item;
            }
            if (select != null) { list.SelectedItem = select; list.ScrollIntoView(select); }
        }
        list.SelectionChanged += (_, _) =>
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is not LogicFunctions.Function f) { about.Text = ""; return; }
            about.Text = $"{f.Name}\n({f.Code})\n\n{(f.Comments.Length > 0 ? f.Comments : "No description in the table.")}\n\n" +
                         (f.Parameters.Count == 0 ? "No parameters." :
                          "Parameters:\n" + string.Join("\n", f.Parameters.Select(p => $"  {p.Name} ({p.Type})" + (p.Description.Length > 0 ? $": {p.Description}" : ""))));
        };
        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && list.Items.Count > 0) { list.Focus(); e.Handled = true; }
        };

        var ok = new Button { Content = "Choose", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            if ((list.SelectedItem as ListBoxItem)?.Tag is LogicFunctions.Function f) { chosen = f; window.DialogResult = true; }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumnSpan(search, 2);
        grid.Children.Add(search);
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var scroller = new ScrollViewer { Content = about, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 1);
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        Grid.SetRow(buttons, 2);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        window.Content = grid;

        Fill();
        window.Loaded += (_, _) => search.Focus();
        return window.ShowDialog() == true ? chosen : null;
    }

    /// <summary>A function's category: the table's (Volition's editor's), else the words before " - " in its name.</summary>
    static string Category(LogicFunctions.Function f)
    {
        if (f.Category.Length > 0) return f.Category;
        int dash = f.Name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? char.ToUpperInvariant(f.Name[0]) + f.Name[1..dash] : "Other";
    }
}

/// <summary>Chooses a recipe (<see cref="LogicRecipe"/>), fills in its blanks and adds its state machine to a script.</summary>
static class RecipeDialog
{
    public static LogicObject? Choose(ScriptEditor owner, LogicScript script, ScriptContext context)
    {
        var window = new Window
        {
            Title = "Add from a recipe", Owner = owner, Width = 720, Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Brush("WindowBrush"), Foreground = Theme.Brush("TextBrush"),
        };
        var list = new ListBox
        {
            Margin = new Thickness(8), Background = Theme.Brush("InputBrush"), Foreground = Theme.Brush("TextBrush"),
            ItemsSource = LogicRecipe.All, DisplayMemberPath = nameof(LogicRecipe.Name),
        };
        var form = new StackPanel { Margin = new Thickness(4, 8, 8, 8) };
        var filled = new LogicRecipe.Filled();

        string ClassOf(uint handle) => context.Objects.FirstOrDefault(o => o.Handle == handle)?.Class ?? "";

        void ShowRecipe()
        {
            form.Children.Clear();
            filled.Objects.Clear();
            filled.Texts.Clear();
            filled.Numbers.Clear();
            if (list.SelectedItem is not LogicRecipe recipe) return;
            form.Children.Add(new TextBlock { Text = recipe.Name, FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            form.Children.Add(new TextBlock
            {
                Text = recipe.Description, Foreground = Theme.Brush("DimTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
            });
            foreach (LogicRecipe.Blank blank in recipe.Blanks)
            {
                FrameworkElement editor;
                switch (blank.Kind)
                {
                    case LogicRecipe.BlankKind.Object:
                        // Starts as the handler's own object or the level's picked one, when it is of a class that fits.
                        uint start = 0xFFFFFFFF;
                        if (blank.Classes.Contains(ClassOf(context.Self))) start = context.Self;
                        else if (context.Picked() is uint picked && blank.Classes.Contains(ClassOf(picked))) start = picked;
                        filled.Objects[blank.Key] = start;
                        editor = owner.ObjectPicker(start, blank.Classes, h => filled.Objects[blank.Key] = h);
                        break;
                    case LogicRecipe.BlankKind.Number:
                        filled.Numbers[blank.Key] = float.TryParse(blank.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : 0;
                        var number = new TextBox { Text = blank.Default };
                        number.TextChanged += (_, _) =>
                        {
                            bool good = float.TryParse(number.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v);
                            if (good) { filled.Numbers[blank.Key] = v; number.ClearValue(Control.BorderBrushProperty); }
                            else number.BorderBrush = Theme.Brush("ErrorBrush");
                        };
                        editor = number;
                        break;
                    default:
                        filled.Texts[blank.Key] = blank.Default;
                        var text = new TextBox { Text = blank.Default, MaxLength = 31 };
                        text.TextChanged += (_, _) => filled.Texts[blank.Key] = text.Text;
                        editor = text;
                        break;
                }
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = blank.Tip.Length > 0 ? blank.Tip : null };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = blank.Label, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(editor, 1);
                row.Children.Add(editor);
                form.Children.Add(row);
            }
        }
        list.SelectionChanged += (_, _) => ShowRecipe();

        LogicObject? made = null;
        var ok = new Button { Content = "Add", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is not LogicRecipe recipe) return;
            if (recipe.Blanks.FirstOrDefault(b => b.Kind == LogicRecipe.BlankKind.Object && filled.Objects.GetValueOrDefault(b.Key, 0xFFFFFFFF) == 0xFFFFFFFF) is { } empty &&
                MessageBox.Show(window, $"{empty.Label} isn't chosen. Add the recipe anyway (and choose it later in its calls)?", window.Title,
                                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            made = recipe.AddTo(script, filled);
            window.DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.Children.Add(list);
        var scroller = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        window.Content = grid;
        list.SelectedIndex = 0;

        return window.ShowDialog() == true ? made : null;
    }
}
