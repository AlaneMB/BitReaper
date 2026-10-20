using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BitReaper.Controls;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

public sealed class ScanningView : Grid, IScreen
{
    private readonly AppModel _model;
    private readonly DispatcherTimer _timer;
    private readonly ProgressRing _ring = new() { Width = 290, Height = 290 };
    private readonly Image _reaper = Ui.Image("ReaperDisk", height: 130);
    private readonly TextBlock _bytes = Ui.Display("0 bytes", 20, bold: true);
    private readonly TextBlock _percent = Ui.Display("", 14, Theme.Cyan);
    private readonly TextBlock _path = Ui.Text("", 11, Theme.TextSecondary, Ui.MonoFont);
    private readonly TextBlock _files, _folders, _elapsed, _unreadable;
    private readonly FrameworkElement _unreadablePanel;
    private readonly RotateTransform _tilt = new();
    private readonly TranslateTransform _bob = new();
    private int _ticks;

    public ScanJob Job { get; }
    public UIElement? ToolbarCenter => null;
    public UIElement? ToolbarRight => null;
    public bool ShowsSkyline => false;

    public ScanningView(AppModel model, ScanJob job)
    {
        _model = model;
        Job = job;

        var sunset = Ui.Image("SunsetBanner");
        sunset.VerticalAlignment = VerticalAlignment.Bottom;
        sunset.Opacity = 0.55;
        sunset.IsHitTestVisible = false;
        Children.Add(sunset);

        _reaper.WithGlow(Theme.Magenta, 8);
        _reaper.RenderTransformOrigin = new Point(0.5, 0.5);
        _reaper.RenderTransform = new TransformGroup { Children = { _tilt, _bob } };
        var readout = Ui.Row(6, _bytes, _percent);
        readout.HorizontalAlignment = HorizontalAlignment.Center;
        var inside = Ui.Column(4, _reaper, readout);
        inside.HorizontalAlignment = HorizontalAlignment.Center;
        inside.VerticalAlignment = VerticalAlignment.Center;
        var ring = new Grid { Width = 290, Height = 290, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(_ring);
        ring.Children.Add(inside);

        var title = Ui.Display($"Scanning “{job.Target.DisplayName}”…", 24, bold: true).WithGlow(Theme.Magenta, 6);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _path.HorizontalAlignment = HorizontalAlignment.Center;
        _path.MaxWidth = 620;
        _path.TextTrimming = TextTrimming.CharacterEllipsis;
        _path.Text = job.Target.Path;

        FrameworkElement Stat(string name, Color color, out TextBlock value)
        {
            value = Ui.Display("0", 18, color, bold: true);
            value.HorizontalAlignment = HorizontalAlignment.Center;
            var label = Ui.Text(name.ToUpperInvariant(), 10, Theme.TextSecondary, Ui.DisplayFont);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var panel = Ui.Panel(Ui.Column(2, value, label), new Thickness(14, 10, 14, 10), corner: 10, glow: color);
            panel.MinWidth = 104;
            panel.Margin = new Thickness(6, 0, 6, 0);
            return panel;
        }
        var stats = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        stats.Children.Add(Stat("Files", Theme.Cyan, out _files));
        stats.Children.Add(Stat("Folders", Theme.Purple, out _folders));
        stats.Children.Add(Stat("Elapsed", Theme.Yellow, out _elapsed));
        _unreadablePanel = Stat("No access", Theme.Orange, out _unreadable);
        _unreadablePanel.Visibility = Visibility.Collapsed;
        stats.Children.Add(_unreadablePanel);

        var cancel = Ui.Button("Cancel", click: model.CancelScan, tip: "Stop scanning (Esc)");
        cancel.HorizontalAlignment = HorizontalAlignment.Center;
        cancel.MinWidth = 90;

        var stack = Ui.Column(24, ring, Ui.Column(8, title, _path), stats, cancel);
        stack.VerticalAlignment = VerticalAlignment.Center;
        stack.Margin = new Thickness(0, -30, 0, 0);
        Children.Add(stack);

        if (!model.IsElevated)
        {
            var tip = Ui.Secondary("Tip: run BitReaper as administrator to see inside every folder.", 11, wrap: false);
            tip.HorizontalAlignment = HorizontalAlignment.Center;
            tip.VerticalAlignment = VerticalAlignment.Bottom;
            tip.Margin = new Thickness(0, 0, 0, 20);
            Children.Add(tip);
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    public bool HandleKey(KeyEventArgs e, Key key, ModifierKeys modifiers)
    {
        if (key != Key.Escape) return false;
        _model.CancelScan();
        return true;
    }

    private void Tick()
    {
        double t = (DateTime.Now - Job.StartDate).TotalSeconds;
        _ring.Tick(t);
        _bob.Y = Math.Sin(t * 2.4) * 5;
        _tilt.Angle = Math.Sin(t * 1.2) * 3;
        if (_ticks++ % 3 != 0) return;

        var stats = Job.Stats;
        var fraction = Job.Fraction(stats);
        _ring.Fraction = fraction;
        _bytes.Text = ByteFormat.Size(stats.Bytes);
        _percent.Text = fraction is { } f ? $"{f * 100:0} %" : "";
        if (!string.IsNullOrEmpty(stats.CurrentPath)) _path.Text = stats.CurrentPath;
        _files.Text = ByteFormat.Count(stats.Files);
        _folders.Text = ByteFormat.Count(stats.Folders);
        int seconds = (int)t;
        _elapsed.Text = $"{seconds / 60}:{seconds % 60:00}";
        if (stats.Unreadable > 0)
        {
            _unreadablePanel.Visibility = Visibility.Visible;
            _unreadable.Text = ByteFormat.Count(stats.Unreadable);
        }
    }
}
