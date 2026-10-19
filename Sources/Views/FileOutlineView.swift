import SwiftUI
import AppKit
import Quartz

private extension NSUserInterfaceItemIdentifier {
    static let nameColumn = NSUserInterfaceItemIdentifier("name")
    static let sizeColumn = NSUserInterfaceItemIdentifier("size")
    static let percentColumn = NSUserInterfaceItemIdentifier("percent")
    static let itemsColumn = NSUserInterfaceItemIdentifier("items")
    static let modifiedColumn = NSUserInterfaceItemIdentifier("modified")
    static let collectColumn = NSUserInterfaceItemIdentifier("collect")

    static let nameCell = NSUserInterfaceItemIdentifier("nameCell")
    static let sizeCell = NSUserInterfaceItemIdentifier("sizeCell")
    static let textCell = NSUserInterfaceItemIdentifier("textCell")
    static let collectCell = NSUserInterfaceItemIdentifier("collectCell")
    static let rowView = NSUserInterfaceItemIdentifier("rowView")
}

/// The file tree: an NSOutlineView (fast with huge folders, native keyboard navigation, drag & drop,
/// Quick Look) wrapped for SwiftUI.
struct FileOutlineView: NSViewRepresentable {
    let session: ScanSession
    let focusID: ObjectIdentifier
    let treeVersion: Int
    let collectorVersion: Int
    let flatVersion: Int
    let isFlat: Bool
    let revealVersion: Int
    let decimalUnits: Bool

    func makeCoordinator() -> OutlineCoordinator { OutlineCoordinator() }

    func makeNSView(context: Context) -> NSScrollView {
        let coordinator = context.coordinator
        let outline = ReaperOutlineView()
        outline.coordinator = coordinator
        outline.style = .fullWidth
        outline.rowHeight = 30
        outline.intercellSpacing = NSSize(width: 6, height: 0)
        outline.usesAlternatingRowBackgroundColors = false
        outline.backgroundColor = .clear
        outline.gridStyleMask = []
        outline.selectionHighlightStyle = .regular
        outline.allowsMultipleSelection = true
        outline.allowsColumnReordering = true
        outline.allowsColumnResizing = true
        outline.allowsTypeSelect = true
        outline.columnAutoresizingStyle = .firstColumnOnlyAutoresizingStyle
        outline.indentationPerLevel = 14
        outline.autoresizesOutlineColumn = false
        outline.headerView = NeonHeaderView(frame: NSRect(x: 0, y: 0, width: 100, height: 30))

        let columns: [(NSUserInterfaceItemIdentifier, String, CGFloat, CGFloat, CGFloat, String?, Bool, NSTextAlignment)] = [
            (.nameColumn, "Name", 320, 160, 4000, "name", true, .left),
            (.sizeColumn, "Size", 190, 120, 400, "size", false, .left),
            (.percentColumn, "%", 58, 46, 90, "percent", false, .right),
            (.itemsColumn, "Items", 74, 50, 140, "items", false, .right),
            (.modifiedColumn, "Modified", 110, 80, 200, "modified", false, .right),
            (.collectColumn, "", 30, 30, 30, nil, false, .center),
        ]
        for (identifier, title, width, minWidth, maxWidth, sortKey, ascending, alignment) in columns {
            let column = NSTableColumn(identifier: identifier)
            column.title = title
            column.width = width
            column.minWidth = minWidth
            column.maxWidth = maxWidth
            column.headerCell.alignment = alignment
            if let sortKey {
                column.sortDescriptorPrototype = NSSortDescriptor(key: sortKey, ascending: ascending)
            }
            if identifier == .collectColumn {
                column.resizingMask = []
                column.headerToolTip = "Collect for deletion"
                column.headerCell.image = NSImage(systemSymbolName: "tray.and.arrow.down", accessibilityDescription: "Collect")
            }
            outline.addTableColumn(column)
            if identifier == .nameColumn { outline.outlineTableColumn = column }
        }
        outline.sortDescriptors = [NSSortDescriptor(key: "size", ascending: false)]

        outline.dataSource = coordinator
        outline.delegate = coordinator
        outline.target = coordinator
        outline.doubleAction = #selector(OutlineCoordinator.doubleClicked(_:))
        outline.menu = coordinator.makeContextMenu()
        outline.setDraggingSourceOperationMask([.copy, .generic], forLocal: false)
        outline.setDraggingSourceOperationMask([.copy, .generic], forLocal: true)

        let scrollView = NSScrollView()
        scrollView.documentView = outline
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = false
        scrollView.autohidesScrollers = true
        scrollView.borderType = .noBorder
        scrollView.drawsBackground = false
        scrollView.contentView.drawsBackground = false

        coordinator.outline = outline
        coordinator.update(from: self)
        return scrollView
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {
        context.coordinator.update(from: self)
    }
}

// MARK: - Outline view

final class ReaperOutlineView: NSOutlineView {
    weak var coordinator: OutlineCoordinator?
    private var tookInitialFocus = false

