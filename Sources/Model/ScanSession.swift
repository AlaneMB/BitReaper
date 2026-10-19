import AppKit
import Observation

struct DeletionFailure: Identifiable, Hashable {
    let id = UUID()
    let path: String
    let reason: String
}

struct FailureReport: Identifiable {
    let id = UUID()
    let failures: [DeletionFailure]
}

struct DeletionOutcome: Identifiable {
    struct TrashRecord {
        let node: FileNode
        let parentPath: String
        let originalPath: String
        let trashURL: URL
    }

    let id = UUID()
    let mode: DeleteMode
    let count: Int
    let bytes: Int64
    let failureCount: Int
    let trashRecords: [TrashRecord]

    var canUndo: Bool { mode == .trash && !trashRecords.isEmpty }
}

/// Cancellation flag shared with a background tree query.
final class QueryToken: @unchecked Sendable {
    private let lock = NSLock()
    private var cancelled = false

    var isCancelled: Bool {
        lock.lock(); defer { lock.unlock() }
        return cancelled
    }

    func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }
}

/// The results of one scan: the tree, where the user is in it, and the collector.
@MainActor
@Observable
final class ScanSession {
    enum ViewMode: String, CaseIterable, Identifiable {
        case tree = "Tree"
        case largest = "Largest Files"
        var id: String { rawValue }
    }

    enum DeletionPhase: Equatable {
        case idle
        case countdown(Int)
        case deleting(done: Int, total: Int)
    }

    private(set) var root: FileNode
    let target: ScanTarget
    let scanDate = Date()
    let scanDuration: TimeInterval
    private(set) var unreadableCount: Int
    let collector = Collector()

    // Navigation
    private(set) var focus: FileNode
    @ObservationIgnored private var backStack: [FileNode] = []
    @ObservationIgnored private var forwardStack: [FileNode] = []
    private(set) var historyVersion = 0
    var selection: [FileNode] = []
    private(set) var treeVersion = 0
    private(set) var colorSlots: [ObjectIdentifier: Int] = [:]
    private(set) var revealTarget: FileNode?
    private(set) var revealVersion = 0

    // Flat lists (search results / largest files)
    var viewMode: ViewMode = .tree { didSet { if oldValue != viewMode { scheduleQuery() } } }
    var searchText = "" { didSet { if oldValue != searchText { scheduleQuery(debounce: true) } } }
    /// Bumped by ⌘F; the search field takes focus when it changes.
    private(set) var searchFocusRequest = 0
    private(set) var flatResults: [FileNode]?
    private(set) var flatVersion = 0
    private(set) var isQuerying = false
    @ObservationIgnored private var queryTasks: [Task<Void, Never>] = []
    @ObservationIgnored private var currentQuery: QueryToken?

    // Rescanning
    private(set) var isRescanning = false

    // Collector & deletion
    var collectorExpanded = false
    var showDeleteSheet = false
    private(set) var deletionPhase: DeletionPhase = .idle
    private(set) var lastOutcome: DeletionOutcome?
    var failureReport: FailureReport?
    @ObservationIgnored private var countdownTask: Task<Void, Never>?

    // Messages
    private(set) var toast: String?
    @ObservationIgnored private var toastTask: Task<Void, Never>?

    // Volume extras
    private(set) var snapshots: [String]?

    init(root: FileNode, target: ScanTarget, unreadableCount: Int, duration: TimeInterval) {
        self.root = root
        self.focus = root
        self.target = target
        self.unreadableCount = unreadableCount
        self.scanDuration = duration
        recomputeColors()
        if let volumeID = target.volumeID {
            Task { [weak self] in
                let snapshots = await SystemAccess.localSnapshots(onVolume: volumeID)
                self?.snapshots = snapshots
            }
        }
    }

    // MARK: - Names & lookup

    func displayName(of node: FileNode) -> String {
        node === root ? target.displayName : node.name
    }

    var volume: VolumeInfo? {
        target.volumeID.flatMap { AppModel.shared.volumes.volume(id: $0) }
    }

    /// Space the volume reports as used but the scan couldn't see (other APFS volumes, snapshots,
    /// folders without permission).
    var hiddenSpace: Int64? {
        guard let volume else { return nil }
        return max(0, volume.used(purgeableAsFree: true) - root.size)
    }

    func isAttached(_ node: FileNode) -> Bool { node.topmostAncestor === root }

