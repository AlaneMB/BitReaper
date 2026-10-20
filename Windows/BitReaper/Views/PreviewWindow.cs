using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BitReaper.Model;
using BitReaper.Util;

namespace BitReaper.Views;

/// <summary>
/// Space-bar preview, the Windows stand-in for Quick Look: images, text, audio and video play inline;
/// folders show what's biggest inside; anything else shows its icon and details. The window never
/// takes keyboard focus, so arrow keys keep browsing the tree and the preview follows the selection.
/// </summary>
public sealed class PreviewWindow : Window
{
    private static PreviewWindow? _current;

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
        { "png", "jpg", "jpeg", "gif", "bmp", "tif", "tiff", "ico", "webp", "jfif", "heic" };
    private static readonly HashSet<string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "m4v", "mov", "wmv", "avi", "mkv", "webm", "mp3", "wav", "wma", "m4a", "aac", "flac" };
    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt", "md", "log", "json", "xml", "yml", "yaml", "csv", "tsv", "ini", "cfg", "conf", "toml", "cs", "js", "ts", "tsx", "jsx",
        "py", "rb", "go", "rs", "java", "kt", "swift", "c", "cpp", "h", "hpp", "html", "htm", "css", "scss", "sh", "ps1", "bat", "cmd",
        "sql", "gitignore", "editorconfig", "csproj", "sln", "props", "targets", "plist", "meta", "asset", "prefab", "unity",
    };

    private readonly ContentControl _body = new() { Focusable = false };
    private readonly ContentControl _header = new() { Focusable = false };
    private IReadOnlyList<FileNode> _nodes = Array.Empty<FileNode>();
    private int _index;
    private MediaElement? _media;

    public static bool IsOpen => _current is { IsVisible: true };

    public static void Toggle(IReadOnlyList<FileNode> nodes, Window owner)
    {
        if (IsOpen)
        {
            CloseIfOpen();
            return;
        }
        if (nodes.Count == 0) return;
        _current = new PreviewWindow { Owner = owner };
        _current.Show(nodes);
    }

    public static void UpdateIfVisible(IReadOnlyList<FileNode> nodes)
    {
        if (IsOpen && nodes.Count > 0) _current!.Show(nodes);
    }

    public static void CloseIfOpen()
    {
        _current?.Close();
        _current = null;
    }

    private PreviewWindow()
    {
        Title = "Preview";
        Width = 760;
        Height = 560;
        MinWidth = 360;
        MinHeight = 260;
        ShowActivated = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new LinearGradientBrush(Theme.Night, Theme.NightDeep, 90);
        Foreground = Theme.Brush(Theme.Text);
        FontFamily = Ui.Res<FontFamily>("BodyFont");
        var dock = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        dock.Children.Add(_body);
        Content = dock;
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int dark = 1;
            Native.DwmSetWindowAttribute(handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Space) { Close(); e.Handled = true; }
            else if (e.Key == Key.Right && _nodes.Count > 1) { _index = (_index + 1) % _nodes.Count; Render(); e.Handled = true; }
            else if (e.Key == Key.Left && _nodes.Count > 1) { _index = (_index + _nodes.Count - 1) % _nodes.Count; Render(); e.Handled = true; }
        };
        Closed += (_, _) =>
        {
            StopMedia();
            if (ReferenceEquals(_current, this)) _current = null;
            Owner?.Activate();
        };
    }

    private void Show(IReadOnlyList<FileNode> nodes)
    {
        _nodes = nodes;
        _index = 0;
        Render();
        if (!IsVisible) Show();
    }

    private void Render()
    {
        StopMedia();
        var node = _nodes[_index];
        Title = $"Preview – {node.Name}";

        var icon = Ui.Icon(IconCache.Icon(node, large: true), 32);
        icon.Margin = new Thickness(0, 0, 12, 0);
        string meta = $"{IconCache.Kind(node)} · {ByteFormat.Size(node.Size)}";
        if (node.Modified > 0) meta += $" · {ByteFormat.DateAndTime(node.ModifiedLocal)}";
        if (_nodes.Count > 1) meta += $" · {_index + 1} of {_nodes.Count} (← →)";
        var words = Ui.Column(2, Ui.Display(node.Name, 16), Ui.Text(meta, 12, Theme.TextSecondary));
        var buttons = Ui.Row(8,
                             Ui.Button(Ui.Label(Ui.GlyphOpen, "Open", 12), compact: true, click: () => ShellActions.Open(node.Path)),
                             Ui.Button(Ui.Label(Ui.GlyphFolder, "Show in Explorer", 12), compact: true, click: () => ShellActions.Reveal(new[] { node.Path })));
        foreach (var child in buttons.Children.OfType<Button>()) child.Focusable = false;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(icon);
        header.Children.Add(buttons);
        header.Children.Add(words);
        _header.Content = header;

        _body.Content = Ui.Panel(Body(node), new Thickness(8), corner: 12);
    }

    private UIElement Body(FileNode node)
    {
        if (node.IsDirectory) return FolderSummary(node);
        string ext = node.Extension;
        try
        {
            if (ImageTypes.Contains(ext)) return ImageView(node.Path);
            if (MediaTypes.Contains(ext)) return MediaView(node.Path);
            if (TextTypes.Contains(ext) || LooksLikeText(node.Path)) return TextView(node.Path);
        }
        catch (Exception)
        {
            // Unreadable or unsupported content: fall through to the details view.
        }
        return Details(node);
    }

    private static UIElement ImageView(string path)
    {
        var bitmap = new BitmapImage();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1600;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();
        var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private UIElement MediaView(string path)
    {
        _media = new MediaElement
        {
            Source = new Uri(path), LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Close, Stretch = Stretch.Uniform,
        };
        _media.MediaFailed += (_, _) => _body.Content = Ui.Panel(Details(_nodes[_index]), new Thickness(8), corner: 12);
        _media.Play();
        var grid = new Grid();
        grid.Children.Add(Ui.Glyph("", 64, Theme.TextTertiary));
        grid.Children.Add(_media);
        return grid;
    }

    private void StopMedia()
    {
        _media?.Stop();
        _media?.Close();
        _media = null;
    }

    private static bool LooksLikeText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[4096];
        int read = stream.Read(buffer, 0, buffer.Length);
        if (read == 0) return true;
        for (int i = 0; i < read; i++)
            if (buffer[i] == 0) return false;
        return true;
    }

    private static UIElement TextView(string path)
    {
        const int limit = 256 * 1024;
        string text;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true))
        {
            var buffer = new char[limit];
            int read = reader.ReadBlock(buffer, 0, limit);
            text = new string(buffer, 0, read);
            if (!reader.EndOfStream) text += "\n\n… (preview truncated)";
        }
        return new TextBox
        {
            Text = text, IsReadOnly = true, FontFamily = Ui.MonoFont, FontSize = 12, Background = Brushes.Transparent,
            Foreground = Theme.Brush(Theme.Text), BorderThickness = new Thickness(0), Focusable = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static UIElement FolderSummary(FileNode node)
    {
        var stack = new StackPanel { Margin = new Thickness(10) };
        stack.Children.Add(Ui.Text($"{Ui.Plural(node.ItemCount, "item")} · biggest inside:", 13, Theme.TextSecondary));
        foreach (var child in node.Children.Take(14))
        {
            var row = Ui.Spread(Ui.Row(8, Ui.Icon(IconCache.Icon(child), 16), Ui.Text(child.Name, 13)),
                                Ui.Text(ByteFormat.Size(child.Size), 13, Theme.TextSecondary));
            row.Margin = new Thickness(0, 6, 0, 0);
            stack.Children.Add(row);
        }
        if (node.Children.Count > 14) stack.Children.Add(Ui.Secondary($"and {ByteFormat.Count(node.Children.Count - 14)} more…"));
        if (node.Children.Count == 0) stack.Children.Add(Ui.Secondary(node.IsUnreadable ? "BitReaper couldn't read this folder." : "This folder is empty."));
        return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private static UIElement Details(FileNode node)
    {
        var column = Ui.Column(12, Ui.Icon(IconCache.ForPath(node.Path, large: true, directory: false), 96),
                               Ui.Display(node.Name, 18), Ui.Text(IconCache.Kind(node), 13, Theme.TextSecondary),
                               Ui.Display(ByteFormat.Size(node.Size), 26, bold: true),
                               Ui.Secondary("No preview for this kind of file. Use Open to view it in its program.", 12));
        foreach (FrameworkElement child in column.Children) child.HorizontalAlignment = HorizontalAlignment.Center;
        column.VerticalAlignment = VerticalAlignment.Center;
        return column;
    }
}
