using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

public sealed class SettingsWindow : Window
{
    private readonly AppModel _model;
    private readonly StackPanel _stack = new() { Margin = new Thickness(24, 20, 24, 20) };

    public SettingsWindow(AppModel model)
    {
        _model = model;
        Title = "BitReaper Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new LinearGradientBrush(Theme.Night, Theme.NightDeep, 90);
        Foreground = Theme.Brush(Theme.Text);
        FontFamily = Ui.Res<FontFamily>("BodyFont");
        FontSize = 13;
        Content = _stack;
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dark = 1;
            Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int caption = Theme.Night.R | Theme.Night.G << 8 | Theme.Night.B << 16;
            Native.DwmSetWindowAttribute(handle, Native.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Build();
    }

    private void Build()
    {
        var prefs = _model.Prefs;
        _stack.Children.Clear();

        Section("Sizes",
                Radios("units", new (string, bool)[]
                {
                    ("Decimal: 1 KB = 1,000 bytes", prefs.DecimalUnits),
                    ("Binary: 1 KiB = 1,024 bytes (like Explorer)", !prefs.DecimalUnits),
                }, index => prefs.DecimalUnits = index == 0),
                Check("Count hard-linked files only once", prefs.DedupeHardLinks, value => prefs.DedupeHardLinks = value,
                      "Matches the space files really take (Windows and package caches use many hard links). Applies to the next scan."));

        Section("Deleting",
                Label("Default method"),
                Radios("method", new (string, bool)[]
                {
                    (DeleteMode.RecycleBin.Title(), prefs.DeleteMode == DeleteMode.RecycleBin),
                    (DeleteMode.Permanent.Title(), prefs.DeleteMode == DeleteMode.Permanent),
                }, index => prefs.DeleteMode = index == 0 ? DeleteMode.RecycleBin : DeleteMode.Permanent),
                Check("Review items before deleting", prefs.ConfirmDelete, value => prefs.ConfirmDelete = value),
                Label("Safety countdown (time to cancel a started deletion)"),
                Radios("countdown", new (string, bool)[]
                {
                    ("Off", prefs.CountdownSeconds == 0), ("3 seconds", prefs.CountdownSeconds == 3),
                    ("5 seconds", prefs.CountdownSeconds == 5), ("10 seconds", prefs.CountdownSeconds == 10),
                }, index => prefs.CountdownSeconds = new[] { 0, 3, 5, 10 }[index], horizontal: true));

        var adminStatus = Ui.Row(8,
                                 Ui.Glyph(_model.IsElevated ? Ui.GlyphCompleted : Ui.GlyphWarning, 14, _model.IsElevated ? Theme.Mint : Theme.Orange),
                                 Ui.Text(_model.IsElevated ? "Running as administrator: every folder is readable."
                                                           : "Running as a standard user: some system folders can't be read.", 13));
        var accessItems = new List<UIElement> { adminStatus };
        if (!_model.IsElevated)
        {
            var restart = Ui.Button(Ui.Label(Ui.GlyphShield, "Restart as Administrator", 12), Ui.Kind.Primary, compact: true, click: () =>
            {
                if (SystemAccess.RestartElevated("")) Application.Current.Shutdown();
            });
            restart.HorizontalAlignment = HorizontalAlignment.Left;
            accessItems.Add(restart);
        }
        Section("Access", accessItems.ToArray());

        bool installed = Installer.IsInstalled;
        var installStatus = Ui.Secondary(installed
            ? $"Installed in {Ui.Abbreviate(Installer.InstallDirectory)}, with a Start menu shortcut and “Scan with BitReaper” on folders and drives in Explorer."
            : "Add BitReaper to the Start menu and a “Scan with BitReaper” entry on folders and drives in Explorer. No administrator rights needed.", 12);
        var installButton = installed
            ? Ui.Button("Uninstall", Ui.Kind.Ghost, compact: true, click: () => Run(Installer.Uninstall, "BitReaper was uninstalled. Your settings are kept."))
            : Ui.Button(Ui.Label(Ui.GlyphAdd, "Install BitReaper", 12), Ui.Kind.Primary, compact: true,
                        click: () => Run(Installer.Install, "BitReaper is installed. Find it in the Start menu, or right-click a folder in Explorer › Scan with BitReaper."));
        installButton.HorizontalAlignment = HorizontalAlignment.Left;
        Section("Windows integration", installStatus, installButton);

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        var footer = Ui.Spread(Ui.Text($"BitReaper {version?.ToString(3)} for Windows", 11, Theme.TextTertiary),
                               Ui.Button("Done", Ui.Kind.Primary, click: Close));
        footer.Margin = new Thickness(0, 8, 0, 0);
        _stack.Children.Add(footer);
    }

    private void Run(Action action, string done)
    {
        try
        {
            action();
            MessageBox.Show(this, done, "BitReaper", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "BitReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Build();
    }

    private void Section(string title, params UIElement[] items)
    {
        var header = (FrameworkElement)Ui.SectionHeader(title);
        var column = Ui.Column(10, items);
        var panel = Ui.Panel(column, new Thickness(16, 14, 16, 14), corner: 12);
        _stack.Children.Add(Ui.Column(10, header, panel));
        ((FrameworkElement)_stack.Children[^1]).Margin = new Thickness(0, 0, 0, 18);
    }

    private static TextBlock Label(string text) => Ui.Text(text, 12, Theme.TextSecondary);

    private static UIElement Check(string text, bool value, Action<bool> set, string? tip = null)
    {
        var box = new CheckBox { Style = Ui.Res<Style>("NeonCheckBox"), Content = text, IsChecked = value, ToolTip = tip };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    private UIElement Radios(string group, (string Title, bool Selected)[] options, Action<int> select, bool horizontal = false)
    {
        var panel = new WrapPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            var radio = new RadioButton
            {
                Style = Ui.Res<Style>("NeonRadio"), Content = options[i].Title, IsChecked = options[i].Selected,
                GroupName = group + GetHashCode(), Margin = horizontal ? new Thickness(0, 0, 18, 0) : new Thickness(0, 0, 0, 6),
            };
            radio.Checked += (_, _) => select(index);
            panel.Children.Add(radio);
        }
        return panel;
    }
}
