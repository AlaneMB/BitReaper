using BitReaper.Util;

namespace BitReaper.Model;

public sealed class ScanJob
{
    public ScanTarget Target { get; }
    public long? ExpectedBytes { get; }
    public DateTime StartDate { get; } = DateTime.Now;
    public Scanner Scanner { get; }

    public ScanJob(ScanTarget target, long? expectedBytes, bool dedupeHardLinks)
    {
        Target = target;
        ExpectedBytes = expectedBytes;
        Scanner = new Scanner(dedupeHardLinks);
    }

    public ScanStats Stats => Scanner.Snapshot;

    public double? Fraction(ScanStats stats) =>
        ExpectedBytes is > 0 ? Math.Min(0.99, (double)stats.Bytes / ExpectedBytes.Value) : null;

    public Task<FileNode?> Start()
    {
        var completion = new TaskCompletionSource<FileNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(Scanner.Scan(Target.Path)); }
            catch (Exception) { completion.SetResult(null); }
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "BitReaper scan" };
        thread.Start();
        return completion.Task;
    }

    public void Cancel() => Scanner.Cancel();
}

public enum Screen
{
    Home,
    Scanning,
    Results,
}

public sealed class AppModel
{
    public static AppModel Shared { get; } = new(new Preferences(Preferences.DefaultFile));

    public Preferences Prefs { get; }
    public VolumeStore Volumes { get; } = new();
    public Screen Screen { get; private set; } = Screen.Home;
    public ScanSession? Session { get; private set; }
    public ScanJob? ScanJob { get; private set; }
    public bool IsElevated { get; } = SystemAccess.IsElevated;
    /// <summary>Main window handle, parent of the dialogs Windows shows while deleting.</summary>
    public IntPtr OwnerWindow { get; set; }

    /// <summary>Screen changes, new sessions and scan jobs.</summary>
    public event Action? Changed;
    public event Action<string>? Error;

    public AppModel(Preferences prefs)
    {
        Prefs = prefs;
        ByteFormat.DecimalUnits = prefs.DecimalUnits;
        prefs.Changed += () => ByteFormat.DecimalUnits = prefs.DecimalUnits;
    }

    // ---- Scanning ----

    public void Scan(VolumeInfo volume) =>
        Start(new ScanTarget(volume.Id, volume.Name, volume.Id), volume.Used);

    public void Scan(string path)
    {
        string full = Scanner.NormalizeRoot(path);
        if ((Volumes.Volume(full) ?? VolumeStore.Load().FirstOrDefault(v => v.Id.Equals(full, StringComparison.OrdinalIgnoreCase))) is { } volume)
        {
            Scan(volume);
            return;
        }
        if (!Directory.Exists(full))
        {
            Error?.Invoke($"“{System.IO.Path.GetFileName(full)}” isn't a folder. Drop a folder or a drive to scan it.");
            return;
        }
        Prefs.NoteRecent(full);
        Start(new ScanTarget(full, DisplayName(full), null), null);
    }

    public static string DisplayName(string path)
    {
        string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private async void Start(ScanTarget target, long? expectedBytes)
    {
        ScanJob?.Cancel();
        var job = new ScanJob(target, expectedBytes, Prefs.DedupeHardLinks);
        ScanJob = job;
        Screen = Screen.Scanning;
        Changed?.Invoke();

        var root = await job.Start();
        if (!ReferenceEquals(ScanJob, job)) return;
        ScanJob = null;
        if (root == null)
        {
            if (!job.Scanner.IsCancelled) Error?.Invoke($"BitReaper couldn't read “{target.DisplayName}”.");
            Screen = Screen.Home;
            Changed?.Invoke();
            return;
        }
        Session = new ScanSession(this, root, target, job.Stats.Unreadable, DateTime.Now - job.StartDate);
        Screen = Screen.Results;
        Changed?.Invoke();
        Volumes.Refresh();
    }

    public void CancelScan() => ScanJob?.Cancel();

    // ---- Navigation ----

    public void GoHome()
    {
        if (Screen == Screen.Scanning) return;
        Screen = Screen.Home;
        Changed?.Invoke();
        Volumes.Refresh();
    }

    public void ShowResults()
    {
        if (Session == null) return;
        Screen = Screen.Results;
        Changed?.Invoke();
    }

    /// <summary>The active session when the results screen is showing (used by keyboard commands).</summary>
    public ScanSession? ActiveSession => Screen == Screen.Results ? Session : null;
}
