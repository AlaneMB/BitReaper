using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>One visible line of the tree: a node at some depth, expanded or not.</summary>
public sealed class TreeRow : INotifyPropertyChanged
{
    private readonly FileTree _tree;

    public FileNode Node { get; }
    public int Depth { get; }
    public bool IsExpanded { get; set; }

    public TreeRow(FileTree tree, FileNode node, int depth)
    {
        _tree = tree;
        Node = node;
        Depth = depth;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Refreshes every bound value (collector, units or tree changes).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private ScanSession Session => _tree.Session;
    private bool Collected => Session.Collector.Contains(Node);
    private bool Covered => !Collected && Session.Collector.CoveringAncestor(Node) != null;
    private Color ItemColor => Palette.ColorFor(Session.ColorInfo(Node)?.Slot, Session.ColorInfo(Node)?.Depth ?? 0);

    // Name column
    public Thickness Indent => new(Depth * 16, 0, 0, 0);
    public bool CanExpand => !_tree.IsFlat && Node.IsDirectory && Node.HasChildren;
    public Visibility ExpanderVisibility => CanExpand ? Visibility.Visible : Visibility.Hidden;
    public string ExpanderGlyph => IsExpanded ? Ui.GlyphChevronDown : Ui.GlyphChevronRight;
    public Brush DotBrush => Theme.Brush(Collected || Covered ? ItemColor.WithAlpha(0.3) : ItemColor);
    public ImageSource Icon => IconCache.Icon(Node);
    public double IconOpacity => Collected || Covered ? 0.4 : 1;
    public string Name => Session.DisplayName(Node);
    public string Subtitle => _tree.IsFlat && Session.RelativePath(Node.Parent) is { Length: > 0 } relative ? "  " + relative : "";
    public Brush NameBrush => Theme.Brush(Collected || Covered ? Theme.TextTertiary : Theme.Text);
    public TextDecorationCollection? NameDecorations => Collected ? StrikeThrough : null;
    public Visibility LockVisibility => Node.IsUnreadable ? Visibility.Visible : Visibility.Collapsed;
    public string Tip => Node.Path;

    private static readonly TextDecorationCollection StrikeThrough = CreateStrike();

    private static TextDecorationCollection CreateStrike()
    {
        var decoration = new TextDecoration(TextDecorationLocation.Strikethrough, new Pen(Theme.Brush(Theme.Magenta), 1.2), 0,
                                            TextDecorationUnit.FontRecommended, TextDecorationUnit.FontRecommended);
        var collection = new TextDecorationCollection { decoration };
        collection.Freeze();
        return collection;
    }

    // Size, %, items, modified
    private long Reference => _tree.IsFlat ? Session.Focus.Size : (Node.Parent?.Size ?? Node.Size);
    public string SizeText => ByteFormat.Size(Node.Size);
    public Brush SizeBrush => Theme.Brush(Collected || Covered ? Theme.TextTertiary : Theme.Text);
    public double BarFraction => Reference > 0 ? (double)Node.Size / Reference : 0;
    public Color BarColor => ItemColor;
    public bool BarDimmed => Collected || Covered;
    public string PercentText => ByteFormat.Percent(Node.Size, Reference);
    public string ItemsText => Node.IsDirectory ? ByteFormat.Count(Node.ItemCount) : "";
    public string ModifiedText => Node.Modified > 0 ? ByteFormat.Date(Node.ModifiedLocal) : "";

    // Collect column
    public string CollectGlyph => Collected ? "" : Covered ? Ui.GlyphCheck : Ui.GlyphAdd;
    public Brush CollectBrush => Theme.Brush(Collected ? Theme.Magenta : Covered ? Theme.TextTertiary : Theme.TextSecondary);
    public bool CollectEnabled => !Covered;
    public string CollectTip => Collected ? "Remove from Collector (Del)" : Covered ? "Collected with its folder" : "Add to Collector (Del)";

    /// <summary>Screen readers and type-to-select read the item's name.</summary>
    public override string ToString() => Name;
}

/// <summary>A collection that can swap all its rows with a single Reset notification.</summary>
public sealed class RowCollection : ObservableCollection<TreeRow>
{
    public void ReplaceAll(IEnumerable<TreeRow> rows)
    {
        Items.Clear();
        foreach (var row in rows) Items.Add(row);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>
/// The file tree: a virtualized ListView with sortable, resizable columns. Folders expand in place;
/// keyboard: arrows, Right/Left to expand/collapse, Space to preview, Del to collect, Enter to zoom in.
/// </summary>
public sealed class FileTree : ListView
{
    private const int BulkThreshold = 300;

    private readonly RowCollection _rows = new();
    private readonly GridView _grid = new();
    private readonly Dictionary<string, GridViewColumn> _columns = new();
    private readonly Dictionary<FileNode, List<FileNode>> _sortedCache = new(ReferenceEqualityComparer.Instance);
    private string _sortKey = "size";
    private bool _ascending;
    private bool _suppressSelection;
    private Point? _dragStart;

    public ScanSession Session { get; }
    public bool IsFlat { get; private set; }
    public Func<IReadOnlyList<FileNode>, ContextMenu?>? MenuProvider { get; set; }
    public event Action<IReadOnlyList<FileNode>>? PreviewRequested;

    public FileTree(ScanSession session)
    {
        Session = session;
        Style = Ui.Res<Style>("FileList");
        ItemsSource = _rows;
        _grid.ColumnHeaderContainerStyle = Ui.Res<Style>("ColumnHeader");   // also styles the filler header after the last column
        View = _grid;
        TextSearch.SetTextPath(this, "Name");
        IsTextSearchEnabled = true;

        AddColumn("name", "Name", 320, NameTemplate);
        AddColumn("size", "Size", 190, SizeTemplate);
        AddColumn("percent", "%", 64, TextTemplate("PercentText"), right: true);
        AddColumn("items", "Items", 80, TextTemplate("ItemsText"), right: true);
        AddColumn("modified", "Modified", 110, TextTemplate("ModifiedText"), right: true);
        AddColumn("collect", Ui.GlyphAdd, 40, CollectTemplate, sortable: false);
        UpdateHeaders();

        AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(OnHeaderClick));
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnCellButton));
        SelectionChanged += (_, _) => { if (!_suppressSelection) SyncSelectionToSession(); };
        PreviewKeyDown += OnKey;
        MouseDoubleClick += OnDoubleClick;
        PreviewMouseLeftButtonDown += (_, e) => _dragStart = RowAt(e.OriginalSource) != null ? e.GetPosition(this) : null;
        PreviewMouseMove += OnMouseMove;
        ContextMenuOpening += OnContextMenuOpening;
        SizeChanged += (_, _) => FitNameColumn();
        ContextMenu = new ContextMenu();   // replaced on opening
        Reload(resetScroll: true);
    }

