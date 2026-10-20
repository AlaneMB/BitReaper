using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BitReaper.Controls;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>
/// Right-hand column: the current folder as a donut chart with its legend, and details about the
/// selected items below it. Rebuilt from the session whenever the folder, tree or selection changes.
/// </summary>
public sealed class InfoSidebar : ScrollViewer
{
    private readonly AppModel _model;
    private readonly ScanSession _session;
    private readonly ResultsView _results;
    private readonly StackPanel _stack = new() { Margin = new Thickness(12, 12, 14, 12) };

    public InfoSidebar(AppModel model, ScanSession session, ResultsView results)
    {
        _model = model;
        _session = session;
        _results = results;
        Content = _stack;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Focusable = false;
        Rebuild();
    }

    public void Rebuild()
    {
        _stack.Children.Clear();
        Add(Overview());
        Add(_session.Selection.Count switch
        {
            0 => Tips(),
            1 => ItemDetails(_session.Selection[0]),
            _ => MultiSelection(_session.Selection),
        });
        if (_session.UnreadableCount > 0 && !_model.IsElevated)
        {
            var note = Ui.Row(6, Ui.Glyph(Ui.GlyphLock, 12, Theme.Orange),
                              Ui.Text($"{Ui.Plural(_session.UnreadableCount, "folder")} couldn't be read", 12, Theme.Orange, Ui.DisplayFont));
            Add(Ui.Column(8, note, AdminBanner.Create(_model, compact: true)));
        }
    }

    private void Add(UIElement element)
    {
        if (element is FrameworkElement framework) framework.Margin = new Thickness(0, 0, 0, 12);
        _stack.Children.Add(element);
    }

    private bool IsVolumeRoot => ReferenceEquals(_session.Focus, _session.Root) && _session.Target.VolumeId != null;

    private ImageSource FocusIcon => IsVolumeRoot ? IconCache.ForPath(_session.Target.Path, large: true) : IconCache.Icon(_session.Focus, large: true);

    // ---- Overview ----

    private UIElement Overview()
    {
        var focus = _session.Focus;
        long threshold = Math.Max(focus.Size / 200, 1);
        var listed = focus.Children.Take(7).Where(c => c.Size >= threshold).ToList();
        var rest = focus.Children.Skip(listed.Count).ToList();
        long restSize = rest.Sum(c => c.Size);
        long total = Math.Max(focus.Size, 1);
        var volume = _session.Volume;

        var header = new DockPanel();
        var icon = Ui.Icon(FocusIcon, 32);
        icon.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(icon, Dock.Left);
        var size = Ui.Display(ByteFormat.Size(IsVolumeRoot && volume != null ? volume.Total : focus.Size), 17, bold: true);
        size.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(size, Dock.Right);
        var name = Ui.Display(_session.DisplayName(focus), 16);
        header.Children.Add(icon);
        header.Children.Add(size);
        header.Children.Add(name);

        // Donut
        var top = SelectedTopLevel();
        var slices = new List<DonutChart.Slice>();
        foreach (var node in listed)
        {
            var color = ColorOf(node);
            bool dimmed = top != null && !ReferenceEquals(node, top);
            slices.Add(new DonutChart.Slice((double)node.Size / total, color, dimmed, top != null && !dimmed));
        }
        if (restSize > 0) slices.Add(new DonutChart.Slice((double)restSize / total, Theme.TextTertiary, top != null, false));
        var donut = new Grid { Height = 210 };
        donut.Children.Add(new DonutChart { Slices = slices });
        var center = DonutCenter(volume);
        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center;
        donut.Children.Add(center.WithGlow(Theme.Violet, 8, 0.7));

        // Legend
        var legend = new StackPanel();
        foreach (var node in listed) legend.Children.Add(LegendRow(node, ColorOf(node)));
        if (restSize > 0)
        {
            var row = new DockPanel { Margin = new Thickness(6, 5, 6, 5) };
            var right = Ui.Text(ByteFormat.Size(restSize), 13, Theme.TextSecondary);
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(Ui.Row(10, Ui.Dot(Theme.TextTertiary), Ui.Text("smaller objects…", 13, Theme.TextSecondary),
                                    Ui.Text($"({ByteFormat.Count(rest.Count)})", 11, Theme.TextTertiary)));
            legend.Children.Add(row);
        }
        if (focus.Children.Count == 0)
            legend.Children.Add(Ui.Secondary(focus.IsUnreadable ? "BitReaper couldn't read this folder." : "This folder is empty.", 13));

        var column = Ui.Column(14, header, donut, legend);
        if (IsVolumeRoot && volume != null)
        {
            column.Children.Add(new Border { Height = 1, Background = Ui.Res<Brush>("NeonBorderBrush"), Margin = new Thickness(0, 14, 0, 0) });
            var summary = VolumeSummary(volume);
            summary.Margin = new Thickness(0, 14, 0, 0);
            column.Children.Add(summary);
        }
        return Ui.Panel(column, new Thickness(16));
    }

