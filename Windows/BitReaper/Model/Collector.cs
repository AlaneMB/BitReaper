namespace BitReaper.Model;

/// <summary>
/// The staging area for deletion. Nothing is touched on disk until the user deletes the collected items.
///
/// The collector never holds an item together with one of its ancestors: collecting a folder absorbs
/// anything already collected inside it, and items inside a collected folder are already covered.
/// </summary>
public sealed class Collector
{
    public sealed record Item(FileNode Node, string Path);

    private readonly List<Item> _items = new();
    private readonly HashSet<FileNode> _nodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised whenever membership changes.</summary>
    public event Action? Changed;

    public IReadOnlyList<Item> Items => _items;
    public bool IsEmpty => _items.Count == 0;
    public int Count => _items.Count;
    public long TotalSize => _items.Sum(item => item.Node.Size);
    public List<Item> SortedItems => _items.OrderByDescending(item => item.Node.Size).ToList();

    public bool Contains(FileNode node) => _nodes.Contains(node);

    public FileNode? CoveringAncestor(FileNode node)
    {
        for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
            if (_nodes.Contains(ancestor)) return ancestor;
        return null;
    }

    public bool IsCovered(FileNode node) => Contains(node) || CoveringAncestor(node) != null;

    /// <summary>Adds nodes, returning a message for every node that was refused.</summary>
    public List<string> Add(IEnumerable<FileNode> nodes, FileNode root)
    {
        var refusals = new List<string>();
        bool changed = false;
        foreach (var node in nodes)
        {
            if (IsCovered(node)) continue;
            string path = node.Path;
            if (ReferenceEquals(node, root))
            {
                refusals.Add($"“{DisplayName(path)}” is the folder you scanned. Collect what's inside it instead.");
                continue;
            }
            if (SystemAccess.ProtectionReason(path) is { } reason)
            {
                refusals.Add(reason);
                continue;
            }
            foreach (var absorbed in _items.Where(item => item.Node.IsDescendantOf(node)).ToList())
            {
                _items.Remove(absorbed);
                _nodes.Remove(absorbed.Node);
            }
            _items.Add(new Item(node, path));
            _nodes.Add(node);
            changed = true;
        }
        if (changed) Changed?.Invoke();
        return refusals;
    }

    public void Remove(IEnumerable<FileNode> nodes)
    {
        var removing = new HashSet<FileNode>(nodes, ReferenceEqualityComparer.Instance);
        if (!removing.Overlaps(_nodes)) return;
        _items.RemoveAll(item => removing.Contains(item.Node));
        _nodes.ExceptWith(removing);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        _nodes.Clear();
        Changed?.Invoke();
    }

    /// <summary>Re-resolves items after part of the tree was rescanned; items that no longer exist are dropped.</summary>
    public void Remap(Func<string, FileNode?> resolve)
    {
        var remapped = _items.Select(item => resolve(item.Path) is { } node ? new Item(node, item.Path) : null)
                             .OfType<Item>().ToList();
        _items.Clear();
        _items.AddRange(remapped);
        _nodes.Clear();
        foreach (var item in remapped) _nodes.Add(item.Node);
        Changed?.Invoke();
    }

    private static string DisplayName(string path)
    {
        string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
