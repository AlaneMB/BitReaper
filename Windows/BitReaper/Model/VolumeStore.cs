namespace BitReaper.Model;

public sealed record VolumeInfo(
    // Root path, e.g. "C:\"
    string Id,
    string Label,
    string Format,
    long Total,
    // Free space available to this user (honours disk quotas)
    long Available,
    // Bytes in this drive's Recycle Bin: used, but reclaimable by emptying it
    long RecycleBin,
    bool IsSystem,
    bool IsRemovable,
    bool IsNetwork)
{
    public long Used => Math.Max(0, Total - Available);

    /// <summary>Explorer-style name: "Local Disk (C:)", "Backup (E:)".</summary>
    public string Name => $"{(string.IsNullOrWhiteSpace(Label) ? DefaultLabel : Label)} ({Id.TrimEnd('\\')})";

    private string DefaultLabel => IsNetwork ? "Network Drive" : IsRemovable ? "USB Drive" : "Local Disk";

    public string Kind => IsNetwork ? "Network" : IsRemovable ? "Removable" : "Internal";
}

/// <summary>Every ready drive with its usage. Refreshed when drives come and go (WM_DEVICECHANGE) and on demand.</summary>
public sealed class VolumeStore
{
    public IReadOnlyList<VolumeInfo> Volumes { get; private set; }
    public event Action? Changed;

    public VolumeStore()
    {
        Volumes = Load();
    }

    public async void Refresh()
    {
        var loaded = await Task.Run(Load);
        if (loaded.SequenceEqual(Volumes)) return;
        Volumes = loaded;
        Changed?.Invoke();
    }

    public VolumeInfo? Volume(string id) =>
        Volumes.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static List<VolumeInfo> Load()
    {
        string systemRoot = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var result = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.NoRootDirectory or DriveType.Unknown) continue;
                if (drive.TotalSize <= 0) continue;
                string id = drive.RootDirectory.FullName;
                result.Add(new VolumeInfo(
                    Id: id,
                    Label: drive.VolumeLabel,
                    Format: drive.DriveFormat,
                    Total: drive.TotalSize,
                    Available: drive.AvailableFreeSpace,
                    RecycleBin: drive.DriveType == DriveType.Network ? 0 : (BitReaper.Model.RecycleBin.Size(id) ?? 0),
                    IsSystem: id.Equals(systemRoot, StringComparison.OrdinalIgnoreCase),
                    IsRemovable: drive.DriveType == DriveType.Removable,
                    IsNetwork: drive.DriveType == DriveType.Network));
            }
            catch (Exception)
            {
                // A drive that disappears or can't be queried is just left out.
            }
        }
        return result.OrderByDescending(v => v.IsSystem)
                     .ThenBy(v => v.IsNetwork)
                     .ThenBy(v => v.IsRemovable)
                     .ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }
}