    /// Take keyboard focus as soon as the tree appears, so arrows, Space and ⌫ work right away.
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        guard let window, !tookInitialFocus else { return }
        tookInitialFocus = true
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            window.makeFirstResponder(self)
            if self.selectedRow < 0 && self.numberOfRows > 0 {
                self.selectRowIndexes([0], byExtendingSelection: false)
            }
        }
    }

    override func keyDown(with event: NSEvent) {
        let flags = event.modifierFlags.intersection([.command, .option, .control, .shift])
        switch event.keyCode {
        case 49 where flags.isEmpty:                 // space
            coordinator?.quickLookSelection()
            return
        case 51 where flags.isEmpty, 117 where flags.isEmpty:   // ⌫, ⌦
            coordinator?.toggleCollectSelection()
            return
        case 36, 76:                                 // return, enter
            coordinator?.openSelection()
            return
        case 125 where flags == .command:            // ⌘↓
            coordinator?.zoomIntoSelection()
            return
        case 53:                                     // esc
            if QuickLook.shared.isVisible { QuickLook.shared.toggle(urls: []) ; return }
        default:
            break
        }
        super.keyDown(with: event)
    }

    override func becomeFirstResponder() -> Bool {
        QuickLook.shared.keyHandler = self
        return super.becomeFirstResponder()
    }

    // Quick Look panel control through the responder chain.
    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }

    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        QuickLook.shared.keyHandler = self
        panel.dataSource = QuickLook.shared
        panel.delegate = QuickLook.shared
    }

    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {}
}

// MARK: - Coordinator

@MainActor
final class OutlineCoordinator: NSObject, NSOutlineViewDataSource, NSOutlineViewDelegate, NSMenuDelegate {
    weak var outline: ReaperOutlineView?
    private(set) weak var session: ScanSession?

    private var lastFocus: ObjectIdentifier?
    private var lastTree = -1
    private var lastCollector = -1
    private var lastFlatVersion = -1
    private var lastIsFlat = false
    private var lastReveal = 0
    private var lastUnits = true

    private var sortKey = "size"
    private var ascending = false
    private var sortedCache: [ObjectIdentifier: [FileNode]] = [:]
    private var flatItems: [FileNode] = []
    private var isFlat = false
    private var suppressSelectionSync = false
    private var menuTargets: [FileNode] = []

    func update(from view: FileOutlineView) {
        guard let outline else { return }
        let newSession = view.session
        let sessionChanged = session !== newSession
        session = newSession

        if sessionChanged || lastFocus != view.focusID || lastIsFlat != view.isFlat
            || (view.isFlat && lastFlatVersion != view.flatVersion) {
            isFlat = view.isFlat
            flatItems = sortedFlat(newSession.flatResults ?? [])
            sortedCache.removeAll()
            suppressSelectionSync = true
            outline.reloadData()
            outline.scrollRowToVisible(0)
            suppressSelectionSync = false
            syncSelectionToSession()
        } else if lastTree != view.treeVersion {
            sortedCache.removeAll()
            flatItems = flatItems.filter { newSession.isAttached($0) }
            reloadPreservingState()
        } else if lastCollector != view.collectorVersion || lastUnits != view.decimalUnits {
            refreshVisibleRows()
        }

        if lastReveal != view.revealVersion, let target = newSession.revealTarget {
            reveal(target)
        }

        lastFocus = view.focusID
        lastTree = view.treeVersion
        lastCollector = view.collectorVersion
        lastFlatVersion = view.flatVersion
        lastIsFlat = view.isFlat
        lastReveal = view.revealVersion
        lastUnits = view.decimalUnits
    }

