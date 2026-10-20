using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BitReaper.Controls;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

public sealed class ResultsView : Grid, IScreen
{
    private readonly AppModel _model;
    private readonly MainWindow _window;
    private readonly FileTree _tree;
    private readonly InfoSidebar _sidebar;
    private readonly CollectorPanel _collector;
    private readonly ContentControl _listHeader = new() { Focusable = false };
    private readonly ContentControl _breadcrumb = new() { Focusable = false };
    private readonly TextBlock _emptySearch;
    private readonly Border _toast;
    private readonly TextBlock _toastText = Ui.Text("", 13, weight: FontWeights.Medium);
    private readonly Grid _sheetLayer = new() { Visibility = Visibility.Collapsed };
    private readonly TextBox _search;
    private readonly RadioButton _treeMode, _largestMode;
    private readonly Button _rescanButton;
    private readonly DispatcherTimer _sidebarDebounce;
    private bool _showingFailures;

    public ScanSession Session { get; }
    public UIElement? ToolbarCenter { get; }
    public UIElement? ToolbarRight { get; }
    public bool ShowsSkyline => true;

    public ResultsView(AppModel model, ScanSession session, MainWindow window)
    {
        _model = model;
        _window = window;
        Session = session;
        Margin = new Thickness(14, 4, 14, 14);

        // ---- Toolbar ----
        _treeMode = Segment(Ui.GlyphList, "Tree View", ViewMode.Tree);
        _largestMode = Segment(Ui.GlyphChart, "Largest Files", ViewMode.Largest);
        var segments = new Border
        {
            Background = Theme.Brush(Theme.Panel.WithAlpha(0.9)), BorderBrush = Theme.Brush(Theme.Line), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11), Padding = new Thickness(3),
            Child = Ui.Row(4, _treeMode, _largestMode),
            ToolTip = "Tree (Ctrl+1) or the largest files in this folder (Ctrl+2)",
        };
        ToolbarCenter = segments;

