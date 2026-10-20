using System.Diagnostics;
using System.Security.Principal;
using BitReaper.Util;

namespace BitReaper.Model;

public static class SystemAccess
{
    /// <summary>
    /// Elevated processes can read the folders Windows keeps from regular users (other profiles,
    /// System Volume Information, parts of Windows and ProgramData). It's the Windows counterpart of
    /// macOS Full Disk Access.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Starts an elevated copy of the app (UAC prompt). Returns false when the user declined.</summary>
    public static bool RestartElevated(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static readonly Lazy<Rules> ProtectionRules = new(() => new Rules());

    /// <summary>Returns a user-facing reason when <paramref name="path"/> must never be collected for deletion.</summary>
    public static string? ProtectionReason(string path)
    {
        string normalized = Normalize(path);
        string name = System.IO.Path.GetFileName(normalized);
        if (string.IsNullOrEmpty(name)) name = normalized;
        var rules = ProtectionRules.Value;

        string? root = System.IO.Path.GetPathRoot(normalized);
        if (root != null && Normalize(root).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            return $"“{normalized}” is a disk. Open it and collect what's inside instead.";
        if (rules.Exact.Contains(normalized))
            return $"“{name}” is an essential folder and can't be collected. Open it and collect what's inside instead.";
        string? parent = System.IO.Path.GetDirectoryName(normalized);
        if (parent != null && rules.UsersFolder != null && parent.Equals(rules.UsersFolder, StringComparison.OrdinalIgnoreCase))
            return $"“{name}” is a user's profile folder and can't be collected.";
        if (rules.WindowsFolder != null && IsInside(normalized, rules.WindowsFolder))
            return $"“{name}” is part of Windows and can't be collected.";
        if (parent != null && root != null && Normalize(root).Equals(parent, StringComparison.OrdinalIgnoreCase)
            && Rules.DriveSystemItems.Contains(name))
            return $"“{name}” is managed by Windows and can't be collected.";
        return null;
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>Full path without a trailing separator, except for drive roots ("C:\").</summary>
    internal static string Normalize(string path)
    {
        string full;
        if (path.Length == 2 && path[1] == ':') path += "\\";
        try { full = System.IO.Path.GetFullPath(path); }
        catch (Exception) { full = path; }
        string? root = System.IO.Path.GetPathRoot(full);
        string trimmed = full.TrimEnd('\\');
        if (root != null && trimmed.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return root.EndsWith('\\') ? root : root + "\\";
        return trimmed;
    }

    private sealed class Rules
    {
        public readonly HashSet<string> Exact = new(StringComparer.OrdinalIgnoreCase);
        public readonly string? WindowsFolder;
        public readonly string? UsersFolder;

        /// <summary>Items at the top of any drive that belong to Windows (boot, paging, restore points, Recycle Bin).</summary>
        public static readonly HashSet<string> DriveSystemItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "$Recycle.Bin", "System Volume Information", "Recovery", "Boot", "EFI", "bootmgr", "BOOTNXT",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp",
            "Config.Msi", "Documents and Settings", "$WinREAgent", "$SysReset",
        };

        public Rules()
        {
            WindowsFolder = Clean(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string? home = Clean(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            UsersFolder = home != null ? System.IO.Path.GetDirectoryName(home) : null;

            Add(WindowsFolder);
            Add(UsersFolder);
            Add(home);
            if (UsersFolder != null)
            {
                Add(System.IO.Path.Combine(UsersFolder, "Public"));
                Add(System.IO.Path.Combine(UsersFolder, "Default"));
                Add(System.IO.Path.Combine(UsersFolder, "All Users"));
            }

            foreach (var folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                         Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
                         Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.CommonDocuments,
                         Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.CommonStartMenu,
                         Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.CommonStartup,
                         Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
                         Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
                         Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyVideos,
                         Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.Favorites,
                         Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.Programs,
                         Environment.SpecialFolder.Startup, Environment.SpecialFolder.Fonts,
                         Environment.SpecialFolder.SendTo, Environment.SpecialFolder.Templates,
                     })
            {
                Add(Environment.GetFolderPath(folder));
            }
            Add(Native.KnownFolder(Native.FOLDERID_Downloads));

            if (home != null)
            {
                foreach (var relative in new[]
                         {
                             "Desktop", "Documents", "Downloads", "Pictures", "Videos", "Music", "Favorites", "Links",
                             "Contacts", "Saved Games", "Searches", "3D Objects", "OneDrive", "AppData",
                             @"AppData\Local", @"AppData\LocalLow", @"AppData\Roaming", @"AppData\Local\Temp",
                             @"AppData\Local\Microsoft", @"AppData\Roaming\Microsoft", @"AppData\Local\Packages",
                             @"AppData\Local\Programs", @"AppData\Roaming\Microsoft\Windows",
                         })
                {
                    Add(System.IO.Path.Combine(home, relative));
                }
            }
            foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                Add(Environment.GetEnvironmentVariable(variable));
        }

        private void Add(string? path)
        {
            if (Clean(path) is { } clean) Exact.Add(clean);
        }

        private static string? Clean(string? path) => string.IsNullOrWhiteSpace(path) ? null : Normalize(path);
    }
}