    // ---- Columns ----

    private void AddColumn(string key, string title, double width, string template, bool right = false, bool sortable = true)
    {
        var header = new GridViewColumnHeader
        {
            Style = Ui.Res<Style>("ColumnHeader"),
            Tag = sortable ? key : null,
            HorizontalContentAlignment = right ? HorizontalAlignment.Right : key == "collect" ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        };
        var column = new GridViewColumn { Header = header, Width = width, CellTemplate = (DataTemplate)XamlReader.Parse(template) };
        _columns[key] = column;
        header.Content = title;
        if (key == "collect")
        {
            header.Content = Ui.Glyph("", 12, Theme.TextTertiary);
            header.ToolTip = "Collect for deletion";
        }
        _grid.Columns.Add(column);
    }

    private void FitNameColumn()
    {
        double others = _grid.Columns.Where(c => c != _columns["name"]).Sum(c => c.ActualWidth);
        double available = ActualWidth - others - 14;
        if (available > 160) _columns["name"].Width = available;
    }

    private void UpdateHeaders()
    {
        foreach (var (key, column) in _columns)
        {
            if (key == "collect" || column.Header is not GridViewColumnHeader header) continue;
            string title = key switch { "name" => "Name", "size" => "Size", "percent" => "%", "items" => "Items", _ => "Modified" };
            bool sorted = key == _sortKey || (key == "percent" && _sortKey == "size");
            if (!sorted)
            {
                header.Content = title;
                header.Foreground = Theme.Brush(Theme.TextSecondary);
                continue;
            }
            var chevron = Ui.Glyph(_ascending ? Ui.GlyphChevronUp : Ui.GlyphChevronDown, 9, Theme.Cyan);
            chevron.Margin = new Thickness(6, 1, 0, 0);
            header.Content = Ui.Row(0, Ui.Text(title, 12, Theme.Text, Ui.DisplayFont), chevron);
        }
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Tag: string key }) return;
        if (key == "percent") key = "size";
        if (key == _sortKey) _ascending = !_ascending;
        else
        {
            _sortKey = key;
            _ascending = key == "name";
        }
        _sortedCache.Clear();
        UpdateHeaders();
        Reload(resetScroll: false);
    }

    private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                              "xmlns:c='clr-namespace:BitReaper.Controls;assembly=BitReaper'";

    private const string NameTemplate = "<DataTemplate " + Ns + ">" +
        "<Grid Margin='{Binding Indent}' Height='30' ToolTip='{Binding Tip}' ToolTipService.InitialShowDelay='900'>" +
        "<Grid.ColumnDefinitions><ColumnDefinition Width='20'/><ColumnDefinition Width='12'/><ColumnDefinition Width='Auto'/>" +
        "<ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>" +
        "<Button Tag='expand' AutomationProperties.Name='Expand or collapse' Style='{DynamicResource PlainGlyphButton}' Focusable='False' Padding='2' FontSize='9'" +
        " Content='{Binding ExpanderGlyph}' Visibility='{Binding ExpanderVisibility}'/>" +
        "<Ellipse Grid.Column='1' Width='9' Height='9' Fill='{Binding DotBrush}' VerticalAlignment='Center'/>" +
        "<Image Grid.Column='2' Source='{Binding Icon}' Width='18' Height='18' Margin='7,0,8,0' Opacity='{Binding IconOpacity}'" +
        " RenderOptions.BitmapScalingMode='HighQuality'/>" +
        "<TextBlock Grid.Column='3' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' FontSize='13'>" +
        "<Run Text='{Binding Name, Mode=OneWay}' Foreground='{Binding NameBrush}' TextDecorations='{Binding NameDecorations}' FontWeight='Medium'/>" +
        "<Run Text='{Binding Subtitle, Mode=OneWay}' Foreground='#6E62A0' FontSize='11'/></TextBlock>" +
        "<TextBlock Grid.Column='4' Text='&#xE72E;' FontFamily='{DynamicResource IconFont}' FontSize='11' Foreground='#FF7A3D'" +
        " Margin='6,0,2,0' VerticalAlignment='Center' Visibility='{Binding LockVisibility}'" +
        " ToolTip='BitReaper couldn&apos;t read this folder. Run as administrator to see inside.'/>" +
        "</Grid></DataTemplate>";

    private const string SizeTemplate = "<DataTemplate " + Ns + ">" +
        "<Grid Height='30'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='84'/></Grid.ColumnDefinitions>" +
        "<c:SizeBar Fraction='{Binding BarFraction}' Color='{Binding BarColor}' Dimmed='{Binding BarDimmed}' Margin='0,0,8,0'/>" +
        "<TextBlock Grid.Column='1' Text='{Binding SizeText}' Foreground='{Binding SizeBrush}' HorizontalAlignment='Right'" +
        " VerticalAlignment='Center' FontWeight='SemiBold' Typography.NumeralAlignment='Tabular'/>" +
        "</Grid></DataTemplate>";

    private static string TextTemplate(string property) => "<DataTemplate " + Ns + ">" +
        $"<TextBlock Text='{{Binding {property}}}' HorizontalAlignment='Right' VerticalAlignment='Center' FontSize='12'" +
        " Foreground='#A99BD8' Typography.NumeralAlignment='Tabular' TextTrimming='CharacterEllipsis'/></DataTemplate>";

    private const string CollectTemplate = "<DataTemplate " + Ns + ">" +
        "<Button Tag='collect' AutomationProperties.Name='{Binding CollectTip}' Style='{DynamicResource PlainGlyphButton}' Focusable='False' FontSize='15' HorizontalAlignment='Center'" +
        " Content='{Binding CollectGlyph}' Foreground='{Binding CollectBrush}' IsEnabled='{Binding CollectEnabled}'" +
        " ToolTip='{Binding CollectTip}'/></DataTemplate>";

    // ---- Data ----

    private IReadOnlyList<FileNode> SortedChildren(FileNode node)
    {
        if (IsDefaultSort) return node.Children;
        if (_sortedCache.TryGetValue(node, out var cached)) return cached;
        var sorted = Sort(node.Children);
        _sortedCache[node] = sorted;
        return sorted;
    }

    /// <summary>Children are stored largest first, so that order needs no sorting.</summary>
    private bool IsDefaultSort => _sortKey == "size" && !_ascending;

    private List<FileNode> Sort(IEnumerable<FileNode> nodes)
    {
        var list = nodes.ToList();
        Comparison<FileNode> comparison = _sortKey switch
        {
            "name" => (a, b) => NaturalCompare(a.Name, b.Name),
            "items" => (a, b) => a.ItemCount.CompareTo(b.ItemCount),
            "modified" => (a, b) => a.Modified.CompareTo(b.Modified),
            _ => (a, b) => a.Size.CompareTo(b.Size),
        };
        list.Sort(_ascending ? comparison : (a, b) => comparison(b, a));
        return list;
    }

    private static int NaturalCompare(string a, string b) =>
        Native.StrCmpLogical(a, b);

    /// <summary>Rebuilds every row, keeping expanded folders, the selection and the scroll position when possible.</summary>
    public void Reload(bool resetScroll)
    {
        var expanded = new HashSet<FileNode>(_rows.Where(r => r.IsExpanded).Select(r => r.Node), ReferenceEqualityComparer.Instance);
        var selected = new HashSet<FileNode>(SelectedItems.Cast<TreeRow>().Select(r => r.Node), ReferenceEqualityComparer.Instance);
        var top = resetScroll ? null : TopVisibleRow()?.Node;

        IsFlat = Session.FlatResults != null;
        var rows = new List<TreeRow>();
        if (IsFlat)
        {
            var items = IsDefaultSort ? Session.FlatResults! : Sort(Session.FlatResults!);
            rows.AddRange(items.Where(Session.IsAttached).Select(n => new TreeRow(this, n, 0)));
        }
        else if (!resetScroll)
        {
            AppendRows(rows, Session.Focus, 0, expanded);
        }
        else
        {
            AppendRows(rows, Session.Focus, 0, new HashSet<FileNode>(ReferenceEqualityComparer.Instance));
        }

        _suppressSelection = true;
        _rows.ReplaceAll(rows);
        SelectedItems.Clear();
        if (!resetScroll)
            foreach (var row in rows)
                if (selected.Contains(row.Node)) SelectedItems.Add(row);
        _suppressSelection = false;
        SyncSelectionToSession();

        UpdateLayout();
        if (top != null && rows.FirstOrDefault(r => ReferenceEquals(r.Node, top)) is { } topRow) ScrollToTop(topRow);
        else if (rows.Count > 0) ScrollIntoView(rows[0]);
    }

    private void AppendRows(List<TreeRow> rows, FileNode parent, int depth, HashSet<FileNode> expanded)
    {
        foreach (var child in SortedChildren(parent))
        {
            var row = new TreeRow(this, child, depth);
            rows.Add(row);
            if (child.IsDirectory && child.HasChildren && expanded.Contains(child))
            {
                row.IsExpanded = true;
                AppendRows(rows, child, depth + 1, expanded);
            }
        }
    }

    /// <summary>Redraws visible rows without rebuilding (collector or unit changes).</summary>
    public void RefreshRows()
    {
        foreach (var row in _rows) row.Refresh();
    }

    // ---- Expansion ----

    public void SetExpanded(TreeRow row, bool expand)
    {
        if (!row.CanExpand || row.IsExpanded == expand) return;
        int index = _rows.IndexOf(row);
        if (index < 0) return;
        row.IsExpanded = expand;
        if (expand)
        {
            var children = SortedChildren(row.Node).Select(n => new TreeRow(this, n, row.Depth + 1)).ToList();
            if (children.Count > BulkThreshold) BulkEdit(list => list.InsertRange(index + 1, children));
            else for (int i = 0; i < children.Count; i++) _rows.Insert(index + 1 + i, children[i]);
        }
        else
        {
            int end = index + 1;
            while (end < _rows.Count && _rows[end].Depth > row.Depth) end++;
            int count = end - index - 1;
            if (count > BulkThreshold) BulkEdit(list => list.RemoveRange(index + 1, count));
            else for (int i = end - 1; i > index; i--) _rows.RemoveAt(i);
        }
        row.Refresh();
    }

    private void BulkEdit(Action<List<TreeRow>> edit)
    {
        var selected = SelectedItems.Cast<TreeRow>().ToList();
        var top = TopVisibleRow();
        var list = _rows.ToList();
        edit(list);
        _suppressSelection = true;
        _rows.ReplaceAll(list);
        SelectedItems.Clear();
        foreach (var row in selected)
            if (list.Contains(row)) SelectedItems.Add(row);
        _suppressSelection = false;
        SyncSelectionToSession();
        UpdateLayout();
        if (top != null) ScrollToTop(top);
    }

    /// <summary>Selects <paramref name="node"/>, expanding its ancestors below the current folder.</summary>
    public void Reveal(FileNode node)
    {
        var chain = new List<FileNode>();
        for (var ancestor = node.Parent; ancestor != null && !ReferenceEquals(ancestor, Session.Focus); ancestor = ancestor.Parent)
            chain.Add(ancestor);
        chain.Reverse();
        foreach (var ancestor in chain)
            if (_rows.FirstOrDefault(r => ReferenceEquals(r.Node, ancestor)) is { } row) SetExpanded(row, true);
        if (_rows.FirstOrDefault(r => ReferenceEquals(r.Node, node)) is not { } target) return;
        SelectedItem = target;
        ScrollIntoView(target);
        FocusRow(target);
    }

    public void FocusSelectionOrFirst()
    {
        if (SelectedItem == null && _rows.Count > 0) SelectedIndex = 0;
        if (SelectedItem is TreeRow row) FocusRow(row);
        else Focus();
    }

    private void FocusRow(TreeRow row)
    {
        UpdateLayout();
        if (ItemContainerGenerator.ContainerFromItem(row) is ListViewItem item) item.Focus();
        else Focus();
    }

    // ---- Scrolling ----

    private ScrollViewer? Scroller => FindChild<ScrollViewer>(this);

    private TreeRow? TopVisibleRow()
    {
        if (Scroller is not { } scroller || _rows.Count == 0) return null;
        int index = (int)(scroller.VerticalOffset / 30);
        return index >= 0 && index < _rows.Count ? _rows[index] : null;
    }

    private void ScrollToTop(TreeRow row)
    {
        int index = _rows.IndexOf(row);
        if (index >= 0) Scroller?.ScrollToVerticalOffset(index * 30);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    // ---- Selection ----

    public IReadOnlyList<FileNode> SelectedNodes =>
        _rows.Where(r => SelectedItems.Contains(r)).Select(r => r.Node).ToList();

    private void SyncSelectionToSession()
    {
        var nodes = SelectedItems.Count == 0 ? new List<FileNode>() : SelectedNodes;
        Session.SetSelection(nodes);
        PreviewWindow.UpdateIfVisible(nodes);
    }

    private TreeRow? RowAt(object source)
    {
        if (source is not DependencyObject element) return null;
        if (ContainerFromElement(element) is ListViewItem item) return item.DataContext as TreeRow;
        return null;
    }

    // ---- Input ----

    private void OnCellButton(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button { Tag: string tag } button || button.DataContext is not TreeRow row) return;
        e.Handled = true;
        if (tag == "expand") SetExpanded(row, !row.IsExpanded);
        else if (tag == "collect") Session.ToggleCollect(new[] { row.Node });
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        var current = (Keyboard.FocusedElement as ListViewItem)?.DataContext as TreeRow ?? SelectedItem as TreeRow;
        switch (e.Key)
        {
            case Key.Space when modifiers == ModifierKeys.None:
                PreviewRequested?.Invoke(SelectedNodes);
                e.Handled = true;
                break;
            case Key.Delete or Key.Back when modifiers == ModifierKeys.None:
                Session.ToggleCollect(SelectedNodes);
                e.Handled = true;
                break;
            case Key.Enter when modifiers == ModifierKeys.None:
                OpenSelection();
                e.Handled = true;
                break;
            case Key.Right when modifiers == ModifierKeys.None && current != null:
                if (current.CanExpand && !current.IsExpanded) SetExpanded(current, true);
                else MoveTo(_rows.IndexOf(current) + 1, current.Depth + 1);
                e.Handled = true;
                break;
            case Key.Left when modifiers == ModifierKeys.None && current != null:
                if (current.IsExpanded) SetExpanded(current, false);
                else SelectParentRow(current);
                e.Handled = true;
                break;
            case Key.Escape when PreviewWindow.IsOpen:
                PreviewWindow.CloseIfOpen();
                e.Handled = true;
                break;
        }
    }

    private void MoveTo(int index, int depth)
    {
        if (index < 0 || index >= _rows.Count || _rows[index].Depth != depth) return;
        SelectedItem = _rows[index];
        ScrollIntoView(_rows[index]);
        FocusRow(_rows[index]);
    }

    private void SelectParentRow(TreeRow row)
    {
        int index = _rows.IndexOf(row);
        for (int i = index - 1; i >= 0; i--)
        {
            if (_rows[i].Depth < row.Depth)
            {
                MoveTo(i, _rows[i].Depth);
                return;
            }
        }
    }

    /// <summary>Enter: zoom into a folder, preview a file.</summary>
    public void OpenSelection()
    {
        var nodes = SelectedNodes;
        if (nodes.Count == 0) return;
        if (nodes.Count == 1 && nodes[0].IsDirectory) Session.ZoomIntoSelection();
        else PreviewRequested?.Invoke(nodes);
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || RowAt(e.OriginalSource) is not { } row) return;
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null) return;
        e.Handled = true;
        if (row.Node.IsDirectory)
        {
            if (IsFlat) Session.ShowInTree(row.Node);
            Session.Navigate(row.Node);
        }
        else
        {
            PreviewRequested?.Invoke(new[] { row.Node });
        }
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (var current = element; current != null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    /// <summary>Rows drag out as files: onto the Collector, or into Explorer as a copy.</summary>
    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance * 2
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance * 2) return;
        _dragStart = null;
        var nodes = SelectedNodes;
        if (nodes.Count == 0 || Session.Deletion is not DeletionPhase.Idle) return;
        var data = new DataObject(DataFormats.FileDrop, nodes.Select(n => n.Path).ToArray());
        DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Link);
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = RowAt(e.OriginalSource);
        if (row == null)
        {
            e.Handled = true;
            return;
        }
        if (!SelectedItems.Contains(row)) SelectedItem = row;
        var menu = MenuProvider?.Invoke(SelectedNodes);
        if (menu == null)
        {
            e.Handled = true;
            return;
        }
        ContextMenu = menu;
    }
}
