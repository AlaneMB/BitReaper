using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>A screen shown in the main window, with its own toolbar items.</summary>
public interface IScreen
{
    UIElement? ToolbarCenter { get; }
    UIElement? ToolbarRight { get; }
    bool ShowsSkyline { get; }
    /// <summary>Screen-specific shortcuts; return true when handled.</summary>
    bool HandleKey(KeyEventArgs e, Key key, ModifierKeys modifiers);
}

public sealed class MainWindow : Window
{
    private readonly AppModel _model;
    private readonly Grid _root = new();
    private readonly ContentControl _content = new() { Focusable = false };
    private readonly ContentControl _toolbarCenter = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _toolbarRight = new() { Focusable = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly UIElement _skyline;
    private readonly Grid _alertLayer = new() { Visibility = Visibility.Collapsed };
    private readonly HomeView _home;
    private ResultsView? _results;
    private ScanningView? _scanning;
    private IScreen? _screen;
    private DateTime _lastVolumeRefresh = DateTime.MinValue;

    public MainWindow(AppModel model)
    {
        _model = model;
        Title = "BitReaper";
        Width = 1240;
        Height = 800;
        MinWidth = 1000;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Brush(Theme.Night);
        Foreground = Theme.Brush(Theme.Text);
        FontFamily = Ui.Res<FontFamily>("BodyFont");
        FontSize = 13;
        UseLayoutRounding = true;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/BitReaper.ico"));
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

        _skyline = BuildSkyline();
        _home = new HomeView(model, this);

        BuildLayout();

        model.Changed += Update;
        model.Error += ShowAlert;
        model.Volumes.Changed += () => _home.Refresh();
        model.Prefs.Changed += () => { _home.Refresh(); _results?.PreferencesChanged(); };
        PreviewKeyDown += OnPreviewKeyDown;
        Activated += (_, _) =>
        {
            if (DateTime.Now - _lastVolumeRefresh < TimeSpan.FromSeconds(3)) return;
            _lastVolumeRefresh = DateTime.Now;
            model.Volumes.Refresh();
        };
        SourceInitialized += (_, _) => ConfigureChrome();
        Update();
    }

    // ---- Layout ----

    private void BuildLayout()
    {
        // Night-sky gradient, a faint grid, two soft glows, the sunset skyline behind the toolbar.
        _root.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(Theme.Night, Theme.NightDeep, 90),
        });
        _root.Children.Add(new Rectangle { Fill = GridBrush() });
        _root.Children.Add(new Rectangle
        {
            Fill = new RadialGradientBrush(Theme.Magenta.WithAlpha(0.10), Colors.Transparent)
            {
                Center = new Point(1, 0), GradientOrigin = new Point(1, 0), RadiusX = 0.6, RadiusY = 0.9,
            },
        });
        _root.Children.Add(new Rectangle
        {
            Fill = new RadialGradientBrush(Theme.Cyan.WithAlpha(0.06), Colors.Transparent)
            {
                Center = new Point(0, 1), GradientOrigin = new Point(0, 1), RadiusX = 0.5, RadiusY = 0.8,
            },
        });
        _root.Children.Add(_skyline);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        layout.RowDefinitions.Add(new RowDefinition());

        var toolbar = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var brand = BrandMark();
        brand.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(_toolbarCenter, 1);
        Grid.SetColumn(_toolbarRight, 2);
        _toolbarRight.HorizontalAlignment = HorizontalAlignment.Right;
        toolbar.Children.Add(brand);
        toolbar.Children.Add(_toolbarCenter);
        toolbar.Children.Add(_toolbarRight);
        layout.Children.Add(toolbar);

