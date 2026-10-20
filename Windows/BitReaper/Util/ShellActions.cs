using System.Diagnostics;
using System.Windows;

namespace BitReaper.Util;

/// <summary>Explorer integration: show in Explorer, open, properties, clipboard.</summary>
public static class ShellActions
{
    /// <summary>Opens Explorer windows with the items selected (one window per folder).</summary>
    public static void Reveal(IEnumerable<string> paths)
    {
        foreach (var group in paths.GroupBy(p => System.IO.Path.GetDirectoryName(p) ?? p, StringComparer.OrdinalIgnoreCase).Take(8))
        {
            if (!RevealGroup(group.Key, group.ToList()))
                Start("explorer.exe", $"/select,\"{group.First()}\"");
        }
    }

    private static bool RevealGroup(string folder, List<string> items)
    {
        if (Native.SHParseDisplayName(folder, IntPtr.Zero, out var folderPidl, 0, out _) != 0) return false;
        var itemPidls = new List<IntPtr>();
        try
        {
            foreach (var item in items)
                if (Native.SHParseDisplayName(item, IntPtr.Zero, out var pidl, 0, out _) == 0) itemPidls.Add(pidl);
            if (itemPidls.Count == 0) return false;
            var relative = itemPidls.Select(Native.ILFindLastID).ToArray();
            return Native.SHOpenFolderAndSelectItems(folderPidl, (uint)relative.Length, relative, 0) == 0;
        }
        finally
        {
            foreach (var pidl in itemPidls) Native.ILFree(pidl);
            Native.ILFree(folderPidl);
        }
    }

    /// <summary>Opens a file with its default program, or a folder in Explorer.</summary>
    public static void Open(string path) => Start(path, null);

    public static void ShowProperties(string path, IntPtr owner)
    {
        var info = new Native.SHELLEXECUTEINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHELLEXECUTEINFO>(),
            fMask = Native.SEE_MASK_INVOKEIDLIST,
            hwnd = owner,
            lpVerb = "properties",
            lpFile = path,
            nShow = 1,
        };
        Native.ShellExecuteEx(ref info);
    }

    public static void OpenRecycleBin() => Start("explorer.exe", "shell:RecycleBinFolder");

    public static void CopyPaths(IEnumerable<string> paths)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, paths)); }
        catch (Exception) { /* the clipboard can be locked by another app for a moment */ }
    }

    private static void Start(string file, string? arguments)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = true };
            if (arguments != null) info.Arguments = arguments;
            Process.Start(info);
        }
        catch (Exception)
        {
            // No program associated, or the item vanished: nothing useful to report.
        }
    }
}
