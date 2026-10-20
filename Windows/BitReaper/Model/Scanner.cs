using System.Collections.Concurrent;
using BitReaper.Util;

namespace BitReaper.Model;

public struct ScanStats
{
    public int Files;
    public int Folders;
    public long Bytes;
    public int Unreadable;
    public string CurrentPath;
}

/// <summary>
/// Fast, parallel directory scanner.
///
/// Folders are listed with <c>GetFileInformationByHandleEx(FileIdBothDirectoryInfo)</c>, which returns every
/// entry's allocation size, attributes and file id in bulk (no per-file system call), and a pool of worker
/// threads drains a shared stack of folders. Sizes are physical (allocated clusters, so compressed, sparse and
/// cloud-only files count for what they really take), files with several hard links are counted once, and
/// junctions, symbolic links and mount points are listed but never followed, so nothing is counted twice and
/// other volumes are skipped.
/// </summary>
public sealed unsafe class Scanner
{
    private const int BufferSize = 64 * 1024;

    private readonly object _lock = new();
    private ScanStats _stats = new() { CurrentPath = "" };
    private volatile bool _cancelled;

    private readonly bool _dedupeHardLinks;
    private bool _useFileIds;
    private readonly HashSet<long>[] _seenIds;

    private readonly ConcurrentStack<(FileNode Node, string Path)> _work = new();
    private readonly SemaphoreSlim _available = new(0);
    private int _pending;
    private readonly int _workerCount;

    public Scanner(bool dedupeHardLinks = true)
    {
        _dedupeHardLinks = dedupeHardLinks;
        _seenIds = new HashSet<long>[64];
        for (int i = 0; i < _seenIds.Length; i++) _seenIds[i] = new HashSet<long>();
        _workerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
    }

    public ScanStats Snapshot
    {
        get { lock (_lock) return _stats; }
    }

    public bool IsCancelled => _cancelled;

    public void Cancel()
    {
        _cancelled = true;
        _available.Release(_workerCount);
    }

    /// <summary>Scans <paramref name="path"/> synchronously. Returns null when cancelled or when the path can't be read.</summary>
    /// <param name="rootName">Name for the returned node (defaults to the absolute path, as a tree root needs).</param>
    public FileNode? Scan(string path, string? rootName = null)
    {
        string full = NormalizeRoot(path);
        uint attributes = Native.GetFileAttributes(Native.LongPath(full));
        if (attributes == Native.INVALID_FILE_ATTRIBUTES) return null;

        bool isDirectory = (attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
        var root = new FileNode(rootName ?? full, null, isDirectory ? NodeFlags.Directory : NodeFlags.None);
        if (!isDirectory)
        {
            var info = new FileInfo(full);
            root.Size = info.Length;
            root.Modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
            return root;
        }
        root.Modified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(full)).ToUnixTimeSeconds();
        // Only NTFS and ReFS have stable file ids shared by hard links; FAT and network shares may report zeros.
        _useFileIds = _dedupeHardLinks && FileSystemHasHardLinks(full);

        _pending = 1;
        _work.Push((root, full));
        _available.Release();

        var threads = new Thread[_workerCount];
        for (int i = 0; i < threads.Length; i++)
        {
            threads[i] = new Thread(Worker, 256 * 1024) { IsBackground = true, Name = $"BitReaper scan {i}" };
            threads[i].Start();
        }
        foreach (var thread in threads) thread.Join();

        if (_cancelled) return null;
        root.FinalizeTree();
        return root;
    }

