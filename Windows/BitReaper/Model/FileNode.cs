namespace BitReaper.Model;

[Flags]
public enum NodeFlags : byte
{
    None = 0,
    Directory = 1 << 0,
    Unreadable = 1 << 1,
    /// <summary>Symbolic link or junction: listed, never followed.</summary>
    Link = 1 << 2,
}

/// <summary>
/// One file or folder in a scanned tree.
/// Nodes are kept small because a full-disk scan can create millions of them. The root node's
/// <see cref="Name"/> holds its absolute path; every other node stores only its own name.
/// </summary>
public sealed class FileNode
{
    private static readonly List<FileNode> NoChildren = new(0);

    public string Name { get; }
    public FileNode? Parent { get; internal set; }
    private List<FileNode>? _children;
    /// <summary>Physically allocated bytes (whole subtree for folders).</summary>
    public long Size { get; internal set; }
    /// <summary>Number of descendants (files and folders). Zero for files.</summary>
    public int ItemCount { get; internal set; }
    /// <summary>Last write time, seconds since 1970 (UTC). Zero when unknown.</summary>
    public long Modified { get; internal set; }
    public NodeFlags Flags { get; internal set; }

    public FileNode(string name, FileNode? parent, NodeFlags flags)
    {
        Name = name;
        Parent = parent;
        Flags = flags;
    }

    public IReadOnlyList<FileNode> Children => _children ?? NoChildren;
    internal void SetChildren(List<FileNode> children) => _children = children.Count == 0 ? null : children;

    public bool IsDirectory => (Flags & NodeFlags.Directory) != 0;
    public bool IsUnreadable => (Flags & NodeFlags.Unreadable) != 0;
    public bool IsLink => (Flags & NodeFlags.Link) != 0;
    public bool HasChildren => _children is { Count: > 0 };
    public DateTime ModifiedLocal => DateTimeOffset.FromUnixTimeSeconds(Modified).LocalDateTime;

    public string Extension
    {
        get
        {
            int dot = Name.LastIndexOf('.');
            return dot > 0 && dot < Name.Length - 1 ? Name[(dot + 1)..].ToLowerInvariant() : "";
        }
    }

    public string Path
    {
        get
        {
            var components = new List<string>();
            for (var node = this; node != null; node = node.Parent) components.Add(node.Name);
            var result = new System.Text.StringBuilder(components[^1]);
            for (int i = components.Count - 2; i >= 0; i--)
            {
                if (result.Length == 0 || result[^1] != '\\') result.Append('\\');
                result.Append(components[i]);
            }
            return result.ToString();
        }
    }

    public bool IsDescendantOf(FileNode other)
    {
        for (var node = Parent; node != null; node = node.Parent)
            if (ReferenceEquals(node, other)) return true;
        return false;
    }

    public FileNode TopmostAncestor
    {
        get
        {
            var node = this;
            while (node.Parent != null) node = node.Parent;
            return node;
        }
    }

    // ---- Tree building ----

    private static int CompareBySizeDescending(FileNode a, FileNode b) => b.Size.CompareTo(a.Size);

    /// <summary>
    /// Computes folder totals bottom-up and sorts every folder's children by size (largest first).
    /// Iterative, so very deep trees can't overflow the stack.
    /// </summary>
    public void FinalizeTree()
    {
        if (!IsDirectory) return;
        var stack = new Stack<(FileNode Node, bool Visited)>();
        stack.Push((this, false));
        while (stack.Count > 0)
        {
            var (node, visited) = stack.Pop();
            if (!visited)
            {
                stack.Push((node, true));
                foreach (var child in node.Children)
                    if (child.IsDirectory) stack.Push((child, false));
                continue;
            }
            long total = node.Size;
            int count = 0;
            foreach (var child in node.Children)
            {
                total += child.Size;
                count += child.ItemCount + 1;
            }
            node.Size = total;
            node.ItemCount = count;
            node._children?.Sort(CompareBySizeDescending);
        }
    }

    // ---- Mutation (UI thread only, after a scan finished) ----

    /// <summary>Removes this node from its parent and subtracts its size from every ancestor.</summary>
    public void DetachFromParent()
    {
        var parent = Parent;
        if (parent == null) return;
        parent._children?.Remove(this);
        if (parent._children is { Count: 0 }) parent._children = null;
        parent.Propagate(-Size, -(ItemCount + 1));
        Parent = null;
    }

    /// <summary>Inserts <paramref name="child"/> and adds its size to this node and every ancestor.</summary>
    public void Attach(FileNode child)
    {
        child.Parent = this;
        _children ??= new List<FileNode>();
        _children.Add(child);
        _children.Sort(CompareBySizeDescending);
        Propagate(child.Size, child.ItemCount + 1);
    }

    /// <summary>Swaps <paramref name="old"/> for <paramref name="replacement"/> (rescans) and fixes every ancestor's totals.</summary>
    public void ReplaceChild(FileNode old, FileNode replacement)
    {
        if (_children == null) return;
        int index = _children.IndexOf(old);
        if (index < 0) return;
        _children[index] = replacement;
        replacement.Parent = this;
        old.Parent = null;
        _children.Sort(CompareBySizeDescending);
        Propagate(replacement.Size - old.Size, replacement.ItemCount - old.ItemCount);
    }

    private void Propagate(long sizeDelta, int itemDelta)
    {
        for (var node = this; node != null; node = node.Parent)
        {
            node.Size += sizeDelta;
            node.ItemCount += itemDelta;
            node.Parent?._children?.Sort(CompareBySizeDescending);
        }
    }

    public override string ToString() => Name;
}
