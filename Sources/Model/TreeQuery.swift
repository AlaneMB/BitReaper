import Foundation

/// Read-only walks over a scanned tree. Run off the main thread; the session waits for running
/// queries to finish before it mutates the tree.
enum TreeQuery {
    /// The largest files below `root`, biggest first.
    static func largestFiles(in root: FileNode, limit: Int) -> [FileNode] {
        var top: [FileNode] = []   // ascending by size, so top[0] is the smallest kept
        var stack: [FileNode] = [root]
        while let node = stack.popLast() {
            for child in node.children {
                if child.isDirectory {
                    stack.append(child)
                } else if top.count < limit || child.size > top[0].size {
                    let index = insertionIndex(for: child.size, in: top)
                    top.insert(child, at: index)
                    if top.count > limit { top.removeFirst() }
                }
            }
        }
        return top.reversed()
    }

    /// Files and folders below `root` whose name contains `query`, biggest first.
    static func search(_ query: String, in root: FileNode, limit: Int, isCancelled: () -> Bool) -> [FileNode] {
        var matches: [FileNode] = []
        var stack: [FileNode] = [root]
        var visited = 0
        while let node = stack.popLast() {
            for child in node.children {
                if child.name.range(of: query, options: [.caseInsensitive, .diacriticInsensitive]) != nil {
                    matches.append(child)
                }
                if child.isDirectory && !child.children.isEmpty { stack.append(child) }
            }
            visited += 1
            if visited % 4096 == 0 && isCancelled() { return [] }
        }
        matches.sort { $0.size > $1.size }
        return Array(matches.prefix(limit))
    }

    private static func insertionIndex(for size: Int64, in sorted: [FileNode]) -> Int {
        var low = 0
        var high = sorted.count
        while low < high {
            let mid = (low + high) / 2
            if sorted[mid].size < size { low = mid + 1 } else { high = mid }
        }
        return low
    }
}
