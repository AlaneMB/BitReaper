import Foundation

/// One file or folder in a scanned tree.
///
/// Nodes are kept as small as possible because a full-disk scan can create millions of them.
/// The root node's `name` holds its absolute path; every other node stores only its own name.
final class FileNode {
    struct Flags: OptionSet {
        let rawValue: UInt8
        static let directory  = Flags(rawValue: 1 << 0)
        static let unreadable = Flags(rawValue: 1 << 1)
        static let symlink    = Flags(rawValue: 1 << 2)
    }

    let name: String
    weak var parent: FileNode?
    var children: [FileNode] = []
    /// Physically allocated bytes (whole subtree for folders).
    var size: Int64 = 0
    /// Number of descendants (files and folders). Zero for files.
    var itemCount: Int = 0
    /// Modification time, seconds since 1970.
    var modified: Int = 0
    var flags: Flags

    init(name: String, parent: FileNode?, flags: Flags) {
        self.name = name
        self.parent = parent
        self.flags = flags
    }

    var isDirectory: Bool { flags.contains(.directory) }
    var isUnreadable: Bool { flags.contains(.unreadable) }
    var modificationDate: Date { Date(timeIntervalSince1970: TimeInterval(modified)) }

    var path: String {
        var components: [String] = []
        var node: FileNode? = self
        while let current = node {
            components.append(current.name)
            node = current.parent
        }
        var result = components.removeLast()
        for component in components.reversed() {
            if !result.hasSuffix("/") { result += "/" }
            result += component
        }
        return result
    }

    var url: URL { URL(fileURLWithPath: path, isDirectory: isDirectory) }

    var fileExtension: String { (name as NSString).pathExtension.lowercased() }

    func isDescendant(of other: FileNode) -> Bool {
        var node = parent
        while let current = node {
            if current === other { return true }
            node = current.parent
        }
        return false
    }

    var topmostAncestor: FileNode {
        var node = self
        while let parent = node.parent { node = parent }
        return node
    }

    // MARK: - Tree building

    /// Computes folder totals bottom-up and sorts every folder's children by size (largest first).
    func finalizeTree() {
        guard isDirectory else { return }
        var total = size
        var count = 0
        for child in children {
            child.finalizeTree()
            total += child.size
            count += child.itemCount + 1
        }
        size = total
        itemCount = count
        children.sort { $0.size > $1.size }
    }

    // MARK: - Mutation (main thread only, after a scan finished)

    /// Removes this node from its parent and subtracts its size from every ancestor.
    func detachFromParent() {
        guard let parent else { return }
        if let index = parent.children.firstIndex(where: { $0 === self }) {
            parent.children.remove(at: index)
        }
        parent.propagate(sizeDelta: -size, itemDelta: -(itemCount + 1))
        self.parent = nil
    }

    /// Inserts `child` and adds its size to this node and every ancestor.
    func attach(_ child: FileNode) {
        child.parent = self
        children.append(child)
        children.sort { $0.size > $1.size }
        propagate(sizeDelta: child.size, itemDelta: child.itemCount + 1)
    }

    /// Swaps `old` for `new` (used by rescans) and fixes up the totals of every ancestor.
    func replaceChild(_ old: FileNode, with new: FileNode) {
        guard let index = children.firstIndex(where: { $0 === old }) else { return }
        children[index] = new
        new.parent = self
        old.parent = nil
        children.sort { $0.size > $1.size }
        propagate(sizeDelta: new.size - old.size, itemDelta: new.itemCount - old.itemCount)
    }

    private func propagate(sizeDelta: Int64, itemDelta: Int) {
        var node: FileNode? = self
        while let current = node {
            current.size += sizeDelta
            current.itemCount += itemDelta
            current.parent?.children.sort { $0.size > $1.size }
            node = current.parent
        }
    }
}