    /// <summary>"C:" → "C:\", trailing separators trimmed elsewhere, so paths are built consistently.</summary>
    public static string NormalizeRoot(string path)
    {
        // "C:" alone means "the current folder on C:" to Win32; here it always means the drive.
        if (path.Length == 2 && path[1] == ':') path += "\\";
        string full = System.IO.Path.GetFullPath(path);
        string? root = System.IO.Path.GetPathRoot(full);
        if (root != null && string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return root.EndsWith('\\') ? root : root + "\\";
        return full.TrimEnd('\\');
    }

    private static bool FileSystemHasHardLinks(string path)
    {
        try
        {
            string? root = System.IO.Path.GetPathRoot(path);
            if (root == null || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            string format = new DriveInfo(root).DriveFormat;
            return format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---- Workers ----

    private void Worker()
    {
        var buffer = new byte[BufferSize];
        while (!_cancelled)
        {
            if (_work.TryPop(out var item))
            {
                ScanDirectory(item.Node, item.Path, buffer);
                if (Interlocked.Decrement(ref _pending) == 0)
                {
                    _available.Release(_workerCount);   // wake everyone up so they can exit
                    return;
                }
                continue;
            }
            if (Volatile.Read(ref _pending) == 0) return;
            _available.Wait(50);
        }
    }

    private void ScanDirectory(FileNode node, string path, byte[] buffer)
    {
        if (_cancelled) return;
        using var handle = Native.CreateFile(Native.LongPath(path), Native.FILE_LIST_DIRECTORY | Native.SYNCHRONIZE,
                                             Native.FILE_SHARE_ALL, IntPtr.Zero, Native.OPEN_EXISTING,
                                             Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            MarkUnreadable(node, path);
            return;
        }

        string prefix = path.EndsWith('\\') ? path : path + "\\";
        var children = new List<FileNode>();
        var subdirectories = new List<(FileNode, string)>();
        int fileCount = 0;
        long bytes = 0;
        bool first = true;

        fixed (byte* start = buffer)
        {
            while (!_cancelled)
            {
                if (!Native.GetFileInformationByHandleEx(handle, Native.FileIdBothDirectoryInfo, start, BufferSize))
                {
                    if (first && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != Native.ERROR_NO_MORE_FILES)
                    {
                        MarkUnreadable(node, path);
                        return;
                    }
                    break;
                }
                first = false;

                int offset = 0;
                while (true)
                {
                    byte* entry = start + offset;
                    // FILE_ID_BOTH_DIR_INFO
                    uint next = *(uint*)entry;
                    long lastWrite = *(long*)(entry + 24);
                    long allocation = *(long*)(entry + 48);
                    uint attributes = *(uint*)(entry + 56);
                    int nameLength = (int)(*(uint*)(entry + 60)) / 2;
                    uint reparseTag = *(uint*)(entry + 64);   // EaSize holds the reparse tag for reparse points
                    long fileId = *(long*)(entry + 96);
                    char* namePointer = (char*)(entry + 104);

                    bool isDot = (nameLength == 1 && namePointer[0] == '.')
                                 || (nameLength == 2 && namePointer[0] == '.' && namePointer[1] == '.');
                    if (!isDot)
                    {
                        string name = new(namePointer, 0, nameLength);
                        bool isReparse = (attributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                        bool isLink = isReparse && Native.IsNameSurrogate(reparseTag);
                        long modified = lastWrite > 0 ? (lastWrite - 116444736000000000L) / 10_000_000 : 0;

                        if ((attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0 && !isLink)
                        {
                            var child = new FileNode(name, node, NodeFlags.Directory) { Modified = modified };
                            children.Add(child);
                            subdirectories.Add((child, prefix + name));
                        }
                        else
                        {
                            long size = isLink ? 0 : allocation;
                            if (size > 0 && _useFileIds && fileId != 0 && !RegisterFileId(fileId)) size = 0;
                            var flags = isLink ? NodeFlags.Link : NodeFlags.None;
                            if (isLink && (attributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0) flags |= NodeFlags.Directory;
                            children.Add(new FileNode(name, node, flags) { Modified = modified, Size = size });
                            fileCount++;
                            bytes += size;
                        }
                    }
                    if (next == 0) break;
                    offset += (int)next;
                }
            }
        }

        children.TrimExcess();
        node.SetChildren(children);

        lock (_lock)
        {
            _stats.Files += fileCount;
            _stats.Folders += subdirectories.Count;
            _stats.Bytes += bytes;
            _stats.CurrentPath = path;
        }

        if (subdirectories.Count == 0) return;
        Interlocked.Add(ref _pending, subdirectories.Count);
        for (int i = subdirectories.Count - 1; i >= 0; i--) _work.Push(subdirectories[i]);
        _available.Release(Math.Min(subdirectories.Count, _workerCount));
    }

    private void MarkUnreadable(FileNode node, string path)
    {
        node.Flags |= NodeFlags.Unreadable;
        lock (_lock)
        {
            _stats.Unreadable++;
            _stats.CurrentPath = path;
        }
    }

    /// <summary>
    /// Returns true the first time a file id is seen. Directory listings don't say how many links a file has,
    /// so every file with data is checked; the set is split in shards to keep lock contention low.
    /// </summary>
    private bool RegisterFileId(long fileId)
    {
        var shard = _seenIds[(int)((ulong)fileId * 0x9E3779B97F4A7C15UL >> 58)];
        lock (shard) return shard.Add(fileId);
    }
}
