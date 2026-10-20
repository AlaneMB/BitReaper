using System.Windows;
using System.Windows.Threading;
using BitReaper.Model;
using BitReaper.Util;
using BitReaper.Views;

namespace BitReaper;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        bool quiet = args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);

        if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
        {
            RunAndExit(Installer.Install, quiet, "BitReaper is installed. Find it in the Start menu, or right-click a folder in Explorer › Scan with BitReaper.");
            return;
        }
        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            if (!quiet && MessageBox.Show("Uninstall BitReaper? Your settings are kept.", "BitReaper",
                                          MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                Shutdown();
                return;
            }
            RunAndExit(Installer.Uninstall, quiet, "BitReaper was uninstalled.");
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        var model = AppModel.Shared;
        var window = new MainWindow(model);
        MainWindow = window;
        window.Show();

        // A folder or drive passed on the command line (Explorer's "Scan with BitReaper", drag onto the icon) is scanned.
        var path = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
        if (path != null) model.Scan(path.Trim('"'));
    }

    private void RunAndExit(Action action, bool quiet, string done)
    {
        try
        {
            action();
            if (!quiet) MessageBox.Show(done, "BitReaper", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
        }
        catch (Exception error)
        {
            if (!quiet) MessageBox.Show(error.Message, "BitReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    /// <summary>Logs the error to %LOCALAPPDATA%\BitReaper\crash.log and keeps the app running.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitReaper");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "crash.log"), $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch (Exception)
        {
            // Logging is best effort.
        }
        MessageBox.Show($"Something went wrong: {e.Exception.Message}", "BitReaper", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