    func node(forPath path: String) -> FileNode? {
        let rootPath = root.name
        if path == rootPath { return root }
        let prefix = rootPath.hasSuffix("/") ? rootPath : rootPath + "/"
        guard path.hasPrefix(prefix) else { return nil }
        var node = root
        for component in path.dropFirst(prefix.count).split(separator: "/") {
            guard let child = node.children.first(where: { $0.name == component }) else { return nil }
            node = child
        }
        return node
    }

    /// Path of `node` relative to the current folder, e.g. "Library/Caches".
    func relativePath(of node: FileNode?) -> String {
        guard let node, node !== focus else { return "" }
        var components: [String] = []
        var current: FileNode? = node
        while let c = current, c !== focus {
            components.append(displayName(of: c))
            current = c.parent
        }
        return components.reversed().joined(separator: "/")
    }

    /// The colour slot of the top-level item (relative to the current folder) that contains `node`.
    func colorInfo(for node: FileNode) -> (slot: Int, depth: Int)? {
        var depth = 0
        var current = node
        while let parent = current.parent, parent !== focus {
            current = parent
            depth += 1
        }
        guard current.parent === focus, let slot = colorSlots[ObjectIdentifier(current)] else { return nil }
        return (slot, depth)
    }

    func color(for node: FileNode) -> NSColor {
        let info = colorInfo(for: node)
        return Palette.nsColor(slot: info?.slot, depth: info?.depth ?? 0)
    }

    private func recomputeColors() {
        var result: [ObjectIdentifier: Int] = [:]
        for (index, child) in focus.children.enumerated() {
            result[ObjectIdentifier(child)] = index
        }
        colorSlots = result
    }

    // MARK: - Navigation

    var breadcrumb: [FileNode] {
        var nodes: [FileNode] = []
        var current: FileNode? = focus
        while let node = current {
            nodes.append(node)
            current = node.parent
        }
        return nodes.reversed()
    }

    var canGoBack: Bool { _ = historyVersion; return !backStack.isEmpty }
    var canGoForward: Bool { _ = historyVersion; return !forwardStack.isEmpty }
    var canGoUp: Bool { focus.parent != nil }

    func navigate(to node: FileNode) {
        guard node.isDirectory, node !== focus, isAttached(node) else { return }
        backStack.append(focus)
        forwardStack.removeAll()
        setFocus(node)
    }

    func goBack() {
        while let previous = backStack.popLast() {
            guard isAttached(previous) else { continue }
            forwardStack.append(focus)
            setFocus(previous)
            return
        }
        historyVersion += 1
    }

    func goForward() {
        while let next = forwardStack.popLast() {
            guard isAttached(next) else { continue }
            backStack.append(focus)
            setFocus(next)
            return
        }
        historyVersion += 1
    }

    func goUp() {
        guard let parent = focus.parent else { return }
        let child = focus
        navigate(to: parent)
        reveal(child)
    }

    func zoomIntoSelection() {
        if let folder = selection.first(where: \.isDirectory) { navigate(to: folder) }
    }

    private func setFocus(_ node: FileNode) {
        focus = node
        selection = []
        historyVersion += 1
        recomputeColors()
        if flatResults != nil || viewMode != .tree { scheduleQuery() }
    }

    /// Selects `node` in the tree, expanding its ancestors.
    func reveal(_ node: FileNode) {
        revealTarget = node
        revealVersion += 1
    }

    /// From a flat list (search / largest files), go back to the tree with `node` selected.
    func showInTree(_ node: FileNode) {
        guard isAttached(node) else { return }
        if !node.isDescendant(of: focus) {
            let folder = node.parent ?? root
            if folder !== focus {
                backStack.append(focus)
                forwardStack.removeAll()
                focus = folder
                historyVersion += 1
                recomputeColors()
            }
        }
        searchText = ""
        viewMode = .tree
        flatResults = nil
        flatVersion += 1
        reveal(node)
    }

    // MARK: - Search & largest files

    func requestSearchFocus() { searchFocusRequest += 1 }