    private Color ColorOf(FileNode node)
    {
        var info = _session.ColorInfo(node);
        return Palette.ColorFor(info?.Slot, info?.Depth ?? 0);
    }

    private FileNode? SelectedTopLevel()
    {
        if (_session.Selection.Count != 1) return null;
        var node = _session.Selection[0];
        while (node.Parent != null && !ReferenceEquals(node.Parent, _session.Focus)) node = node.Parent;
        return ReferenceEquals(node.Parent, _session.Focus) ? node : null;
    }

    private FrameworkElement DonutCenter(VolumeInfo? volume)
    {
        var focus = _session.Focus;
        FrameworkElement Centered(params UIElement[] items)
        {
            var column = Ui.Column(2, items);
            foreach (FrameworkElement item in column.Children) item.HorizontalAlignment = HorizontalAlignment.Center;
            return column;
        }
        if (_session.Selection.Count == 1)
        {
            var node = _session.Selection[0];
            var of = Ui.Text($"of {_session.DisplayName(focus)}", 11, Theme.TextSecondary);
            of.MaxWidth = 110;
            return Centered(Ui.Display(ByteFormat.Size(node.Size), 22, bold: true),
                            Ui.Display(ByteFormat.Percent(node.Size, focus.Size), 15), of);
        }
        if (IsVolumeRoot && volume != null)
            return Centered(Ui.Display(ByteFormat.Size(volume.Used), 22, bold: true),
                            Ui.Display(ByteFormat.Percent(volume.Used, volume.Total), 15),
                            Ui.Text("Used", 12, Theme.TextSecondary));
        return Centered(Ui.Display(ByteFormat.Size(focus.Size), 22, bold: true),
                        Ui.Text(Ui.Plural(focus.ItemCount, "item"), 12, Theme.TextSecondary));
    }

