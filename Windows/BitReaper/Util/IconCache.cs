using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BitReaper.Model;

namespace BitReaper.Util;

/// <summary>
/// Shell icons. Plain files are looked up per extension without touching the disk (fast); programs,
/// shortcuts and icons have their own picture, so those are looked up per path.
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource> ByKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PerFile = new(StringComparer.OrdinalIgnoreCase) { "exe", "lnk", "ico", "url", "cpl", "msc", "scr" };
    private const int MaxPerFileIcons = 2000;
    private static int _perFileCount;

    public static ImageSource Icon(FileNode node, bool large = false)
    {
        if (node.IsDirectory && node.Parent == null && node.Name.Length <= 3) return ForPath(node.Name, large, directory: true);
        if (node.IsDirectory) return Generic("<folder>", Native.FILE_ATTRIBUTE_DIRECTORY, "folder", large);
        string ext = node.Extension;
        if (PerFile.Contains(ext) && _perFileCount < MaxPerFileIcons) return ForPath(node.Path, large, directory: false);
        return Generic(ext.Length == 0 ? "<file>" : ext, Native.FILE_ATTRIBUTE_NORMAL, ext.Length == 0 ? "file" : "x." + ext, large);
    }

    /// <summary>The icon for a real path (drives, programs).</summary>
    public static ImageSource ForPath(string path, bool large = false, bool directory = true)
    {
        string key = (large ? "L|" : "S|") + path;
        if (ByKey.TryGetValue(key, out var cached)) return cached;
        if (!directory) _perFileCount++;
        var image = Load(path, 0, large ? Native.SHGFI_LARGEICON : Native.SHGFI_SMALLICON)
                    ?? Generic(directory ? "<folder>" : "<file>", directory ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL,
                               directory ? "folder" : "file", large);
        ByKey[key] = image;
        return image;
    }

    private static ImageSource Generic(string key, uint attributes, string probe, bool large)
    {
        string fullKey = (large ? "GL|" : "GS|") + key;
        if (ByKey.TryGetValue(fullKey, out var cached)) return cached;
        var image = Load(probe, attributes, Native.SHGFI_USEFILEATTRIBUTES | (large ? Native.SHGFI_LARGEICON : Native.SHGFI_SMALLICON))
                    ?? Empty;
        ByKey[fullKey] = image;
        return image;
    }

    private static readonly ImageSource Empty = CreateEmpty();

    private static ImageSource CreateEmpty()
    {
        var image = new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        image.Freeze();
        return image;
    }

    private static ImageSource? Load(string path, uint attributes, uint flags)
    {
        var info = new Native.SHFILEINFO();
        IntPtr result = Native.SHGetFileInfo(path, attributes, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), Native.SHGFI_ICON | flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Native.DestroyIcon(info.hIcon);
        }
    }

    /// <summary>Explorer's type name, e.g. "Text Document", "Application", "File folder".</summary>
    public static string Kind(FileNode node)
    {
        if (node.IsLink) return node.IsDirectory ? "Junction or symbolic link" : "Symbolic link";
        if (node.IsDirectory && node.Parent == null && node.Name.Length <= 3) return "Drive";
        if (node.IsDirectory) return "Folder";
        string ext = node.Extension;
        if (Kinds.TryGetValue(ext, out var kind)) return kind;
        var info = new Native.SHFILEINFO();
        Native.SHGetFileInfo(ext.Length == 0 ? "file" : "x." + ext, Native.FILE_ATTRIBUTE_NORMAL, ref info,
                             (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), Native.SHGFI_TYPENAME | Native.SHGFI_USEFILEATTRIBUTES);
        kind = string.IsNullOrWhiteSpace(info.szTypeName) ? (ext.Length == 0 ? "File" : ext.ToUpperInvariant() + " File") : info.szTypeName;
        Kinds[ext] = kind;
        return kind;
    }
}