    func scheduleQuery(debounce: Bool = false) {
        currentQuery?.cancel()
        let query = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        if query.isEmpty && viewMode == .tree {
            currentQuery = nil
            if flatResults != nil {
                flatResults = nil
                flatVersion += 1
            }
            isQuerying = false
            return
        }
        isQuerying = true
        let token = QueryToken()
        currentQuery = token
        let scope = focus
        let task = Task { [weak self] in
            if debounce {
                try? await Task.sleep(for: .milliseconds(220))
            }
            guard !token.isCancelled else { return }
            let results = await Task.detached(priority: .userInitiated) { () -> [FileNode] in
                if query.isEmpty {
                    return TreeQuery.largestFiles(in: scope, limit: 500)
                }
                return TreeQuery.search(query, in: scope, limit: 2000) { token.isCancelled }
            }.value
            guard let self, !token.isCancelled else { return }
            self.flatResults = results
            self.flatVersion += 1
            self.isQuerying = false
        }
        queryTasks.append(task)
    }

    /// Waits until no background query is reading the tree, so it can be mutated safely.
    private func quiesce() async {
        currentQuery?.cancel()
        currentQuery = nil
        let tasks = queryTasks
        queryTasks.removeAll()
        for task in tasks { await task.value }
        isQuerying = false
    }

    // MARK: - Collecting

    func collect(_ nodes: [FileNode]) {
        let refusals = collector.add(nodes, root: root)
        if let first = refusals.first {
            showToast(refusals.count > 1 ? "\(first) (+\(refusals.count - 1) more)" : first)
        }
    }

    /// Adds the nodes, or removes them when they're all already collected.
    func toggleCollect(_ nodes: [FileNode]) {
        guard !nodes.isEmpty, deletionPhase == .idle else { return }
        if nodes.allSatisfy({ collector.contains($0) }) {
            collector.remove(nodes)
        } else {
            let alreadyCovered = nodes.filter { collector.coveringAncestor(of: $0) != nil }
            if alreadyCovered.count == nodes.count, let ancestor = collector.coveringAncestor(of: nodes[0]) {
                showToast("Already collected as part of “\(displayName(of: ancestor))”.")
                return
            }
            collect(nodes.filter { !collector.contains($0) })
        }
    }

    func collect(urls: [URL]) {
        var nodes: [FileNode] = []
        var missing = 0
        for url in urls {
            if let node = node(forPath: Scanner.realPath(url.path)) ?? node(forPath: url.path) {
                nodes.append(node)
            } else {
                missing += 1
            }
        }
        if !nodes.isEmpty { collect(nodes) }
        if missing > 0 && nodes.isEmpty {
            showToast("Only items inside “\(target.displayName)” can be collected.")
        }
    }

    var selectionIsCollected: Bool {
        !selection.isEmpty && selection.allSatisfy { collector.contains($0) }
    }

    // MARK: - Deleting

    func requestDelete() {
        guard !collector.isEmpty, deletionPhase == .idle else { return }
        if AppModel.shared.prefs.confirmDelete {
            showDeleteSheet = true
        } else {
            beginCountdown(mode: AppModel.shared.prefs.deleteMode)
        }
    }

    func confirmDelete(mode: DeleteMode) {
        showDeleteSheet = false
        beginCountdown(mode: mode)
    }

    private func beginCountdown(mode: DeleteMode) {
        lastOutcome = nil
        collectorExpanded = false
        let seconds = AppModel.shared.prefs.countdownSeconds
        countdownTask?.cancel()
        countdownTask = Task { [weak self] in
            var remaining = seconds
            while remaining > 0 {
                guard let self, !Task.isCancelled else { return }
                self.deletionPhase = .countdown(remaining)
                try? await Task.sleep(for: .seconds(1))
                remaining -= 1
            }
            guard let self, !Task.isCancelled else { return }
            await self.performDeletion(mode: mode)
        }
    }

    func cancelCountdown() {
        guard case .countdown = deletionPhase else { return }
        countdownTask?.cancel()
        countdownTask = nil
        deletionPhase = .idle
    }