        _search = new TextBox { Style = Ui.Res<Style>("NeonSearchBox"), Width = 260, Height = 34 };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search");
        _search.TextChanged += (_, _) => session.SearchText = _search.Text;
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _search.Text = "";
                _tree!.FocusSelectionOrFirst();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                _tree!.FocusSelectionOrFirst();
                e.Handled = true;
            }
        };
        _rescanButton = Ui.IconButton(Ui.GlyphRefresh, () => session.Rescan(), "Rescan this folder (Ctrl+R)");
        ToolbarRight = Ui.Row(8,
                              _rescanButton,
                              Ui.IconButton(Ui.GlyphDrive, model.GoHome, "Disks and Folders (Ctrl+Shift+D)"),
                              _search,
                              Ui.IconButton(Ui.GlyphSettings, window.OpenSettings, "Settings (Ctrl+,)"));

        // ---- Layout ----
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Children.Add(_breadcrumb);

        _tree = new FileTree(session) { MenuProvider = BuildMenu };
        _tree.PreviewRequested += Preview;
        _emptySearch = Ui.Secondary("", 14, wrap: true);
        _emptySearch.HorizontalAlignment = HorizontalAlignment.Center;
        _emptySearch.VerticalAlignment = VerticalAlignment.Center;
        _emptySearch.TextAlignment = TextAlignment.Center;
        _emptySearch.Visibility = Visibility.Collapsed;
        _emptySearch.IsHitTestVisible = false;

        var treeArea = new Grid();
        treeArea.Children.Add(_tree);
        treeArea.Children.Add(_emptySearch);
        var treeDock = new DockPanel();
        DockPanel.SetDock(_listHeader, Dock.Top);
        treeDock.Children.Add(_listHeader);
        var rule = new Border { Height = 1, Background = Theme.Brush(Theme.Line.WithAlpha(0.6)) };
        DockPanel.SetDock(rule, Dock.Top);
        treeDock.Children.Add(rule);
        treeDock.Children.Add(treeArea);
        var treePanel = Ui.Panel(treeDock, new Thickness(2, 0, 2, 4));
        treePanel.MinWidth = 560;

        _sidebar = new InfoSidebar(model, session, this) { Width = 344 };
        var main = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        main.ColumnDefinitions.Add(new ColumnDefinition());
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _sidebar.Margin = new Thickness(0, -12, -14, -12);
        Grid.SetColumn(_sidebar, 1);
        main.Children.Add(treePanel);
        main.Children.Add(_sidebar);
        SetRow(main, 1);
        Children.Add(main);

        _collector = new CollectorPanel(model, session, this);
        SetRow(_collector, 2);
        Children.Add(_collector);

        _toast = new Border
        {
            Background = Theme.Brush(Theme.PanelRaised), BorderBrush = Ui.Res<Brush>("NeonBorderStrongBrush"), BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(16, 9, 16, 9), MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 104),
            Child = Ui.Row(8, Ui.Glyph(Ui.GlyphSparkle, 13, Theme.Cyan), _toastText), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        }.WithGlow(Theme.Magenta, 10, 0.5);
        _toastText.TextWrapping = TextWrapping.Wrap;
        SetRowSpan(_toast, 3);
        Children.Add(_toast);

        SetRowSpan(_sheetLayer, 3);
        _sheetLayer.Margin = new Thickness(-14, -64, -14, -14);
        Children.Add(_sheetLayer);

        _sidebarDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _sidebarDebounce.Tick += (_, _) =>
        {
            _sidebarDebounce.Stop();
            _sidebar.Rebuild();
        };

        session.Changed += OnSessionChanged;
        RefreshBreadcrumb();
        RefreshHeader();
        RefreshModes();
    }

    private RadioButton Segment(string glyph, string title, ViewMode mode)
    {
        var button = new RadioButton
        {
            Style = Ui.Res<Style>("SegmentButton"), GroupName = "mode" + GetHashCode(), Content = Ui.Label(glyph, title),
            IsChecked = Session.ViewMode == mode,
        };
        System.Windows.Automation.AutomationProperties.SetName(button, title);
        button.Checked += (_, _) => Session.ViewMode = mode;
        return button;
    }

    public void TakeFocus() => Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _tree.FocusSelectionOrFirst());

    public void PreferencesChanged()
    {
        _tree.RefreshRows();
        RefreshHeader();
        _sidebar.Rebuild();
        _collector.Rebuild();
    }

    // ---- Session changes ----

    private void OnSessionChanged(SessionChange change)
    {
        if (change.HasFlag(SessionChange.Focus) || change.HasFlag(SessionChange.Flat))
        {
            _tree.Reload(resetScroll: true);
            UpdateEmptySearch();
        }
        else if (change.HasFlag(SessionChange.Tree))
        {
            _tree.Reload(resetScroll: false);
        }
        else if (change.HasFlag(SessionChange.Collector))
        {
            _tree.RefreshRows();
        }

        if ((change & (SessionChange.Focus | SessionChange.Tree | SessionChange.Busy | SessionChange.Flat)) != 0) RefreshHeader();
        if ((change & (SessionChange.Focus | SessionChange.History | SessionChange.Tree)) != 0) RefreshBreadcrumb();
        if ((change & (SessionChange.Focus | SessionChange.Tree | SessionChange.Selection | SessionChange.Collector)) != 0)
        {
            _sidebarDebounce.Stop();
            _sidebarDebounce.Start();
        }
        if ((change & (SessionChange.Collector | SessionChange.Deletion | SessionChange.Outcome | SessionChange.Tree)) != 0) _collector.Rebuild();
        if (change.HasFlag(SessionChange.Deletion)) UpdateDeleteSheet();
        if (change.HasFlag(SessionChange.Outcome) && Session.FailureReport is { } failures && !_showingFailures) ShowFailures(failures);
        if (change.HasFlag(SessionChange.Toast)) UpdateToast();
        if (change.HasFlag(SessionChange.Reveal) && Session.RevealTarget is { } target) _tree.Reveal(target);
        if (change.HasFlag(SessionChange.ViewMode)) RefreshModes();
        if (change.HasFlag(SessionChange.SearchFocus))
        {
            _search.Focus();
            _search.SelectAll();
        }
    }

    private void RefreshModes()
    {
        _treeMode.IsChecked = Session.ViewMode == ViewMode.Tree;
        _largestMode.IsChecked = Session.ViewMode == ViewMode.Largest;
        if (Session.SearchText != _search.Text) _search.Text = Session.SearchText;
        Neon.SetPlaceholder(_search, $"Search in {Session.DisplayName(Session.Focus)}");
    }

    private void UpdateEmptySearch()
    {
        bool empty = Session.FlatResults is { Count: 0 } && !Session.IsQuerying;
        _emptySearch.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        string query = Session.SearchText.Trim();
        _emptySearch.Text = query.Length > 0 ? $"No results for “{query}”\nCheck the spelling or search another folder." : "No files here.";
    }

    private void UpdateToast()
    {
        if (Session.Toast is { } message)
        {
            _toastText.Text = message;
            _toast.Visibility = Visibility.Visible;
        }
        else
        {
            _toast.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Card header above the tree: the current folder, its size, and the file system for drives.</summary>
    private void RefreshHeader()
    {
        var focus = Session.Focus;
        bool isVolumeRoot = ReferenceEquals(focus, Session.Root) && Session.Target.VolumeId != null;
        var icon = Ui.Icon(isVolumeRoot ? IconCache.ForPath(Session.Target.Path, large: true) : IconCache.Icon(focus, large: true), 34);
        icon.Margin = new Thickness(0, 0, 12, 0);

        string subtitle;
        if (Session.FlatResults is { } results)
        {
            string query = Session.SearchText.Trim();
            subtitle = query.Length == 0 ? $"{ByteFormat.Count(results.Count)} largest files" : $"{ByteFormat.Count(results.Count)} matches for “{query}”";
        }
        else
        {
            subtitle = Ui.Plural(focus.ItemCount, "item");
        }
        var words = Ui.Column(1, Ui.Display(Session.DisplayName(focus), 16), Ui.Text(subtitle, 12, Theme.TextSecondary));
        var left = Ui.Row(0, icon, words);
        if (Session.IsQuerying || Session.IsRescanning)
        {
            var busy = Ui.Text(Session.IsRescanning ? "Rescanning…" : "Searching…", 12, Theme.Cyan);
            busy.Margin = new Thickness(14, 0, 0, 0);
            left.Children.Add(busy);
        }

        var size = Ui.Display(ByteFormat.Size(focus.Size), 22, bold: true).WithGlow(Theme.Violet, 6);
        size.HorizontalAlignment = HorizontalAlignment.Right;
        var right = Ui.Column(1, size);
        if (isVolumeRoot && Session.Volume is { } volume)
        {
            var kind = Ui.Text(string.Join(" · ", new[] { volume.Format, volume.Kind }.Where(s => s.Length > 0)), 11, Theme.TextSecondary);
            kind.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(kind);
        }
        var header = Ui.Spread(left, right);
        header.Margin = new Thickness(14, 10, 16, 10);
        _listHeader.Content = header;
        _rescanButton.IsEnabled = !Session.IsRescanning;
        UpdateEmptySearch();
        Neon.SetPlaceholder(_search, $"Search in {Session.DisplayName(focus)}");
    }

    /// <summary>Back / forward / up, then the path: [Disks and Folders] › Local Disk (C:) › Users › you</summary>
    private void RefreshBreadcrumb()
    {
        var back = Ui.IconButton(Ui.GlyphBack, Session.GoBack, "Back (Alt+Left)", 28);
        back.IsEnabled = Session.CanGoBack;
        var forward = Ui.IconButton(Ui.GlyphForward, Session.GoForward, "Forward (Alt+Right)", 28);
        forward.IsEnabled = Session.CanGoForward;
        var up = Ui.IconButton(Ui.GlyphUp, Session.GoUp, "Enclosing folder (Alt+Up)", 28);
        up.IsEnabled = Session.CanGoUp;

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 2, 6) };
        chips.Children.Add(Chip("Disks and Folders", isRoot: true, isCurrent: false, _model.GoHome));
        var crumbs = Session.Breadcrumb;
        for (int i = 0; i < crumbs.Count; i++)
        {
            var node = crumbs[i];
            var chevron = Ui.Glyph(Ui.GlyphChevronRight, 9, Theme.TextTertiary);
            chevron.Margin = new Thickness(6, 0, 6, 0);
            chips.Children.Add(chevron);
            chips.Children.Add(Chip(Session.DisplayName(node), false, i == crumbs.Count - 1, () => Session.Navigate(node)));
        }
        var scroller = new ScrollViewer
        {
            Content = chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false, Margin = new Thickness(8, 0, 0, 0),
        };
        scroller.Loaded += (_, _) => scroller.ScrollToRightEnd();
        scroller.PreviewMouseWheel += (_, e) =>
        {
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta / 2.0);
            e.Handled = true;
        };

        var dock = new DockPanel();
        var buttons = Ui.Row(6, back, forward, up);
        DockPanel.SetDock(buttons, Dock.Left);
        dock.Children.Add(buttons);
        dock.Children.Add(scroller);
        _breadcrumb.Content = dock;
    }

    private static Button Chip(string title, bool isRoot, bool isCurrent, Action click)
    {
        var content = isRoot
            ? Ui.Row(6, Ui.Glyph(Ui.GlyphDrive, 11), Ui.Text(title, 13, font: Ui.DisplayFont))
            : (UIElement)Ui.Text(title, 13, isCurrent ? Theme.Text : Theme.TextSecondary, isCurrent ? Ui.DisplayFont : null,
                                 isCurrent ? null : FontWeights.Medium);
        var button = new Button { Content = content, Style = Ui.Res<Style>("NeonButton"), Padding = new Thickness(12, 5, 12, 5) };
        Neon.SetCornerRadius(button, new CornerRadius(8));
        if (isRoot)
        {
            button.Background = Theme.Gradient(Theme.Blue.WithAlpha(0.6), Theme.Violet.WithAlpha(0.5));
            button.BorderBrush = Theme.Brush(Theme.Cyan.WithAlpha(0.8));
            Neon.SetGlow(button, Theme.Blue);
        }
        else
        {
            button.Background = Theme.Brush(Theme.Panel.WithAlpha(isCurrent ? 0.95 : 0.7));
            button.BorderBrush = isCurrent ? Ui.Res<Brush>("NeonBorderBrush") : Theme.Brush(Theme.Line);
            Neon.SetGlow(button, isCurrent ? Theme.Violet : Colors.Transparent);
        }
        button.Click += (_, _) => click();
        return button;
    }

    // ---- Context menu ----

    private ContextMenu? BuildMenu(IReadOnlyList<FileNode> targets)
    {
        if (targets.Count == 0) return null;
        var single = targets.Count == 1 ? targets[0] : null;
        bool allCollected = targets.All(Session.Collector.Contains);
        string collectTitle = allCollected
            ? (single != null ? "Remove from Collector" : $"Remove {targets.Count} Items from Collector")
            : (single != null ? "Add to Collector" : $"Add {targets.Count} Items to Collector");

        var items = new List<object>
        {
            Ui.MenuItem(collectTitle, allCollected ? Ui.GlyphClose : Ui.GlyphAdd, () => Session.ToggleCollect(targets), "Del"),
            new Separator(),
        };
        if (single is { IsDirectory: true })
            items.Add(Ui.MenuItem("Zoom In", Ui.GlyphZoomIn, () =>
            {
                if (Session.FlatResults != null) Session.ShowInTree(single);
                Session.Navigate(single);
            }, "Ctrl+Down"));
        if (Session.FlatResults != null && single != null)
            items.Add(Ui.MenuItem("Show in Tree", Ui.GlyphList, () => Session.ShowInTree(single)));
        items.Add(Ui.MenuItem("Preview", Ui.GlyphPreview, () => Preview(targets), "Space"));
        items.Add(Ui.MenuItem("Show in Explorer", Ui.GlyphFolder, () => ShellActions.Reveal(targets.Select(n => n.Path))));
        items.Add(Ui.MenuItem("Open", Ui.GlyphOpen, () =>
        {
            foreach (var node in targets.Take(10)) ShellActions.Open(node.Path);
        }));
        items.Add(Ui.MenuItem(single != null ? "Copy Path" : "Copy Paths", Ui.GlyphCopy, () => ShellActions.CopyPaths(targets.Select(n => n.Path))));
        if (single != null)
            items.Add(Ui.MenuItem("Properties", Ui.GlyphProperties, () => ShellActions.ShowProperties(single.Path, _model.OwnerWindow)));
        if (single is { IsDirectory: true })
        {
            items.Add(new Separator());
            items.Add(Ui.MenuItem("Rescan Folder", Ui.GlyphRefresh, () => Session.Rescan(single)));
        }
        return Ui.Menu(items.ToArray());
    }

    public void Preview(IReadOnlyList<FileNode> nodes) => PreviewWindow.Toggle(nodes, _window);

    // ---- Sheets ----

    private void UpdateDeleteSheet()
    {
        if (Session.DeleteSheetRequested && _sheetLayer.Visibility != Visibility.Visible) ShowSheet(Sheets.Delete(_model, Session, CloseSheet));
        else if (!Session.DeleteSheetRequested && !_showingFailures && _sheetLayer.Visibility == Visibility.Visible) CloseSheet();
    }

    private void ShowFailures(List<DeletionFailure> failures)
    {
        _showingFailures = true;
        ShowSheet(Sheets.Failures(failures, () =>
        {
            _showingFailures = false;
            Session.DismissFailureReport();
            CloseSheet();
        }));
    }

    private void ShowSheet(FrameworkElement sheet)
    {
        _sheetLayer.Children.Clear();
        _sheetLayer.Background = Theme.Brush(Colors.Black.WithAlpha(0.55));
        sheet.HorizontalAlignment = HorizontalAlignment.Center;
        sheet.VerticalAlignment = VerticalAlignment.Center;
        _sheetLayer.Children.Add(sheet);
        _sheetLayer.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => sheet.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    private void CloseSheet()
    {
        _sheetLayer.Visibility = Visibility.Collapsed;
        _sheetLayer.Children.Clear();
        Session.DismissDeleteSheet();
        TakeFocus();
    }

    private bool SheetOpen => _sheetLayer.Visibility == Visibility.Visible;

    // ---- Keyboard ----

    public bool HandleKey(KeyEventArgs e, Key key, ModifierKeys modifiers)
    {
        bool ctrl = modifiers == ModifierKeys.Control;
        bool alt = modifiers == ModifierKeys.Alt;
        bool inText = Keyboard.FocusedElement is TextBox;

        if (SheetOpen)
        {
            if (key == Key.Escape)
            {
                if (_showingFailures)
                {
                    _showingFailures = false;
                    Session.DismissFailureReport();
                }
                CloseSheet();
                return true;
            }
            return false;
        }
        if (key == Key.Escape && Session.Deletion is DeletionPhase.Countdown)
        {
            Session.CancelCountdown();
            return true;
        }
        if (Session.Deletion is not DeletionPhase.Idle) return false;

        switch (key)
        {
            case Key.R when ctrl:
            case Key.F5 when modifiers == ModifierKeys.None:
                Session.Rescan();
                return true;
            case Key.F when ctrl:
                Session.RequestSearchFocus();
                return true;
            case Key.D1 or Key.NumPad1 when ctrl:
                Session.ViewMode = ViewMode.Tree;
                return true;
            case Key.D2 or Key.NumPad2 when ctrl:
                Session.ViewMode = ViewMode.Largest;
                return true;
            case Key.Left when alt:
            case Key.OemOpenBrackets when ctrl:
            case Key.BrowserBack:
                Session.GoBack();
                return true;
            case Key.Right when alt:
            case Key.OemCloseBrackets when ctrl:
            case Key.BrowserForward:
                Session.GoForward();
                return true;
            case Key.Up when alt || (ctrl && !inText):
                Session.GoUp();
                return true;
            case Key.Down when ctrl && !inText:
                Session.SetSelection(_tree.SelectedNodes);
                Session.ZoomIntoSelection();
                return true;
            case Key.K when ctrl:
                if (!Session.Collector.IsEmpty) Session.SetCollectorExpanded(!Session.CollectorExpanded);
                return true;
            case Key.Delete when modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                Session.RequestDelete();
                return true;
            case Key.Z when ctrl && !inText:
                Session.UndoLastDeletion();
                return true;
        }
        return false;
    }
}