    // MARK: Data

    private func children(of item: Any?) -> [FileNode] {
        guard let session else { return [] }
        if item == nil {
            return isFlat ? flatItems : sortedChildren(of: session.focus)
        }
        guard !isFlat, let node = item as? FileNode else { return [] }
        return sortedChildren(of: node)
    }

    /// Children are stored largest first, so that order needs no sorting.
    private var isDefaultSort: Bool { (sortKey == "size" || sortKey == "percent") && !ascending }

    private func sortedChildren(of node: FileNode) -> [FileNode] {
        if isDefaultSort { return node.children }
        let key = ObjectIdentifier(node)
        if let cached = sortedCache[key] { return cached }
        let sorted = sort(node.children)
        sortedCache[key] = sorted
        return sorted
    }

    private func sortedFlat(_ items: [FileNode]) -> [FileNode] {
        isDefaultSort ? items : sort(items)
    }

    private func sort(_ nodes: [FileNode]) -> [FileNode] {
        let ascending = self.ascending
        switch sortKey {
        case "name":
            return nodes.sorted {
                let order = $0.name.localizedStandardCompare($1.name)
                return ascending ? order == .orderedAscending : order == .orderedDescending
            }
        case "items":
            return nodes.sorted { ascending ? $0.itemCount < $1.itemCount : $0.itemCount > $1.itemCount }
        case "modified":
            return nodes.sorted { ascending ? $0.modified < $1.modified : $0.modified > $1.modified }
        default:
            return nodes.sorted { ascending ? $0.size < $1.size : $0.size > $1.size }
        }
    }

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        children(of: item).count
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        children(of: item)[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        guard !isFlat, let node = item as? FileNode else { return false }
        return node.isDirectory && !node.children.isEmpty
    }

    func outlineView(_ outlineView: NSOutlineView, pasteboardWriterForItem item: Any) -> NSPasteboardWriting? {
        (item as? FileNode)?.url as NSURL?
    }

    func outlineView(_ outlineView: NSOutlineView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
        guard let descriptor = outlineView.sortDescriptors.first, let key = descriptor.key else { return }
        sortKey = key
        ascending = descriptor.ascending
        sortedCache.removeAll()
        flatItems = isFlat ? sort(flatItems) : flatItems
        reloadPreservingState()
    }

    // MARK: Cells

    func outlineView(_ outlineView: NSOutlineView, rowViewForItem item: Any) -> NSTableRowView? {
        let row = outlineView.makeView(withIdentifier: .rowView, owner: nil) as? NeonRowView ?? NeonRowView()
        row.identifier = .rowView
        return row
    }

    func outlineView(_ outlineView: NSOutlineView, didAdd rowView: NSTableRowView, forRow row: Int) {
        (rowView as? NeonRowView)?.isAlternate = row % 2 == 1
    }

    func outlineView(_ outlineView: NSOutlineView, viewFor tableColumn: NSTableColumn?, item: Any) -> NSView? {
        guard let node = item as? FileNode, let column = tableColumn, let session else { return nil }
        let collector = session.collector

        switch column.identifier {
        case .nameColumn:
            let cell = outlineView.makeView(withIdentifier: .nameCell, owner: nil) as? NameCell ?? NameCell(identifier: .nameCell)
            let state: NameCell.CollectState = collector.contains(node) ? .collected
                : (collector.coveringAncestor(of: node) != nil ? .covered : .none)
            cell.configure(
                name: session.displayName(of: node),
                subtitle: isFlat ? session.relativePath(of: node.parent) : nil,
                icon: IconCache.icon(for: node),
                color: session.color(for: node),
                state: state,
                unreadable: node.isUnreadable
            )
            cell.toolTip = node.path
            return cell

        case .sizeColumn:
            let cell = outlineView.makeView(withIdentifier: .sizeCell, owner: nil) as? SizeCell ?? SizeCell(identifier: .sizeCell)
            let reference = isFlat ? session.focus.size : (node.parent?.size ?? node.size)
            let fraction = reference > 0 ? Double(node.size) / Double(reference) : 0
            cell.configure(text: ByteFormat.string(node.size), fraction: fraction, color: session.color(for: node),
                           dimmed: collector.isCovered(node))
            return cell

        case .percentColumn:
            let reference = isFlat ? session.focus.size : (node.parent?.size ?? node.size)
            return textCell(outlineView, ByteFormat.percent(node.size, of: reference), alignment: .right)

        case .itemsColumn:
            return textCell(outlineView, node.isDirectory ? ByteFormat.count(node.itemCount) : "", alignment: .right)

        case .modifiedColumn:
            return textCell(outlineView, node.modified > 0 ? ByteFormat.date(node.modificationDate) : "", alignment: .right)

        case .collectColumn:
            let cell = outlineView.makeView(withIdentifier: .collectCell, owner: nil) as? CollectCell ?? CollectCell(identifier: .collectCell)
            cell.button.target = self
            cell.button.action = #selector(collectButtonClicked(_:))
            cell.configure(collected: collector.contains(node), covered: collector.coveringAncestor(of: node) != nil)
            return cell

        default:
            return nil
        }
    }