    private func performDeletion(mode: DeleteMode) async {
        let items = collector.items
        guard !items.isEmpty else {
            deletionPhase = .idle
            return
        }
        let paths = items.map(\.path)
        let total = paths.count
        deletionPhase = .deleting(done: 0, total: total)
        let progress: @Sendable (Int) -> Void = { done in
            Task { @MainActor [weak self] in
                guard let self, case .deleting = self.deletionPhase else { return }
                self.deletionPhase = .deleting(done: done, total: total)
            }
        }
        let results = await Task.detached(priority: .userInitiated) {
            Deleter.delete(paths: paths, mode: mode, progress: progress)
        }.value
        await quiesce()

        var failures: [DeletionFailure] = []
        var records: [DeletionOutcome.TrashRecord] = []
        var removedNodes: [FileNode] = []
        var bytes: Int64 = 0
        for (item, result) in zip(items, results) {
            switch result {
            case .failed(let reason):
                failures.append(DeletionFailure(path: item.path, reason: reason))
            case .removed, .trashed:
                let parentPath = item.node.parent?.path ?? ""
                bytes += item.node.size
                item.node.detachFromParent()
                removedNodes.append(item.node)
                if case .trashed(let url?) = result {
                    records.append(.init(node: item.node, parentPath: parentPath, originalPath: item.path, trashURL: url))
                }
            }
        }
        collector.remove(removedNodes)
        deletionPhase = .idle
        lastOutcome = DeletionOutcome(mode: mode, count: removedNodes.count, bytes: bytes,
                                      failureCount: failures.count, trashRecords: records)
        if !failures.isEmpty {
            failureReport = FailureReport(failures: failures)
        }
        treeDidChange()
        AppModel.shared.volumes.refresh()
    }

    /// Puts the items of the last "Move to Trash" back where they were.
    func undoLastDeletion() {
        guard let outcome = lastOutcome, outcome.canUndo, deletionPhase == .idle else { return }
        var restored = 0
        var failures: [DeletionFailure] = []
        for record in outcome.trashRecords.reversed() {
            do {
                try FileManager.default.moveItem(at: record.trashURL, to: URL(fileURLWithPath: record.originalPath))
                if let parent = node(forPath: record.parentPath) {
                    parent.attach(record.node)
                }
                restored += 1
            } catch {
                failures.append(DeletionFailure(path: record.originalPath, reason: error.localizedDescription))
            }
        }
        lastOutcome = nil
        if !failures.isEmpty { failureReport = FailureReport(failures: failures) }
        showToast("Put back \(restored) item\(restored == 1 ? "" : "s").")
        treeDidChange()
        AppModel.shared.volumes.refresh()
    }

    func dismissOutcome() { lastOutcome = nil }

    // MARK: - Rescanning

    func rescan(_ node: FileNode? = nil) {
        let target = node ?? focus
        guard target.isDirectory, !isRescanning, deletionPhase == .idle, isAttached(target) else { return }
        isRescanning = true
        let path = target.path
        let isRoot = target.parent == nil
        let name = isRoot ? root.name : target.name
        let scanRoot = root.name
        let dedupe = AppModel.shared.prefs.dedupeHardLinks
        Task { [weak self] in
            let fresh = await Task.detached(priority: .userInitiated) {
                Scanner(dedupeHardLinks: dedupe).scan(path: path, rootName: name, scanRoot: scanRoot)
            }.value
            guard let self else { return }
            await self.quiesce()
            self.isRescanning = false
            guard let fresh, self.isAttached(target) else {
                self.showToast("Couldn't rescan “\(self.displayName(of: target))”.")
                return
            }
            self.replace(target, with: fresh)
            self.showToast("Rescanned “\(self.displayName(of: fresh))”.")
        }
    }

    private func replace(_ old: FileNode, with new: FileNode) {
        let focusPath = focus.path
        let selectedPaths = selection.map(\.path)
        if let parent = old.parent {
            parent.replaceChild(old, with: new)
        } else {
            root = new
        }
        focus = node(forPath: focusPath) ?? new
        selection = selectedPaths.compactMap { node(forPath: $0) }
        collector.remap { self.node(forPath: $0) }
        if lastOutcome != nil { lastOutcome = nil }
        treeDidChange()
    }

    /// Fixes up navigation state after nodes were added or removed, then asks views to reload.
    private func treeDidChange() {
        backStack.removeAll { !isAttached($0) }
        forwardStack.removeAll { !isAttached($0) }
        if !isAttached(focus) {
            var candidate: FileNode? = focus.parent
            while let node = candidate, !isAttached(node) { candidate = node.parent }
            focus = candidate ?? root
        }
        selection.removeAll { !isAttached($0) }
        historyVersion += 1
        recomputeColors()
        treeVersion += 1
        if flatResults != nil || viewMode != .tree { scheduleQuery() }
    }

    // MARK: - Toasts

    func showToast(_ message: String) {
        toast = message
        toastTask?.cancel()
        toastTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(4))
            guard !Task.isCancelled else { return }
            self?.toast = nil
        }
    }
}
