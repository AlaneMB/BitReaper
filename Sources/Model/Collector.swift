import Foundation
import Observation

/// The staging area for deletion. Nothing is touched on disk until the user deletes the collected items.
///
/// The collector never holds an item together with one of its ancestors: collecting a folder absorbs
/// anything already collected inside it, and items inside a collected folder are already covered.
@MainActor
@Observable
final class Collector {
    struct Item: Identifiable {
        let node: FileNode
        let path: String
        var id: ObjectIdentifier { ObjectIdentifier(node) }
    }

    private(set) var items: [Item] = []
    private(set) var version = 0
    @ObservationIgnored private var ids = Set<ObjectIdentifier>()

    var isEmpty: Bool { items.isEmpty }
    var count: Int { items.count }
    var totalSize: Int64 { items.reduce(0) { $0 + $1.node.size } }
    var sortedItems: [Item] { items.sorted { $0.node.size > $1.node.size } }

    // `ids` is a lookup cache; reading `version` makes SwiftUI views observe membership changes.
    func contains(_ node: FileNode) -> Bool {
        _ = version
        return ids.contains(ObjectIdentifier(node))
    }

    func coveringAncestor(of node: FileNode) -> FileNode? {
        _ = version
        var current = node.parent
        while let ancestor = current {
            if ids.contains(ObjectIdentifier(ancestor)) { return ancestor }
            current = ancestor.parent
        }
        return nil
    }

    func isCovered(_ node: FileNode) -> Bool { contains(node) || coveringAncestor(of: node) != nil }

    /// Adds nodes, returning a message for every node that was refused.
    @discardableResult
    func add(_ nodes: [FileNode], root: FileNode) -> [String] {
        var refusals: [String] = []
        var changed = false
        for node in nodes {
            if isCovered(node) { continue }
            let path = node.path
            if node === root {
                refusals.append("“\(node.name == "/" ? "/" : (path as NSString).lastPathComponent)” is the folder you scanned. Collect what's inside it instead.")
                continue
            }
            if let reason = SystemAccess.protectionReason(for: path) {
                refusals.append(reason)
                continue
            }
            let absorbed = items.filter { $0.node.isDescendant(of: node) }
            if !absorbed.isEmpty {
                let absorbedIDs = Set(absorbed.map(\.id))
                items.removeAll { absorbedIDs.contains($0.id) }
                ids.subtract(absorbedIDs)
            }
            items.append(Item(node: node, path: path))
            ids.insert(ObjectIdentifier(node))
            changed = true
        }
        if changed { version += 1 }
        return refusals
    }

    func remove(_ nodes: [FileNode]) {
        let removing = Set(nodes.map(ObjectIdentifier.init))
        guard !removing.isDisjoint(with: ids) else { return }
        items.removeAll { removing.contains($0.id) }
        ids.subtract(removing)
        version += 1
    }

    func clear() {
        guard !items.isEmpty else { return }
        items.removeAll()
        ids.removeAll()
        version += 1
    }

    /// Re-resolves items after part of the tree was rescanned; items that no longer exist are dropped.
    func remap(_ resolve: (String) -> FileNode?) {
        let remapped = items.compactMap { item in resolve(item.path).map { Item(node: $0, path: item.path) } }
        items = remapped
        ids = Set(remapped.map(\.id))
        version += 1
    }
}
