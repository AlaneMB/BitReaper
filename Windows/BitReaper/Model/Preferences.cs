using System.Text.Json;
using System.Text.Json.Serialization;

namespace BitReaper.Model;

public enum DeleteMode
{
    RecycleBin,
    Permanent,
}

public static class DeleteModeExtensions
{
    public static string Title(this DeleteMode mode) => mode switch
    {
        DeleteMode.RecycleBin => "Move to Recycle Bin",
        _ => "Delete Immediately",
    };
}

/// <summary>User preferences, saved as JSON in %APPDATA%\BitReaper\settings.json.</summary>
public sealed class Preferences
{
    private sealed class Stored
    {
        public bool DecimalUnits { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public DeleteMode DeleteMode { get; set; } = DeleteMode.RecycleBin;
        public bool ConfirmDelete { get; set; } = true;
        public int CountdownSeconds { get; set; } = 5;
        public bool DedupeHardLinks { get; set; } = true;
        public List<string> StarredFolders { get; set; } = new();
        public List<string> RecentFolders { get; set; } = new();
    }

    private readonly Stored _values;
    private readonly string? _file;

    public event Action? Changed;

    public static string DefaultFile =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BitReaper", "settings.json");

    /// <param name="file">Where to persist; null keeps everything in memory (tests).</param>
    public Preferences(string? file)
    {
        _file = file;
        _values = Load(file) ?? new Stored();
        if (_values.CountdownSeconds < 0) _values.CountdownSeconds = 5;
    }

    /// <summary>1 KB = 1000 bytes (like Explorer's "on disk" on most tools) when true, 1 KiB = 1024 bytes otherwise.</summary>
    public bool DecimalUnits { get => _values.DecimalUnits; set => Update(() => _values.DecimalUnits = value); }
    public DeleteMode DeleteMode { get => _values.DeleteMode; set => Update(() => _values.DeleteMode = value); }
    public bool ConfirmDelete { get => _values.ConfirmDelete; set => Update(() => _values.ConfirmDelete = value); }
    /// <summary>Safety countdown: seconds during which a started deletion can still be cancelled.</summary>
    public int CountdownSeconds { get => _values.CountdownSeconds; set => Update(() => _values.CountdownSeconds = Math.Max(0, value)); }
    public bool DedupeHardLinks { get => _values.DedupeHardLinks; set => Update(() => _values.DedupeHardLinks = value); }
    public IReadOnlyList<string> StarredFolders => _values.StarredFolders;
    public IReadOnlyList<string> RecentFolders => _values.RecentFolders;

    public bool IsStarred(string path) => _values.StarredFolders.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void ToggleStar(string path) => Update(() =>
    {
        int index = _values.StarredFolders.FindIndex(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _values.StarredFolders.RemoveAt(index);
        }
        else
        {
            _values.StarredFolders.Add(path);
            _values.RecentFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
    });

    public void NoteRecent(string path)
    {
        if (IsStarred(path)) return;
        Update(() =>
        {
            _values.RecentFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            _values.RecentFolders.Insert(0, path);
            if (_values.RecentFolders.Count > 6) _values.RecentFolders.RemoveRange(6, _values.RecentFolders.Count - 6);
        });
    }

    public void Forget(string path) => Update(() =>
    {
        _values.RecentFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        _values.StarredFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
    });

    private void Update(Action change)
    {
        change();
        Save();
        Changed?.Invoke();
    }

    private static Stored? Load(string? file)
    {
        if (file == null || !File.Exists(file)) return null;
        try
        {
            return JsonSerializer.Deserialize<Stored>(File.ReadAllText(file));
        }
        catch (Exception)
        {
            return null;   // a damaged settings file falls back to the defaults
        }
    }

    private void Save()
    {
        if (_file == null) return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Preferences are a convenience; never fail an action because they couldn't be written.
        }
    }
}
