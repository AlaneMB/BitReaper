using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BitReaper.Controls;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>"Disks and Folders": every drive with its usage, plus starred and recent folders.</summary>
public sealed class HomeView : Grid, IScreen
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private readonly StackPanel _list = new() { MaxWidth = 900, Margin = new Thickness(32, 0, 32, 28) };
    private readonly Grid _dropOverlay;
    private readonly ContentControl _backButton = new() { Focusable = false };

    public UIElement? ToolbarCenter => null;
    public UIElement? ToolbarRight { get; }
    public bool ShowsSkyline => false;

    public HomeView(AppModel model, MainWindow window)
    {
        _model = model;
        _window = window;
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Children.Add(scroll);

        var footer = Footer();
        SetRow(footer, 1);
        Children.Add(footer);

        _dropOverlay = DropOverlay();
        SetRowSpan(_dropOverlay, 2);
        Children.Add(_dropOverlay);

        ToolbarRight = Ui.Row(8,
                              Ui.Button(Ui.Label(Ui.GlyphNewFolder, "Scan Folder…", 12), compact: true, click: window.ChooseFolder,
                                        tip: "Choose a folder to scan (Ctrl+O)"),
                              Ui.IconButton(Ui.GlyphSettings, window.OpenSettings, "Settings (Ctrl+,)"));

        AllowDrop = true;
        Background = Brushes.Transparent;
        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _dropOverlay.Visibility = Visibility.Collapsed;
        Drop += OnDrop;
        Refresh();
    }

    public bool HandleKey(KeyEventArgs e, Key key, ModifierKeys modifiers) => false;

    public void Refresh()
    {
        _list.Children.Clear();
        _list.Children.Add(Hero());
        if (!_model.IsElevated)
        {
            var banner = AdminBanner.Create(_model, compact: false);
            banner.Margin = new Thickness(0, 0, 0, 26);
            _list.Children.Add(banner);
        }

        _list.Children.Add(Section("Disks"));
        var volumes = _model.Volumes.Volumes;
        foreach (var volume in volumes) _list.Children.Add(VolumeRow(volume));
        if (volumes.Count == 0) _list.Children.Add(Ui.Secondary("No drives found."));

        var folders = _model.Prefs.StarredFolders
                            .Concat(_model.Prefs.RecentFolders.Where(p => !_model.Prefs.IsStarred(p)))
                            .ToList();
        if (folders.Count > 0)
        {
            var header = Section("Folders");
            header.Margin = new Thickness(0, 14, 0, 12);
            _list.Children.Add(header);
            foreach (var path in folders) _list.Children.Add(FolderRow(path));
        }

        _backButton.Content = _model.Session is { } session
            ? Ui.Button(Ui.Label(Ui.GlyphForward, $"Back to “{session.Target.DisplayName}”"), Ui.Kind.Primary, click: _model.ShowResults)
            : null;
    }

    private static FrameworkElement Section(string title)
    {
        var header = (FrameworkElement)Ui.SectionHeader(title);
        header.Margin = new Thickness(0, 0, 0, 12);
        return header;
    }

    /// <summary>Logo, mascot and the synthwave sunset.</summary>
    private static UIElement Hero()
    {
        var grid = new Grid { Height = 230, Margin = new Thickness(0, 0, 0, 22), ClipToBounds = false };
        var sunset = Ui.Image("SunsetBanner");
        sunset.VerticalAlignment = VerticalAlignment.Bottom;
        sunset.Opacity = 0.9;
        sunset.Margin = new Thickness(0, 0, 0, -30);
        grid.Children.Add(sunset);

        var reaper = Ui.Image("ReaperStanding", height: 180).WithGlow(Theme.Magenta, 10, 0.7);
        var logo = Ui.Image("Logo");
        logo.MaxWidth = 440;
        logo.HorizontalAlignment = HorizontalAlignment.Left;
        var tagline = Ui.Display("See what's filling your disk, collect what you don't need, reap it in one go.", 15, Theme.TextSecondary)
                        .WithGlow(Colors.Black, 4, 1);
        tagline.TextWrapping = TextWrapping.Wrap;
        var words = Ui.Column(12, logo, tagline);
        words.Margin = new Thickness(22, 0, 0, 92);
        words.VerticalAlignment = VerticalAlignment.Bottom;
        var row = new DockPanel { VerticalAlignment = VerticalAlignment.Bottom, LastChildFill = true };
        DockPanel.SetDock(reaper, Dock.Left);
        row.Children.Add(reaper);
        row.Children.Add(words);
        grid.Children.Add(row);
        return grid;
    }

    private UIElement VolumeRow(VolumeInfo volume)
    {
        double fraction = volume.Total > 0 ? (double)volume.Used / volume.Total : 0;
        bool hasResults = _model.Session?.Target.VolumeId?.Equals(volume.Id, StringComparison.OrdinalIgnoreCase) == true;

        var icon = Ui.Icon(IconCache.ForPath(volume.Id, large: true), 48).WithGlow(Theme.Cyan, 8, 0.35);

        var title = Ui.Row(8, Ui.Display(volume.Name, 18, bold: true));
        if (!string.IsNullOrEmpty(volume.Format))
        {
            title.Children.Add(new Border
            {
                Margin = new Thickness(8, 2, 0, 0),
                BorderBrush = Theme.Brush(Theme.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Text(volume.Format, 10, Theme.TextSecondary, Ui.DisplayFont),
            });
        }
        var percent = Ui.Text($"{ByteFormat.Percent(volume.Used, volume.Total)} used", 12,
                              fraction > 0.9 ? Theme.Danger : Theme.TextSecondary, Ui.DisplayFont);
        var gauge = new UsageGauge { Fraction = fraction, Colors = Palette.UsageColors(fraction), Height = 10 };

        var detail = new TextBlock { FontSize = 13, Foreground = Theme.Brush(Theme.TextSecondary) };
        void ShowFree()
        {
            detail.Inlines.Clear();
            detail.Inlines.Add(new System.Windows.Documents.Run(ByteFormat.Size(volume.Available)) { Foreground = Theme.Brush(Theme.Text), FontWeight = FontWeights.Bold });
            detail.Inlines.Add($" free of {ByteFormat.Size(volume.Total)}");
            if (volume.RecycleBin > 0)
                detail.Inlines.Add(new System.Windows.Documents.Run($" · incl. {ByteFormat.Size(volume.RecycleBin)} in the Recycle Bin")
                {
                    Foreground = Theme.Brush(Theme.TextTertiary),
                });
        }
        void ShowUsed()
        {
            detail.Inlines.Clear();
            detail.Inlines.Add(new System.Windows.Documents.Run(ByteFormat.Size(volume.Used)) { Foreground = Theme.Brush(Theme.Text), FontWeight = FontWeights.Bold });
            detail.Inlines.Add($" used of {ByteFormat.Size(volume.Total)}");
        }
        ShowFree();

        var info = Ui.Column(8, Ui.Spread(title, percent), gauge, detail);
        info.Margin = new Thickness(18, 0, 18, 0);
        info.VerticalAlignment = VerticalAlignment.Center;

        var actions = new StackPanel { Width = 130, VerticalAlignment = VerticalAlignment.Center };
        if (hasResults)
        {
            actions.Children.Add(Ui.Button("Show Results", Ui.Kind.Primary, click: _model.ShowResults));
            var rescan = Ui.Button("Rescan", compact: true, click: () => _model.Scan(volume));
            rescan.Margin = new Thickness(0, 8, 0, 0);
            rescan.HorizontalAlignment = HorizontalAlignment.Center;
            actions.Children.Add(rescan);
        }
        else
        {
            actions.Children.Add(Ui.Button(Ui.Label(Ui.GlyphTarget, "Scan"), Ui.Kind.Primary, click: () => _model.Scan(volume)));
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(info, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(icon);
        grid.Children.Add(info);
        grid.Children.Add(actions);

        var panel = Ui.Panel(grid, new Thickness(18));
        panel.Margin = new Thickness(0, 0, 0, 12);
        panel.ToolTip = volume.Id;
        panel.MouseEnter += (_, _) => { panel.Glow = Theme.Magenta; ShowUsed(); };
        panel.MouseLeave += (_, _) => { panel.Glow = Theme.Violet; ShowFree(); };
        return panel;
    }

    private UIElement FolderRow(string path)
    {
        bool starred = _model.Prefs.IsStarred(path);
        bool exists = Directory.Exists(path);
        bool hasResults = _model.Session is { Target.VolumeId: null } session
                          && session.Root.Name.Equals(path, StringComparison.OrdinalIgnoreCase);

        var star = Ui.GlyphButton(starred ? Ui.GlyphStarFilled : Ui.GlyphStar, () => _model.Prefs.ToggleStar(path),
                                  starred ? "Unstar folder" : "Star folder to keep it in this list", 15);
        star.Foreground = Theme.Brush(starred ? Theme.Yellow : Theme.TextTertiary);
        if (starred) star.WithGlow(Theme.Yellow, 5, 0.8);

        var icon = Ui.Icon(exists ? IconCache.ForPath(path, large: true) : IconCache.ForPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows), large: true), 32);
        icon.Opacity = exists ? 1 : 0.4;
        icon.Margin = new Thickness(10, 0, 12, 0);
        var name = Ui.Display(AppModel.DisplayName(path), 15);
        var location = Ui.Text(Ui.Abbreviate(path), 11, Theme.TextSecondary);
        location.TextTrimming = TextTrimming.CharacterEllipsis;
        var words = Ui.Column(2, name, location);
        words.VerticalAlignment = VerticalAlignment.Center;

        var right = Ui.Row(8);
        if (!exists) right.Children.Add(Ui.Secondary("Not found", 12, wrap: false));
        Button? remove = null;
        if (!starred)
        {
            remove = Ui.GlyphButton(Ui.GlyphClose, () => _model.Prefs.Forget(path), "Remove from list", 11);
            remove.Visibility = Visibility.Hidden;
            right.Children.Add(remove);
        }
        if (hasResults) right.Children.Add(Ui.Button("Show Results", Ui.Kind.Primary, compact: true, click: _model.ShowResults));
        var scan = Ui.Button(hasResults ? "Rescan" : "Scan", compact: true, click: () => _model.Scan(path));
        scan.IsEnabled = exists;
        right.Children.Add(scan);
        foreach (FrameworkElement child in right.Children) child.Margin = new Thickness(8, 0, 0, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(words, 2);
        Grid.SetColumn(right, 3);
        grid.Children.Add(star);
        grid.Children.Add(icon);
        grid.Children.Add(words);
        grid.Children.Add(right);

        var panel = Ui.Panel(grid, new Thickness(14, 10, 16, 10), corner: 12, fill: Theme.Brush(Theme.Panel.WithAlpha(0.7)));
        panel.Margin = new Thickness(0, 0, 0, 10);
        panel.MouseEnter += (_, _) =>
        {
            panel.Glow = Theme.Magenta;
            panel.Fill = Theme.Brush(Theme.Panel.WithAlpha(0.9));
            if (remove != null) remove.Visibility = Visibility.Visible;
        };
        panel.MouseLeave += (_, _) =>
        {
            panel.Glow = Theme.Violet;
            panel.Fill = Theme.Brush(Theme.Panel.WithAlpha(0.7));
            if (remove != null) remove.Visibility = Visibility.Hidden;
        };
        return panel;
    }

    private UIElement Footer()
    {
        var left = Ui.Row(12,
                          Ui.Button(Ui.Label(Ui.GlyphNewFolder, "Scan Folder…"), click: _window.ChooseFolder),
                          Ui.Secondary("or drop any folder or drive onto this window", 12, wrap: false));
        var bar = Ui.Spread(left, _backButton);
        var border = new Border
        {
            Background = Theme.Brush(Theme.Panel.WithAlpha(0.85)),
            Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = Ui.Res<Brush>("NeonBorderBrush"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = bar,
        };
        return border;
    }

    private static Grid DropOverlay()
    {
        var content = Ui.Column(10, Ui.Image("ReaperFolder", height: 140), Ui.Display("Drop to scan", 24, bold: true).WithGlow(Theme.Magenta, 8));
        foreach (FrameworkElement child in content.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        var frame = new Rectangle
        {
            Stroke = Ui.Res<Brush>("NeonBorderStrongBrush"), StrokeThickness = 3, StrokeDashArray = new DoubleCollection { 4, 2.4 },
            RadiusX = 18, RadiusY = 18, Fill = Theme.Brush(Theme.Cyan.WithAlpha(0.06)),
        }.WithGlow(Theme.Cyan, 10, 0.6);
        var grid = new Grid { Margin = new Thickness(12), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        grid.Children.Add(frame);
        grid.Children.Add(content);
        return grid;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) && _model.Screen != Screen.Scanning;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        _dropOverlay.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) _model.Scan(paths[0]);
    }
}

/// <summary>The Windows counterpart of the macOS Full Disk Access banner: elevation unlocks protected folders.</summary>
public static class AdminBanner
{
    public static FrameworkElement Create(AppModel model, bool compact)
    {
        var title = Ui.Display("Run BitReaper as administrator for complete results", compact ? 13 : 15);
        title.TextWrapping = TextWrapping.Wrap;
        var body = Ui.Secondary("Without it, Windows hides other users' profiles, System Volume Information and parts of " +
                                "Windows and ProgramData, which then show up as hidden space or locked folders.", compact ? 11 : 13);
        var restart = Ui.Button(Ui.Label(Ui.GlyphShield, "Restart as Administrator", 12), Ui.Kind.Primary, compact: true, click: () =>
        {
            string args = model.Session?.Target.Path is { } path && model.Screen == Screen.Results ? $"\"{path}\"" : "";
            if (SystemAccess.RestartElevated(args)) Application.Current.Shutdown();
        });
        restart.HorizontalAlignment = HorizontalAlignment.Left;
        restart.Margin = new Thickness(0, 4, 0, 0);
        var words = Ui.Column(4, title, body, restart);
        var shield = Ui.Glyph(Ui.GlyphShield, compact ? 18 : 26, Theme.Orange).WithGlow(Theme.Orange, 5);
        shield.VerticalAlignment = VerticalAlignment.Top;
        shield.Margin = new Thickness(0, 2, 12, 0);
        var dock = new DockPanel();
        DockPanel.SetDock(shield, Dock.Left);
        dock.Children.Add(shield);
        dock.Children.Add(words);
        return Ui.Panel(dock, new Thickness(compact ? 12 : 16), corner: 12, glow: Theme.Orange, fill: Theme.Brush(Theme.Orange.WithAlpha(0.08)));
    }
}
