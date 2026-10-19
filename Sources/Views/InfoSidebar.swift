import SwiftUI

/// Right-hand column: the current folder as a donut chart with its legend, and
/// details about the selected items below it.
struct InfoSidebar: View {
    let session: ScanSession
    @Environment(AppModel.self) private var model

    var body: some View {
        // File nodes aren't observable: every view showing sizes reads the tree version so it
        // refreshes after deletions, put-backs and rescans.
        let _ = session.treeVersion
        ScrollView {
            VStack(spacing: 12) {
                OverviewCard(session: session)
                switch session.selection.count {
                case 0: TipsCard(session: session)
                case 1: ItemDetails(session: session, node: session.selection[0])
                default: MultiSelectionDetails(session: session, nodes: session.selection)
                }
                if session.unreadableCount > 0 && !model.hasFullDiskAccess {
                    VStack(alignment: .leading, spacing: 8) {
                        Label("\(ByteFormat.count(session.unreadableCount)) folders couldn't be read", systemImage: "lock.fill")
                            .font(.system(size: 12, weight: .semibold, design: .rounded))
                            .foregroundStyle(Theme.orangeC)
                        FullDiskAccessBanner(compact: true)
                    }
                }
            }
            .padding(.vertical, 2)
            .padding(.horizontal, 2)
        }
        .scrollIndicators(.never)
    }
}

// MARK: - Overview

private struct Slice: Identifiable {
    let id: Int
    let node: FileNode?
    let size: Int64
    let color: Color
}

private struct OverviewCard: View {
    let session: ScanSession
    @Environment(AppModel.self) private var model

    var body: some View {
        let _ = session.treeVersion
        let focus = session.focus
        let threshold = max(focus.size / 200, 1)
        let listed = Array(focus.children.prefix(7).filter { $0.size >= threshold })
        let rest = focus.children.dropFirst(listed.count)
        let restSize = rest.reduce(Int64(0)) { $0 + $1.size }
        let slices = listed.enumerated().map { index, node in
            Slice(id: index, node: node, size: node.size, color: Color(nsColor: session.color(for: node)))
        } + (restSize > 0 ? [Slice(id: -1, node: nil, size: restSize, color: Theme.textTertiaryC)] : [])
        let isVolumeRoot = focus === session.root && session.target.volumeID != nil

        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 10) {
                Image(nsImage: isVolumeRoot ? NSWorkspace.shared.icon(forFile: session.target.url.path) : IconCache.icon(for: focus))
                    .resizable()
                    .frame(width: 34, height: 34)
                Text(session.displayName(of: focus))
                    .font(Theme.display(16, .semibold))
                    .lineLimit(2)
                Spacer()
                SizeText(isVolumeRoot ? (session.volume?.total ?? focus.size) : focus.size)
                    .font(Theme.display(17, .bold))
            }

            DonutChart(session: session, slices: slices, total: max(focus.size, 1))
                .frame(height: 210)
                .frame(maxWidth: .infinity)

            VStack(spacing: 1) {
                ForEach(slices) { slice in
                    if let node = slice.node {
                        OverviewRow(session: session, node: node, color: slice.color)
                    } else {
                        HStack(spacing: 10) {
                            ColorDot(color: Theme.textTertiaryC)
                            Text("smaller objects…").foregroundStyle(Theme.textSecondaryC)
                            Text("(\(ByteFormat.count(rest.count)))").foregroundStyle(Theme.textTertiaryC).font(.caption)
                            Spacer()
                            SizeText(slice.size).foregroundStyle(Theme.textSecondaryC)
                        }
                        .font(.system(size: 13, design: .rounded))
                        .padding(.horizontal, 6)
                        .padding(.vertical, 5)
                    }
                }
                if focus.children.isEmpty {
                    Text(focus.isUnreadable ? "BitReaper couldn't read this folder." : "This folder is empty.")
                        .foregroundStyle(Theme.textSecondaryC)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }

            if isVolumeRoot, let volume = session.volume {
                Rectangle().fill(Theme.neonBorder.opacity(0.5)).frame(height: 1)
                VolumeSummary(session: session, volume: volume)
            }
        }
        .padding(16)
        .neonPanel()
    }
}