    private func textCell(_ outlineView: NSOutlineView, _ text: String, alignment: NSTextAlignment) -> NSView {
        let cell = outlineView.makeView(withIdentifier: .textCell, owner: nil) as? TextCell ?? TextCell(identifier: .textCell)
        cell.label.stringValue = text
        cell.label.alignment = alignment
        return cell
    }

    // MARK: Selection

    func outlineViewSelectionDidChange(_ notification: Notification) {
        guard !suppressSelectionSync else { return }
        syncSelectionToSession()
    }

    private func syncSelectionToSession() {
        guard let outline, let session else { return }
        let nodes = selectedNodes()
        if nodes.map(ObjectIdentifier.init) != session.selection.map(ObjectIdentifier.init) {
            session.selection = nodes
        }
        _ = outline
        QuickLook.shared.updateIfVisible(urls: nodes.map(\.url))
    }

    private func selectedNodes() -> [FileNode] {
        guard let outline else { return [] }
        return outline.selectedRowIndexes.compactMap { outline.item(atRow: $0) as? FileNode }
    }

    // MARK: Reloading

    private func reloadPreservingState() {
        guard let outline, let session else { return }
        var expanded: [FileNode] = []
        for row in 0..<outline.numberOfRows {
            if let node = outline.item(atRow: row) as? FileNode, outline.isItemExpanded(node) {
                expanded.append(node)
            }
        }
        let selected = Set(selectedNodes().map(ObjectIdentifier.init))
        let visible = outline.rows(in: outline.visibleRect)
        let topNode = visible.length > 0 ? outline.item(atRow: visible.location) as? FileNode : nil

        suppressSelectionSync = true
        outline.reloadData()
        for node in expanded where session.isAttached(node) {
            outline.expandItem(node)
        }
        var rows = IndexSet()
        if !selected.isEmpty {
            for row in 0..<outline.numberOfRows {
                if let node = outline.item(atRow: row) as? FileNode, selected.contains(ObjectIdentifier(node)) {
                    rows.insert(row)
                }
            }
        }
        outline.selectRowIndexes(rows, byExtendingSelection: false)
        if let topNode {
            let row = outline.row(forItem: topNode)
            if row >= 0 { outline.scroll(NSPoint(x: 0, y: outline.rect(ofRow: row).minY - (outline.headerView?.frame.height ?? 0))) }
        }
        suppressSelectionSync = false
        syncSelectionToSession()
    }

    private func refreshVisibleRows() {
        guard let outline else { return }
        let range = outline.rows(in: outline.visibleRect)
        guard range.length > 0 else { return }
        outline.reloadData(forRowIndexes: IndexSet(integersIn: range.location..<(range.location + range.length)),
                           columnIndexes: IndexSet(integersIn: 0..<outline.numberOfColumns))
    }

    private func reveal(_ node: FileNode) {
        guard let outline, let session else { return }
        var chain: [FileNode] = []
        var current = node.parent
        while let ancestor = current, ancestor !== session.focus {
            chain.append(ancestor)
            current = ancestor.parent
        }
        for ancestor in chain.reversed() { outline.expandItem(ancestor) }
        let row = outline.row(forItem: node)
        guard row >= 0 else { return }
        outline.selectRowIndexes([row], byExtendingSelection: false)
        outline.scrollRowToVisible(row)
        outline.window?.makeFirstResponder(outline)
    }