    private UIElement LegendRow(FileNode node, Color color)
    {
        bool collected = _session.Collector.IsCovered(node);
        var name = Ui.Text(node.Name, 13, collected ? Theme.TextTertiary : Theme.Text, weight: FontWeights.Medium);
        if (collected) name.TextDecorations = TextDecorations.Strikethrough;
        var size = Ui.Text(ByteFormat.Size(node.Size), 13);
        size.Margin = new Thickness(8, 0, 0, 0);
        var dot = Ui.Dot(color);
        dot.Margin = new Thickness(0, 0, 10, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(size, Dock.Right);
        dock.Children.Add(dot);
        dock.Children.Add(size);
        dock.Children.Add(name);
        var button = new Button
        {
            Style = Ui.Res<Style>("BareButton"), Content = dock, Padding = new Thickness(6, 5, 6, 5),
            ToolTip = node.IsDirectory ? $"Zoom into “{node.Name}”" : $"Select “{node.Name}”",
        };
        button.Click += (_, _) =>
        {
            if (node.IsDirectory) _session.Navigate(node);
            else _session.Reveal(node);
        };
        button.ContextMenu = Ui.Menu(
            Ui.MenuItem(_session.Collector.Contains(node) ? "Remove from Collector" : "Add to Collector", Ui.GlyphAdd,
                        () => _session.ToggleCollect(new[] { node })),
            Ui.MenuItem("Show in Explorer", Ui.GlyphFolder, () => ShellActions.Reveal(new[] { node.Path })));
        return button;
    }

    private FrameworkElement VolumeSummary(VolumeInfo volume)
    {
        var rows = new StackPanel();
        UIElement Line(Color dot, UIElement label, long bytes, string? tip = null)
        {
            var size = Ui.Text(ByteFormat.Size(bytes), 13, weight: FontWeights.Medium);
            var row = Ui.Spread(Ui.Row(10, Ui.Dot(dot), label), size);
            row.Margin = new Thickness(6, 3, 6, 3);
            if (tip != null) row.ToolTip = tip;
            return row;
        }
        if (_session.HiddenSpace is > 0 and var hidden)
        {
            var label = Ui.Row(4, Ui.Text("Hidden space", 13, Theme.Purple, weight: FontWeights.Medium), Ui.Glyph(Ui.GlyphInfo, 11, Theme.Purple));
            rows.Children.Add(Line(Theme.Purple, label, hidden,
                "Space the drive reports as used that the scan couldn't see: file system metadata, System Restore points and " +
                "shadow copies in System Volume Information, and folders BitReaper isn't allowed to read. " +
                "Run as administrator and rescan to reduce it. Restore points are managed in System Properties › System Protection."));
        }
        if (volume.RecycleBin > 0)
        {
            var label = Ui.Text("In the Recycle Bin", 13, Theme.TextSecondary);
            var line = Line(Theme.TextTertiary.WithAlpha(0.8), label, volume.RecycleBin, "Empty the Recycle Bin to reclaim this space.");
            rows.Children.Add(line);
        }
        rows.Children.Add(Line(Theme.TextTertiary.WithAlpha(0.6), Ui.Text("Free space", 13, Theme.TextSecondary), volume.Available));
        return rows;
    }

    // ---- Cards below the overview ----

    private UIElement Tips()
    {
        var text = Ui.Column(6,
                             Ui.Text($"Scanned {Ui.Plural(_session.Root.ItemCount, "item")} in {_session.ScanDuration.TotalSeconds:0.0} s", 13,
                                     weight: FontWeights.SemiBold),
                             Ui.Secondary("Select an item for details. Press Del or click ⊕ to add it to the Collector, Space to preview it, " +
                                          "double-click a folder to zoom in."));
        var icon = Ui.Glyph(Ui.GlyphInfo, 18, Theme.Cyan).WithGlow(Theme.Cyan, 4);
        icon.VerticalAlignment = VerticalAlignment.Top;
        icon.Margin = new Thickness(0, 1, 10, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        dock.Children.Add(icon);
        dock.Children.Add(text);
        return Ui.Panel(dock, new Thickness(14), corner: 12, glow: Theme.Blue, fill: Theme.Brush(Theme.Blue.WithAlpha(0.07)));
    }

    private UIElement ItemDetails(FileNode node)
    {
        var collector = _session.Collector;
        bool collected = collector.Contains(node);
        var covering = collector.CoveringAncestor(node);
        var focus = _session.Focus;
        var color = ColorOf(node);

        var title = Ui.Display(_session.DisplayName(node), 15);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxHeight = 60;
        var head = Ui.Row(12, Ui.Icon(IconCache.Icon(node, large: true), 40), Ui.Column(3, title, Ui.Text(IconCache.Kind(node), 12, Theme.TextSecondary)));
        ((FrameworkElement)head.Children[1]).MaxWidth = 230;

        var size = Ui.Display(ByteFormat.Size(node.Size), 28, bold: true).WithGlow(color, 6);
        var gauge = new UsageGauge
        {
            Fraction = focus.Size > 0 ? (double)node.Size / focus.Size : 0, Colors = new[] { color, Palette.BarEnd(color) }, Height = 7,
        };
        var share = Ui.Text($"{ByteFormat.Percent(node.Size, focus.Size)} of “{_session.DisplayName(focus)}”", 11, Theme.TextSecondary);

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition());
        void Detail(string label, string value)
        {
            int row = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = Ui.Text(label, 12, Theme.TextSecondary);
            name.Margin = new Thickness(0, 3, 12, 3);
            name.VerticalAlignment = VerticalAlignment.Top;
            var text = new TextBox
            {
                Text = value, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Theme.Brush(Theme.Text), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(-2, 3, 0, 3),
                Padding = new Thickness(0), CaretBrush = Theme.Brush(Theme.Cyan),
            };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            details.Children.Add(name);
            details.Children.Add(text);
        }
        if (node.IsDirectory) Detail("Items", ByteFormat.Count(node.ItemCount));
        if (node.Modified > 0) Detail("Modified", ByteFormat.DateAndTime(node.ModifiedLocal));
        if (_session.Volume is { } volume) Detail("Of disk", ByteFormat.Percent(node.Size, volume.Total));
        Detail("Where", node.Parent?.Path ?? node.Path);

        var column = Ui.Column(14, head, Ui.Column(6, size, gauge, share), details);
        if (node.IsUnreadable)
            column.Children.Add(Ui.Row(6, Ui.Glyph(Ui.GlyphLock, 12, Theme.Orange),
                                       Ui.Text("BitReaper couldn't read this folder, so its size is unknown.", 12, Theme.Orange, wrap: true)));
        if (node.IsLink)
            column.Children.Add(Ui.Secondary("This is a link to another location. BitReaper lists it but doesn't follow it, so " +
                                             "its target is counted where it really lives.", 12));

        var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (covering != null)
        {
            actions.Children.Add(Ui.Row(6, Ui.Glyph(Ui.GlyphCheck, 12, Theme.TextSecondary),
                                        Ui.Text($"Collected with “{_session.DisplayName(covering)}”", 12, Theme.TextSecondary)));
        }
        else
        {
            var collect = Ui.Button(Ui.Label(collected ? Ui.GlyphClose : Ui.GlyphAdd, collected ? "Remove from Collector" : "Add to Collector"),
                                    collected ? Ui.Kind.Normal : Ui.Kind.Primary, click: () => _session.ToggleCollect(new[] { node }));
            actions.Children.Add(collect);
        }
        actions.Children.Add(ButtonPair(
            Ui.Button(Ui.Label(Ui.GlyphPreview, "Preview", 12), compact: true, click: () => _results.Preview(new[] { node })),
            Ui.Button(Ui.Label(Ui.GlyphFolder, "Show in Explorer", 12), compact: true, click: () => ShellActions.Reveal(new[] { node.Path }))));
        actions.Children.Add(ButtonPair(
            node.IsDirectory ? Ui.Button(Ui.Label(Ui.GlyphZoomIn, "Zoom In", 12), compact: true, click: () => _session.Navigate(node)) : null,
            Ui.Button(Ui.Label(Ui.GlyphCopy, "Copy Path", 12), compact: true, click: () =>
            {
                ShellActions.CopyPaths(new[] { node.Path });
                _session.ShowToast("Copied path");
            })));
        column.Children.Add(actions);
        return Ui.Panel(column, new Thickness(16), glow: color);
    }

    private static UIElement ButtonPair(Button? left, Button right)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (left != null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            left.Margin = new Thickness(0, 0, 4, 0);
            right.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
        }
        grid.Children.Add(right);
        return grid;
    }

