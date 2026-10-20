using System.Globalization;

namespace BitReaper.Model;

/// <summary>
/// Read-only walks over a scanned tree. Run off the UI thread; the session waits for running
/// queries to finish before it mutates the tree.
/// </summary>
public static class TreeQuery
{
    /// <summary>The largest files below <paramref name="root"/>, biggest first.</summary>
    public static List<FileNode> LargestFiles(FileNode root, int limit, CancellationToken token = default)
    {
        var top = new List<FileNode>(limit + 1);   // ascending by size, so top[0] is the smallest kept
        var stack = new Stack<FileNode>();
        stack.Push(root);
        int visited = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                if (child.IsDirectory)
                {
                    if (child.HasChildren) stack.Push(child);
                }
                else if (top.Count < limit || child.Size > top[0].Size)
                {
                    top.Insert(InsertionIndex(child.Size, top), child);
                    if (top.Count > limit) top.RemoveAt(0);
                }
            }
            if (++visited % 4096 == 0) token.ThrowIfCancellationRequested();
        }
        top.Reverse();
        return top;
    }

    /// <summary>Files and folders below <paramref name="root"/> whose name contains <paramref name="query"/>, biggest first.</summary>
    public static List<FileNode> Search(string query, FileNode root, int limit, CancellationToken token = default)
    {
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        var matches = new List<FileNode>();
        var stack = new Stack<FileNode>();
        stack.Push(root);
        int visited = 0;
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.Children)
            {
                if (compare.IndexOf(child.Name, query, options) >= 0) matches.Add(child);
                if (child.IsDirectory && child.HasChildren) stack.Push(child);
            }
            if (++visited % 4096 == 0) token.ThrowIfCancellationRequested();
        }
        matches.Sort((a, b) => b.Size.CompareTo(a.Size));
        return matches.Count > limit ? matches.GetRange(0, limit) : matches;
    }

    private static int InsertionIndex(long size, List<FileNode> sorted)
    {
        int low = 0, high = sorted.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (sorted[mid].Size < size) low = mid + 1; else high = mid;
        }
        return low;
    }
}