/// Neon donut of the current folder's biggest items; the selected item's slice is highlighted.
private struct DonutChart: View {
    let session: ScanSession
    let slices: [Slice]
    let total: Int64
    @Environment(AppModel.self) private var model

    var body: some View {
        let selectedTop = selectedTopLevel
        let focus = session.focus
        let isVolumeRoot = focus === session.root && session.target.volumeID != nil

        ZStack {
            Canvas { context, size in
                let side = min(size.width, size.height)
                let lineWidth: CGFloat = 22
                let radius = side / 2 - lineWidth / 2 - 6
                let center = CGPoint(x: size.width / 2, y: size.height / 2)
                var track = Path()
                track.addArc(center: center, radius: radius, startAngle: .zero, endAngle: .degrees(360), clockwise: false)
                context.stroke(track, with: .color(Theme.lineC.opacity(0.5)), lineWidth: lineWidth)

                let gap = slices.count > 1 ? 1.6 : 0.0
                var start = -90.0
                for slice in slices {
                    let sweep = Double(slice.size) / Double(total) * 360
                    guard sweep > 0.4 else { start += sweep; continue }
                    let dimmed = selectedTop != nil && slice.node !== selectedTop
                    var arc = Path()
                    arc.addArc(center: center, radius: radius,
                               startAngle: .degrees(start + gap / 2),
                               endAngle: .degrees(start + max(sweep - gap / 2, gap / 2 + 0.2)),
                               clockwise: false)
                    var layer = context
                    if !dimmed { layer.addFilter(.shadow(color: slice.color.opacity(0.85), radius: 6)) }
                    let width = selectedTop != nil && !dimmed ? lineWidth + 6 : lineWidth
                    layer.stroke(arc, with: .color(slice.color.opacity(dimmed ? 0.3 : 1)),
                                 style: StrokeStyle(lineWidth: width, lineCap: .butt))
                    start += sweep
                }
            }

            VStack(spacing: 2) {
                if let node = session.selection.first, session.selection.count == 1 {
                    SizeText(node.size).font(Theme.display(22, .bold))
                    Text(ByteFormat.percent(node.size, of: focus.size))
                        .font(Theme.display(15, .semibold))
                    Text("of \(session.displayName(of: focus))")
                        .font(.system(size: 11, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                        .lineLimit(1)
                        .frame(maxWidth: 110)
                } else if isVolumeRoot, let volume = session.volume {
                    let used = volume.used(purgeableAsFree: true)
                    SizeText(used).font(Theme.display(22, .bold))
                    Text(ByteFormat.percent(used, of: volume.total))
                        .font(Theme.display(15, .semibold))
                    Text("Used").font(.system(size: 12, design: .rounded)).foregroundStyle(Theme.textSecondaryC)
                } else {
                    SizeText(focus.size).font(Theme.display(22, .bold))
                    Text("\(ByteFormat.count(focus.itemCount)) items")
                        .font(.system(size: 12, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                }
            }
            .neonGlow(Theme.violetC, radius: 8)
        }
    }

    private var selectedTopLevel: FileNode? {
        guard session.selection.count == 1, var node = session.selection.first else { return nil }
        while let parent = node.parent, parent !== session.focus { node = parent }
        return node.parent === session.focus ? node : nil
    }
}

private struct OverviewRow: View {
    let session: ScanSession
    let node: FileNode
    let color: Color
    @State private var hovering = false

    var body: some View {
        let _ = session.treeVersion
        let collected = session.collector.isCovered(node)
        Button {
            if node.isDirectory {
                session.navigate(to: node)
            } else {
                session.reveal(node)
            }
        } label: {
            HStack(spacing: 10) {
                ColorDot(color: color)
                Text(node.name)
                    .lineLimit(1)
                    .truncationMode(.middle)
                    .strikethrough(collected, color: Theme.magentaC)
                    .foregroundStyle(collected ? Theme.textTertiaryC : Theme.textC)
                Spacer(minLength: 8)
                SizeText(node.size)
                    .foregroundStyle(Theme.textC)
            }
            .font(.system(size: 13, weight: .medium, design: .rounded))
            .padding(.horizontal, 6)
            .padding(.vertical, 5)
            .contentShape(Rectangle())
            .hoverHighlight(hovering)
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
        .help(node.isDirectory ? "Zoom into “\(node.name)”" : "Select “\(node.name)”")
        .contextMenu {
            Button(session.collector.contains(node) ? "Remove from Collector" : "Add to Collector") {
                session.toggleCollect([node])
            }
            Button("Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting([node.url]) }
        }
    }
}

private struct VolumeSummary: View {
    let session: ScanSession
    let volume: VolumeInfo
    @State private var showHiddenHelp = false

    var body: some View {
        let _ = session.treeVersion
        VStack(spacing: 8) {
            if let hidden = session.hiddenSpace, hidden > 0 {
                HStack(spacing: 10) {
                    ColorDot(color: Theme.purpleC)
                    Button { showHiddenHelp.toggle() } label: {
                        HStack(spacing: 4) {
                            Text("Hidden space")
                            Image(systemName: "questionmark.circle").font(.caption)
                        }
                        .foregroundStyle(Theme.purpleC)
                    }
                    .buttonStyle(.plain)
                    .popover(isPresented: $showHiddenHelp, arrowEdge: .leading) {
                        HiddenSpaceHelp(snapshots: session.snapshots ?? [])
                    }
                    Spacer()
                    SizeText(hidden)
                }
            }
            HStack(spacing: 10) {
                ColorDot(color: Theme.textTertiaryC.opacity(0.6))
                Text("Free space").foregroundStyle(Theme.textSecondaryC)
                Spacer()
                SizeText(volume.available)
            }
            HStack(spacing: 10) {
                ColorDot(color: Theme.textTertiaryC.opacity(0.35))
                Text("Free + purgeable").foregroundStyle(Theme.textSecondaryC)
                Spacer()
                SizeText(volume.availableImportant)
            }
            if let snapshots = session.snapshots, !snapshots.isEmpty {
                HStack(spacing: 10) {
                    Image(systemName: "camera.on.rectangle").frame(width: 9).foregroundStyle(Theme.textSecondaryC).font(.caption)
                    Text("\(snapshots.count) local snapshot\(snapshots.count == 1 ? "" : "s")").foregroundStyle(Theme.textSecondaryC)
                    Spacer()
                }
                .help(snapshots.joined(separator: "\n"))
            }
        }
        .padding(.horizontal, 6)
        .font(.system(size: 13, weight: .medium, design: .rounded))
    }
}

private struct HiddenSpaceHelp: View {
    let snapshots: [String]

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("What is hidden space?").font(Theme.display(15, .semibold))
            Text("Space the disk reports as used that the scan couldn't see: other APFS volumes in the same container (macOS itself, VM swap, Preboot, Recovery), local Time Machine snapshots, and folders BitReaper isn't allowed to read.")
            Text("Grant Full Disk Access and rescan to reduce it. Snapshots are purged automatically by macOS when space is needed; you can also remove them with `tmutil deletelocalsnapshots /` in Terminal.")
                .foregroundStyle(Theme.textSecondaryC)
            if !snapshots.isEmpty {
                Text("Local snapshots").font(.subheadline.weight(.semibold)).padding(.top, 4)
                ForEach(snapshots, id: \.self) { Text($0).font(.caption.monospaced()) }
            }
        }
        .font(.callout)
        .padding(16)
        .frame(width: 360)
    }
}

private struct TipsCard: View {
    let session: ScanSession

    var body: some View {
        HStack(alignment: .top, spacing: 10) {
            Image(systemName: "info.circle")
                .font(.system(size: 18))
                .foregroundStyle(Theme.cyanC)
                .neonGlow(Theme.cyanC, radius: 4)
            VStack(alignment: .leading, spacing: 6) {
                Text("Scanned \(ByteFormat.count(session.root.itemCount)) items in \(String(format: "%.1f", session.scanDuration)) s")
                    .font(.system(size: 13, weight: .semibold, design: .rounded))
                Text("Select an item for details. Press ⌫ or click ⊕ to add it to the Collector, Space to preview it, double-click a folder to zoom in.")
                    .font(.system(size: 12, design: .rounded))
                    .foregroundStyle(Theme.textSecondaryC)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer(minLength: 0)
        }
        .padding(14)
        .neonPanel(cornerRadius: 12, glow: Theme.blueC, fill: Theme.blueC.opacity(0.07))
    }
}

// MARK: - Details

private struct ItemDetails: View {
    let session: ScanSession
    let node: FileNode
    @Environment(AppModel.self) private var model

    var body: some View {
        let _ = session.treeVersion
        let collector = session.collector
        let collected = collector.contains(node)
        let covering = collector.coveringAncestor(of: node)
        let focus = session.focus
        let nsColor = session.color(for: node)
        let color = Color(nsColor: nsColor)

        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                Image(nsImage: IconCache.icon(for: node))
                    .resizable()
                    .frame(width: 48, height: 48)
                VStack(alignment: .leading, spacing: 3) {
                    Text(session.displayName(of: node))
                        .font(Theme.display(15, .semibold))
                        .lineLimit(3)
                        .textSelection(.enabled)
                    Text(IconCache.kind(of: node))
                        .font(.system(size: 12, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                }
                Spacer(minLength: 0)
            }

            VStack(alignment: .leading, spacing: 6) {
                SizeText(node.size)
                    .font(Theme.display(28, .bold))
                    .neonGlow(color, radius: 6)
                UsageGauge(fraction: focus.size > 0 ? Double(node.size) / Double(focus.size) : 0,
                           colors: [color, Color(nsColor: Palette.barEnd(for: nsColor))], height: 7)
                Text("\(ByteFormat.percent(node.size, of: focus.size)) of “\(session.displayName(of: focus))”")
                    .font(.system(size: 11, design: .rounded))
                    .foregroundStyle(Theme.textSecondaryC)
            }

            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 6) {
                if node.isDirectory {
                    detailRow("Items", ByteFormat.count(node.itemCount))
                }
                if node.modified > 0 {
                    detailRow("Modified", node.modificationDate.formatted(date: .abbreviated, time: .shortened))
                }
                if let volume = session.volume {
                    detailRow("Of disk", ByteFormat.percent(node.size, of: volume.total))
                }
                GridRow {
                    Text("Where").foregroundStyle(Theme.textSecondaryC)
                    Text(((node.parent?.path ?? node.path) as NSString).abbreviatingWithTildeInPath)
                        .textSelection(.enabled)
                        .lineLimit(4)
                        .truncationMode(.middle)
                }
            }
            .font(.system(size: 12, design: .rounded))

            if node.isUnreadable {
                Label("BitReaper couldn't read this folder, so its size is unknown.", systemImage: "lock.fill")
                    .font(.system(size: 12, design: .rounded))
                    .foregroundStyle(Theme.orangeC)
            }

            VStack(spacing: 8) {
                if let covering {
                    Label("Collected with “\(session.displayName(of: covering))”", systemImage: "checkmark.circle")
                        .foregroundStyle(Theme.textSecondaryC)
                        .font(.system(size: 12, design: .rounded))
                        .frame(maxWidth: .infinity, alignment: .leading)
                } else {
                    Button {
                        session.toggleCollect([node])
                    } label: {
                        Label(collected ? "Remove from Collector" : "Add to Collector",
                              systemImage: collected ? "tray.and.arrow.up" : "tray.and.arrow.down")
                            .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.neon(collected ? .normal : .primary))
                }
                HStack(spacing: 8) {
                    Button { QuickLook.shared.toggle(urls: [node.url]) } label: {
                        Label("Quick Look", systemImage: "eye").frame(maxWidth: .infinity)
                    }
                    Button { NSWorkspace.shared.activateFileViewerSelecting([node.url]) } label: {
                        Label("Reveal", systemImage: "folder").frame(maxWidth: .infinity)
                    }
                }
                .buttonStyle(.neon(.normal, compact: true))
                HStack(spacing: 8) {
                    if node.isDirectory {
                        Button { session.navigate(to: node) } label: {
                            Label("Zoom In", systemImage: "plus.magnifyingglass").frame(maxWidth: .infinity)
                        }
                    }
                    Button {
                        NSPasteboard.general.clearContents()
                        NSPasteboard.general.setString(node.path, forType: .string)
                        session.showToast("Copied path")
                    } label: {
                        Label("Copy Path", systemImage: "doc.on.doc").frame(maxWidth: .infinity)
                    }
                }
                .buttonStyle(.neon(.normal, compact: true))
            }
        }
        .padding(16)
        .neonPanel(glow: color)
    }

    private func detailRow(_ title: String, _ value: String) -> some View {
        GridRow {
            Text(title).foregroundStyle(Theme.textSecondaryC)
            Text(value).textSelection(.enabled)
        }
    }
}

private struct MultiSelectionDetails: View {
    let session: ScanSession
    let nodes: [FileNode]

    var body: some View {
        let _ = session.treeVersion
        // Don't double count items selected together with their folder.
        let ids = Set(nodes.map(ObjectIdentifier.init))
        let topLevel = nodes.filter { node in
            var parent = node.parent
            while let p = parent {
                if ids.contains(ObjectIdentifier(p)) { return false }
                parent = p.parent
            }
            return true
        }
        let total = topLevel.reduce(Int64(0)) { $0 + $1.size }
        let allCollected = nodes.allSatisfy { session.collector.contains($0) }

        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 12) {
                Image(systemName: "square.stack.3d.up.fill")
                    .font(.system(size: 30))
                    .foregroundStyle(Theme.cyanC)
                    .neonGlow(Theme.cyanC, radius: 5)
                VStack(alignment: .leading) {
                    Text("\(nodes.count) items selected").font(Theme.display(15, .semibold))
                    SizeText(total).font(Theme.display(24, .bold))
                }
            }
            VStack(spacing: 3) {
                ForEach(Array(nodes.sorted { $0.size > $1.size }.prefix(10).enumerated()), id: \.offset) { _, node in
                    HStack(spacing: 8) {
                        ColorDot(color: Color(nsColor: session.color(for: node)))
                        Text(node.name).lineLimit(1).truncationMode(.middle)
                        Spacer()
                        SizeText(node.size).foregroundStyle(Theme.textSecondaryC)
                    }
                    .font(.system(size: 12, design: .rounded))
                }
                if nodes.count > 10 {
                    Text("and \(nodes.count - 10) more…").font(.caption).foregroundStyle(Theme.textSecondaryC)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
            }
            Button {
                session.toggleCollect(nodes)
            } label: {
                Label(allCollected ? "Remove All from Collector" : "Add All to Collector",
                      systemImage: allCollected ? "tray.and.arrow.up" : "tray.and.arrow.down")
                    .frame(maxWidth: .infinity)
            }
            .buttonStyle(.neon(allCollected ? .normal : .primary))
            HStack(spacing: 8) {
                Button { QuickLook.shared.toggle(urls: nodes.map(\.url)) } label: {
                    Label("Quick Look", systemImage: "eye").frame(maxWidth: .infinity)
                }
                Button { NSWorkspace.shared.activateFileViewerSelecting(nodes.map(\.url)) } label: {
                    Label("Reveal", systemImage: "folder").frame(maxWidth: .infinity)
                }
            }
            .buttonStyle(.neon(.normal, compact: true))
        }
        .padding(16)
        .neonPanel()
    }
}