        Grid.SetRow(_content, 1);
        layout.Children.Add(_content);
        _root.Children.Add(layout);
        _root.Children.Add(_alertLayer);
        Content = _root;
    }

    private static Brush GridBrush()
    {
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(26, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, 26)));
        var brush = new DrawingBrush(new GeometryDrawing(null, new Pen(Theme.Brush(Theme.Violet.WithAlpha(0.06)), 0.6), lines))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 26, 26), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 26, 26), ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    private static UIElement BuildSkyline()
    {
        var image = Ui.Image("SunsetBanner", width: 560);
        image.VerticalAlignment = VerticalAlignment.Top;
        image.HorizontalAlignment = HorizontalAlignment.Right;
        // 560 × 105: centred 34 px below the top, fading out towards its bottom edge.
        image.Margin = new Thickness(0, -18, -280, 0);
        image.Opacity = 0.85;
        image.IsHitTestVisible = false;
        image.OpacityMask = new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Black, 0), new(Colors.Black, 0.5), new(Colors.Transparent, 1),
        }, 90);
        var host = new Grid { IsHitTestVisible = false };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38, GridUnitType.Star) });
        host.Children.Add(image);
        return host;
    }

    /// <summary>Mascot + "BIT REAPER" wordmark, at the top-left of every screen.</summary>
    private static FrameworkElement BrandMark()
    {
        var peek = Ui.Image("ReaperPeek", height: 40).WithGlow(Theme.Magenta, 4, 0.6);
        var logo = Ui.Image("Logo", height: 22);
        logo.Margin = new Thickness(6, 0, 0, 0);
        var row = Ui.Row(0, peek, logo);
        System.Windows.Automation.AutomationProperties.SetName(row, "BitReaper");
        return row;
    }

    private void ConfigureChrome()
    {
        var handle = new WindowInteropHelper(this).Handle;
        _model.OwnerWindow = handle;
        int dark = 1;
        Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int caption = Theme.Night.R | Theme.Night.G << 8 | Theme.Night.B << 16;
        Native.DwmSetWindowAttribute(handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        int border = Theme.Line.R | Theme.Line.G << 8 | Theme.Line.B << 16;
        Native.DwmSetWindowAttribute(handle, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        HwndSource.FromHwnd(handle)?.AddHook(WindowHook);
    }

    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DEVICECHANGE = 0x0219;
        if (message == WM_DEVICECHANGE) _model.Volumes.Refresh();
        return IntPtr.Zero;
    }

    // ---- Screens ----

    private void Update()
    {
        IScreen screen;
        switch (_model.Screen)
        {
            case Screen.Scanning when _model.ScanJob is { } job:
                if (_scanning?.Job != job)
                {
                    _scanning?.Stop();
                    _scanning = new ScanningView(_model, job);
                }
                screen = _scanning;
                break;
            case Screen.Results when _model.Session is { } session:
                if (_results?.Session != session) _results = new ResultsView(_model, session, this);
                screen = _results;
                break;
            default:
                screen = _home;
                _home.Refresh();
                break;
        }
        if (_model.Screen != Screen.Scanning && _scanning != null)
        {
            _scanning.Stop();
            _scanning = null;
        }
        if (ReferenceEquals(_screen, screen)) return;
        _screen = screen;
        _content.Content = screen;
        _toolbarCenter.Content = screen.ToolbarCenter;
        _toolbarRight.Content = screen.ToolbarRight;
        _skyline.Visibility = screen.ShowsSkyline ? Visibility.Visible : Visibility.Collapsed;
        if (screen is ResultsView results) results.TakeFocus();
    }

    public void ChooseFolder()
    {
        if (_model.Screen == Screen.Scanning) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder to scan" };
        if (dialog.ShowDialog(this) == true) _model.Scan(dialog.FolderName);
    }

    public void OpenSettings()
    {
        var window = new SettingsWindow(_model) { Owner = this };
        window.ShowDialog();
    }

    // ---- Alerts ----

    public void ShowAlert(string message)
    {
        _alertLayer.Children.Clear();
        _alertLayer.Background = Theme.Brush(Colors.Black.WithAlpha(0.55));
        var ok = Ui.Button("OK", Ui.Kind.Primary, click: HideAlert);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.MinWidth = 80;
        ok.IsDefault = true;
        var card = Ui.Panel(Ui.Column(16,
                                      Ui.Row(12, Ui.Image("ReaperFolder", height: 60), Ui.Display("BitReaper", 20, bold: true)),
                                      Ui.Text(message, 14, wrap: true),
                                      ok),
                            new Thickness(24), glow: Theme.Magenta, fill: Theme.Brush(Theme.Panel.WithAlpha(0.97)));
        card.Width = 460;
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        _alertLayer.Children.Add(card);
        _alertLayer.Visibility = Visibility.Visible;
        ok.Focus();
    }

    private void HideAlert() => _alertLayer.Visibility = Visibility.Collapsed;

    // ---- Keyboard ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        bool ctrl = modifiers == ModifierKeys.Control;
        bool ctrlShift = modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

        if (_alertLayer.Visibility == Visibility.Visible)
        {
            if (key is Key.Escape or Key.Enter)
            {
                HideAlert();
                e.Handled = true;
            }
            return;
        }

        if (ctrl && key == Key.O) { ChooseFolder(); e.Handled = true; return; }
        if (ctrlShift && key == Key.D) { _model.GoHome(); e.Handled = true; return; }
        if (ctrl && key == Key.OemComma) { OpenSettings(); e.Handled = true; return; }
        if (_screen != null && _screen.HandleKey(e, key, modifiers)) e.Handled = true;
    }
}
