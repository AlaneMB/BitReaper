using System.Diagnostics;
using Microsoft.Win32;

namespace BitReaper.Util;

/// <summary>
/// Per-user install, no administrator rights needed: the app copies itself to
/// %LOCALAPPDATA%\Programs\BitReaper, adds a Start menu shortcut, a "Scan with BitReaper" entry on folders
/// and drives in Explorer, and an entry in Settings › Apps so it can be uninstalled like any other app.
/// </summary>
public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\BitReaper";
    private static readonly string[] ContextMenuKeys =
    {
        @"Software\Classes\Directory\shell\BitReaper",
        @"Software\Classes\Directory\Background\shell\BitReaper",
        @"Software\Classes\Drive\shell\BitReaper",
    };

    public static string InstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "BitReaper");

    public static string InstalledExe => Path.Combine(InstallDirectory, "BitReaper.exe");

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "BitReaper.lnk");

    public static bool IsInstalled => File.Exists(InstalledExe) && Registry.CurrentUser.OpenSubKey(UninstallKey) != null;

    public static bool IsRunningInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static void Install()
    {
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the running program.");
        Directory.CreateDirectory(InstallDirectory);
        if (!IsRunningInstalledCopy) File.Copy(source, InstalledExe, overwrite: true);
        string exe = InstalledExe;

        CreateShortcut(ShortcutPath, exe);

        foreach (var key in ContextMenuKeys)
        {
            using var shell = Registry.CurrentUser.CreateSubKey(key);
            shell.SetValue("", "Scan with BitReaper");
            shell.SetValue("Icon", $"\"{exe}\",0");
            using var command = shell.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" \"%V\"");
        }

        var version = typeof(Installer).Assembly.GetName().Version ?? new Version(1, 0);
        using (var uninstall = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            uninstall.SetValue("DisplayName", "BitReaper");
            uninstall.SetValue("DisplayVersion", $"{version.Major}.{version.Minor}.{version.Build}");
            uninstall.SetValue("Publisher", "The BitReaper contributors");
            uninstall.SetValue("DisplayIcon", $"\"{exe}\",0");
            uninstall.SetValue("InstallLocation", InstallDirectory);
            uninstall.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            uninstall.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
            uninstall.SetValue("URLInfoAbout", "https://github.com/Pau-mbox/BitReaper");
            uninstall.SetValue("NoModify", 1, RegistryValueKind.DWord);
            uninstall.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            uninstall.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
    }

    /// <summary>Removes the shortcut, Explorer entries and the program folder. Settings in %APPDATA%\BitReaper are kept.</summary>
    public static void Uninstall()
    {
        try { File.Delete(ShortcutPath); }
        catch (Exception) { /* already gone */ }
        foreach (var key in ContextMenuKeys) Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);

        string folder = Path.GetFullPath(InstallDirectory);
        if (!Directory.Exists(folder) || !folder.EndsWith(@"\Programs\BitReaper", StringComparison.OrdinalIgnoreCase)) return;
        if (IsRunningInstalledCopy)
        {
            // A running program can't delete itself: remove the folder once this process has exited.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & rmdir /s /q \"{folder}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        else
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static void CreateShortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "See what's filling your disk, collect what you don't need, reap it in one go.";
            shortcut.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