    // MARK: Actions

    func quickLookSelection() {
        QuickLook.shared.toggle(urls: selectedNodes().map(\.url), keyHandler: outline)
    }

    func toggleCollectSelection() {
        session?.toggleCollect(selectedNodes())
    }

    func zoomIntoSelection() {
        guard let session else { return }
        if let folder = selectedNodes().first(where: \.isDirectory) {
            if isFlat { session.showInTree(folder) }
            session.navigate(to: folder)
        }
    }

    /// Return: zoom into a folder, Quick Look a file.
    func openSelection() {
        let nodes = selectedNodes()
        guard let first = nodes.first else { return }
        if first.isDirectory && nodes.count == 1 {
            zoomIntoSelection()
        } else {
            quickLookSelection()
        }
    }

    @objc func doubleClicked(_ sender: Any?) {
        guard let outline, let session else { return }
        let row = outline.clickedRow
        guard row >= 0, let node = outline.item(atRow: row) as? FileNode else { return }
        if node.isDirectory {
            if isFlat { session.showInTree(node) }
            session.navigate(to: node)
        } else {
            QuickLook.shared.toggle(urls: [node.url], keyHandler: outline)
        }
    }

    @objc func collectButtonClicked(_ sender: NSButton) {
        guard let outline, let session else { return }
        let row = outline.row(for: sender)
        guard row >= 0, let node = outline.item(atRow: row) as? FileNode else { return }
        session.toggleCollect([node])
    }

    // MARK: Context menu