    private UIElement MultiSelection(IReadOnlyList<FileNode> nodes)
    {
        // Don't double count items selected together with their folder.
        var set = new HashSet<FileNode>(nodes, ReferenceEqualityComparer.Instance);
        long total = nodes.Where(n =>
        {
            for (var p = n.Parent; p != null; p = p.Parent)
                if (set.Contains(p)) return false;
            return true;
        }).Sum(n => n.Size);
        bool allCollected = nodes.All(_session.Collector.Contains);

        var head = Ui.Row(12, Ui.Glyph(Ui.GlyphStack, 28, Theme.Cyan).WithGlow(Theme.Cyan, 5),
                          Ui.Column(0, Ui.Display($"{ByteFormat.Count(nodes.Count)} items selected", 15),
                                    Ui.Display(ByteFormat.Size(total), 24, bold: true)));
        var list = new StackPanel();
        foreach (var node in nodes.OrderByDescending(n => n.Size).Take(10))
        {
            var size = Ui.Text(ByteFormat.Size(node.Size), 12, Theme.TextSecondary);
            var row = Ui.Spread(Ui.Row(8, Ui.Dot(ColorOf(node)), Ui.Text(node.Name, 12)), size);
            row.Margin = new Thickness(0, 2, 0, 2);
            list.Children.Add(row);
        }
        if (nodes.Count > 10) list.Children.Add(Ui.Secondary($"and {ByteFormat.Count(nodes.Count - 10)} more…"));

        var collect = Ui.Button(Ui.Label(allCollected ? Ui.GlyphClose : Ui.GlyphAdd, allCollected ? "Remove All from Collector" : "Add All to Collector"),
                                allCollected ? Ui.Kind.Normal : Ui.Kind.Primary, click: () => _session.ToggleCollect(nodes));
        var pair = ButtonPair(
            Ui.Button(Ui.Label(Ui.GlyphPreview, "Preview", 12), compact: true, click: () => _results.Preview(nodes)),
            Ui.Button(Ui.Label(Ui.GlyphFolder, "Show in Explorer", 12), compact: true, click: () => ShellActions.Reveal(nodes.Select(n => n.Path))));
        return Ui.Panel(Ui.Column(14, head, list, Ui.Column(0, collect, pair)), new Thickness(16));
    }
}