    func makeContextMenu() -> NSMenu {
        let menu = NSMenu()
        menu.delegate = self
        return menu
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        guard let outline, let session else { return }
        let clicked = outline.clickedRow
        if clicked >= 0, !outline.selectedRowIndexes.contains(clicked), let node = outline.item(atRow: clicked) as? FileNode {
            menuTargets = [node]
        } else {
            menuTargets = selectedNodes()
        }
        guard !menuTargets.isEmpty else { return }
        let single = menuTargets.count == 1 ? menuTargets[0] : nil

        let allCollected = menuTargets.allSatisfy { session.collector.contains($0) }
        let collectTitle = allCollected
            ? (single != nil ? "Remove from Collector" : "Remove \(menuTargets.count) Items from Collector")
            : (single != nil ? "Add to Collector" : "Add \(menuTargets.count) Items to Collector")
        add(menu, collectTitle, symbol: allCollected ? "tray.and.arrow.up" : "tray.and.arrow.down", action: #selector(menuToggleCollect))
        menu.addItem(.separator())

        if let single, single.isDirectory {
            add(menu, "Zoom In", symbol: "plus.magnifyingglass", action: #selector(menuZoomIn))
        }
        if isFlat, single != nil {
            add(menu, "Show in Tree", symbol: "list.bullet.indent", action: #selector(menuShowInTree))
        }
        add(menu, "Quick Look", symbol: "eye", action: #selector(menuQuickLook))
        add(menu, "Reveal in Finder", symbol: "folder", action: #selector(menuReveal))
        add(menu, "Open", symbol: "arrow.up.forward.app", action: #selector(menuOpen))
        add(menu, menuTargets.count == 1 ? "Copy Path" : "Copy Paths", symbol: "doc.on.doc", action: #selector(menuCopyPath))
        if let single, single.isDirectory {
            menu.addItem(.separator())
            add(menu, "Rescan Folder", symbol: "arrow.clockwise", action: #selector(menuRescan))
        }
    }

    private func add(_ menu: NSMenu, _ title: String, symbol: String, action: Selector) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
        item.target = self
        item.image = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)
        menu.addItem(item)
    }

    @objc private func menuToggleCollect() { session?.toggleCollect(menuTargets) }

    @objc private func menuZoomIn() {
        guard let session, let folder = menuTargets.first else { return }
        if isFlat { session.showInTree(folder) }
        session.navigate(to: folder)
    }

    @objc private func menuShowInTree() {
        if let node = menuTargets.first { session?.showInTree(node) }
    }

    @objc private func menuQuickLook() {
        QuickLook.shared.toggle(urls: menuTargets.map(\.url), keyHandler: outline)
    }

    @objc private func menuReveal() {
        NSWorkspace.shared.activateFileViewerSelecting(menuTargets.map(\.url))
    }

    @objc private func menuOpen() {
        for url in menuTargets.map(\.url) { NSWorkspace.shared.open(url) }
    }

    @objc private func menuCopyPath() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(menuTargets.map(\.path).joined(separator: "\n"), forType: .string)
    }

    @objc private func menuRescan() {
        if let node = menuTargets.first { session?.rescan(node) }
    }
}

// MARK: - Rows & header

/// Subtle zebra stripes, and a glowing magenta outline for the selection (as in the mockup).
final class NeonRowView: NSTableRowView {
    var isAlternate = false { didSet { needsDisplay = true } }

    // Keep cell text in its normal colours on the dark selection.
    override var isEmphasized: Bool {
        get { false }
        set {}
    }

    override func drawBackground(in dirtyRect: NSRect) {
        guard isAlternate else { return }
        Theme.text.withAlphaComponent(0.025).setFill()
        bounds.fill()
    }

    override func drawSelection(in dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 3, dy: 1.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 7, yRadius: 7)
        let window = self.window
        let active = window?.isKeyWindow ?? false
        NSGradient(colors: [Theme.magenta.withAlphaComponent(active ? 0.30 : 0.16),
                            Theme.violet.withAlphaComponent(active ? 0.22 : 0.12)])?.draw(in: path, angle: 0)
        NSGraphicsContext.saveGraphicsState()
        let glow = NSShadow()
        glow.shadowColor = Theme.magenta.withAlphaComponent(active ? 0.8 : 0.3)
        glow.shadowBlurRadius = 6
        glow.set()
        (active ? Theme.magenta : Theme.violet).withAlphaComponent(0.9).setStroke()
        path.lineWidth = 1.4
        path.stroke()
        NSGraphicsContext.restoreGraphicsState()
    }
}

/// Column headers on the dark panel: lavender titles and a neon sort chevron.
final class NeonHeaderView: NSTableHeaderView {
    override func draw(_ dirtyRect: NSRect) {
        Theme.panel.withAlphaComponent(0.6).setFill()
        bounds.fill()
        Theme.line.withAlphaComponent(0.8).setFill()
        NSRect(x: 0, y: isFlipped ? bounds.maxY - 1 : 0, width: bounds.width, height: 1).fill()

        guard let table = tableView else { return }
        let sort = table.sortDescriptors.first
        for (index, column) in table.tableColumns.enumerated() {
            var rect = headerRect(ofColumn: index).insetBy(dx: 8, dy: 0)
            let sorted = column.sortDescriptorPrototype?.key != nil && column.sortDescriptorPrototype?.key == sort?.key
            if column.identifier.rawValue == "collect" {
                if let image = column.headerCell.image {
                    let tinted = image.tinted(Theme.textTertiary)
                    let size = NSSize(width: 14, height: 14)
                    tinted.draw(in: NSRect(x: rect.midX - size.width / 2, y: rect.midY - size.height / 2, width: size.width, height: size.height))
                }
                continue
            }
            let attributes: [NSAttributedString.Key: Any] = [
                .font: NSFont.systemFont(ofSize: 12, weight: .semibold).rounded,
                .foregroundColor: sorted ? Theme.text : Theme.textSecondary,
            ]
            let title = NSAttributedString(string: column.title, attributes: attributes)
            let textSize = title.size()
            if sorted {
                let chevron = NSImage(systemSymbolName: sort?.ascending == true ? "chevron.up" : "chevron.down", accessibilityDescription: nil)?
                    .withSymbolConfiguration(.init(pointSize: 9, weight: .bold))?.tinted(Theme.cyan)
                if let chevron {
                    let chevronRect = NSRect(x: rect.maxX - chevron.size.width, y: rect.midY - chevron.size.height / 2,
                                             width: chevron.size.width, height: chevron.size.height)
                    chevron.draw(in: chevronRect)
                    rect.size.width -= chevron.size.width + 6
                }
            }
            let x: CGFloat
            switch column.headerCell.alignment {
            case .right: x = rect.maxX - textSize.width
            case .center: x = rect.midX - textSize.width / 2
            default: x = rect.minX
            }
            title.draw(at: NSPoint(x: x, y: rect.midY - textSize.height / 2))
        }
    }
}

extension NSImage {
    func tinted(_ color: NSColor) -> NSImage {
        let image = self.copy() as! NSImage
        image.isTemplate = false
        image.lockFocus()
        color.set()
        NSRect(origin: .zero, size: image.size).fill(using: .sourceAtop)
        image.unlockFocus()
        return image
    }
}

extension NSFont {
    var rounded: NSFont {
        guard let descriptor = fontDescriptor.withDesign(.rounded) else { return self }
        return NSFont(descriptor: descriptor, size: pointSize) ?? self
    }
}

// MARK: - Cells

final class DotView: NSView {
    var color: NSColor = .clear { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        NSGraphicsContext.saveGraphicsState()
        let glow = NSShadow()
        glow.shadowColor = color.withAlphaComponent(0.9)
        glow.shadowBlurRadius = 4
        glow.set()
        color.setFill()
        NSBezierPath(ovalIn: bounds.insetBy(dx: 2, dy: 2)).fill()
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class NameCell: NSTableCellView {
    enum CollectState { case none, collected, covered }

    private let dot = DotView()
    private let icon = NSImageView()
    private let label = NSTextField(labelWithString: "")
    private let lock = NSImageView()
    private var name = ""
    private var subtitle: String?
    private var state: CollectState = .none

    convenience init(identifier: NSUserInterfaceItemIdentifier) {
        self.init(frame: .zero)
        self.identifier = identifier
        icon.imageScaling = .scaleProportionallyUpOrDown
        label.lineBreakMode = .byTruncatingMiddle
        label.cell?.truncatesLastVisibleLine = true
        lock.image = NSImage(systemSymbolName: "lock.fill", accessibilityDescription: "No access")
        lock.contentTintColor = Theme.orange
        lock.toolTip = "BitReaper couldn't read this folder. Grant Full Disk Access to see inside."
        for view in [dot, icon, label, lock] as [NSView] { addSubview(view) }
        textField = label
        imageView = icon
    }

    func configure(name: String, subtitle: String?, icon image: NSImage, color: NSColor, state: CollectState, unreadable: Bool) {
        self.name = name
        self.subtitle = (subtitle?.isEmpty ?? true) ? nil : subtitle
        self.state = state
        icon.image = image
        icon.alphaValue = state == .none ? 1 : 0.4
        dot.color = state == .none ? color : color.withAlphaComponent(0.3)
        lock.isHidden = !unreadable
        updateText()
        needsLayout = true
    }

    override var backgroundStyle: NSView.BackgroundStyle {
        didSet { updateText() }
    }

    private func updateText() {
        let primary: NSColor = state == .none ? Theme.text : Theme.textTertiary
        var attributes: [NSAttributedString.Key: Any] = [
            .foregroundColor: primary,
            .font: NSFont.systemFont(ofSize: 13, weight: .medium).rounded,
        ]
        if state == .collected {
            attributes[.strikethroughStyle] = NSUnderlineStyle.single.rawValue
            attributes[.strikethroughColor] = Theme.magenta
        }
        let text = NSMutableAttributedString(string: name, attributes: attributes)
        if let subtitle {
            text.append(NSAttributedString(string: "  " + subtitle, attributes: [
                .foregroundColor: Theme.textTertiary,
                .font: NSFont.systemFont(ofSize: 11).rounded,
            ]))
        }
        label.attributedStringValue = text
    }

    override func layout() {
        super.layout()
        let height = bounds.height
        dot.frame = NSRect(x: -1, y: (height - 12) / 2, width: 12, height: 12)
        icon.frame = NSRect(x: 15, y: (height - 20) / 2, width: 20, height: 20)
        let lockWidth: CGFloat = lock.isHidden ? 0 : 16
        let labelHeight: CGFloat = 17
        label.frame = NSRect(x: 42, y: (height - labelHeight) / 2, width: max(0, bounds.width - 44 - lockWidth), height: labelHeight)
        lock.frame = NSRect(x: bounds.width - 14, y: (height - 12) / 2, width: 12, height: 12)
    }
}

final class SizeBarView: NSView {
    var fraction: Double = 0 { didSet { needsDisplay = true } }
    var color: NSColor = Theme.pink { didSet { needsDisplay = true } }

    override func draw(_ dirtyRect: NSRect) {
        let track = bounds.insetBy(dx: 2, dy: (bounds.height - 7) / 2)
        Theme.line.withAlphaComponent(0.55).setFill()
        NSBezierPath(roundedRect: track, xRadius: 3.5, yRadius: 3.5).fill()
        guard fraction > 0 else { return }
        var fill = track
        fill.size.width = max(5, track.width * CGFloat(min(fraction, 1)))
        let path = NSBezierPath(roundedRect: fill, xRadius: 3.5, yRadius: 3.5)
        NSGraphicsContext.saveGraphicsState()
        let glow = NSShadow()
        glow.shadowColor = color.withAlphaComponent(0.7)
        glow.shadowBlurRadius = 5
        glow.set()
        color.setFill()
        path.fill()
        NSGraphicsContext.restoreGraphicsState()
        NSGradient(colors: [color, Palette.barEnd(for: color)])?.draw(in: path, angle: 0)
    }
}

final class SizeCell: NSTableCellView {
    private let bar = SizeBarView()
    private let label = NSTextField(labelWithString: "")

    convenience init(identifier: NSUserInterfaceItemIdentifier) {
        self.init(frame: .zero)
        self.identifier = identifier
        label.alignment = .right
        label.font = NSFont.monospacedDigitSystemFont(ofSize: 13, weight: .semibold)
        label.textColor = Theme.text
        label.lineBreakMode = .byClipping
        addSubview(bar)
        addSubview(label)
        textField = label
    }

    func configure(text: String, fraction: Double, color: NSColor, dimmed: Bool) {
        label.stringValue = text
        bar.fraction = fraction
        bar.color = dimmed ? color.withAlphaComponent(0.25) : color
        label.textColor = dimmed ? Theme.textTertiary : Theme.text
    }

    override func layout() {
        super.layout()
        let labelWidth: CGFloat = 80
        let height = bounds.height
        bar.frame = NSRect(x: 0, y: 0, width: max(0, bounds.width - labelWidth - 8), height: height)
        label.frame = NSRect(x: bounds.width - labelWidth, y: (height - 17) / 2, width: labelWidth, height: 17)
    }
}

final class TextCell: NSTableCellView {
    let label = NSTextField(labelWithString: "")

    convenience init(identifier: NSUserInterfaceItemIdentifier) {
        self.init(frame: .zero)
        self.identifier = identifier
        label.font = NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .regular)
        label.textColor = Theme.textSecondary
        label.lineBreakMode = .byTruncatingTail
        addSubview(label)
        textField = label
    }

    override func layout() {
        super.layout()
        label.frame = NSRect(x: 0, y: (bounds.height - 16) / 2, width: bounds.width, height: 16)
    }
}

final class CollectCell: NSTableCellView {
    let button = NSButton()

    convenience init(identifier: NSUserInterfaceItemIdentifier) {
        self.init(frame: .zero)
        self.identifier = identifier
        button.isBordered = false
        button.bezelStyle = .regularSquare
        button.imagePosition = .imageOnly
        button.setButtonType(.momentaryChange)
        button.focusRingType = .none
        button.wantsLayer = true
        addSubview(button)
    }

    func configure(collected: Bool, covered: Bool) {
        let symbol = collected ? "checkmark.circle.fill" : (covered ? "checkmark.circle" : "plus.circle")
        let config = NSImage.SymbolConfiguration(pointSize: 16, weight: .regular)
        button.image = NSImage(systemSymbolName: symbol, accessibilityDescription: collected ? "Collected" : "Collect")?
            .withSymbolConfiguration(config)
        button.contentTintColor = collected ? Theme.magenta : (covered ? Theme.textTertiary : Theme.textSecondary)
        button.shadow = collected ? {
            let glow = NSShadow()
            glow.shadowColor = Theme.magenta
            glow.shadowBlurRadius = 6
            return glow
        }() : nil
        button.isEnabled = !covered
        button.toolTip = collected ? "Remove from Collector (⌫)" : (covered ? "Collected with its folder" : "Add to Collector (⌫)")
    }

    override func layout() {
        super.layout()
        button.frame = NSRect(x: (bounds.width - 22) / 2, y: (bounds.height - 22) / 2, width: 22, height: 22)
    }
}
