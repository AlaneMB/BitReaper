import SwiftUI

/// The bottom bar: drop zone, collected total, review list, Clear and Delete, plus the deletion
/// safety countdown, progress and result.
struct CollectorPanel: View {
    @Bindable var session: ScanSession
    @Environment(AppModel.self) private var model
    @State private var dropTargeted = false

    var body: some View {
        let collector = session.collector
        VStack(spacing: 0) {
            if session.collectorExpanded && !collector.isEmpty && session.deletionPhase == .idle {
                CollectorList(session: session)
                    .frame(height: min(250, CGFloat(collector.count) * 42 + 14))
                    .transition(.move(edge: .bottom).combined(with: .opacity))
                Rectangle().fill(Theme.neonBorder.opacity(0.4)).frame(height: 1)
            }
            bar
        }
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .neonPanel(glow: glowColor, fill: fillColor, dashed: collector.isEmpty && session.deletionPhase == .idle)
        .overlay(alignment: .bottomLeading) { mascot }
        .animation(.easeOut(duration: 0.18), value: session.collectorExpanded)
        .animation(.easeOut(duration: 0.18), value: collector.count)
        .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
            guard session.deletionPhase == .idle else { return false }
            return loadFileURLs(from: providers) { urls in session.collect(urls: urls) }
        }
    }

    private var glowColor: Color {
        switch session.deletionPhase {
        case .countdown, .deleting: return Theme.dangerC
        case .idle: return dropTargeted ? Theme.cyanC : (session.collector.isEmpty ? Theme.violetC : Theme.magentaC)
        }
    }

    private var fillColor: Color {
        switch session.deletionPhase {
        case .countdown, .deleting: return Theme.dangerC.opacity(0.10)
        case .idle: return dropTargeted ? Theme.cyanC.opacity(0.12) : Theme.panelC.opacity(0.85)
        }
    }

    /// The reaper peeks up from the bottom edge of the bar, and pops up further when something is
    /// dragged over it or a deletion is running.
    private var mascot: some View {
        let busy = session.deletionPhase != .idle
        let raised = dropTargeted || busy
        return Image("ReaperPeek")
            .resizable()
            .aspectRatio(contentMode: .fit)
            .frame(width: 84)
            .shadow(color: (busy ? Theme.dangerC : Theme.magentaC).opacity(0.6), radius: 8)
            .scaleEffect(raised ? 1.1 : 1, anchor: .bottom)
            .offset(x: 12, y: raised ? -6 : 0)
            .animation(.spring(duration: 0.3, bounce: 0.4), value: raised)
            .allowsHitTesting(false)
    }

    private var bar: some View {
        let collector = session.collector
        return HStack(spacing: 14) {
            Color.clear.frame(width: 84, height: 1)   // room for the mascot

            switch session.deletionPhase {
            case .countdown(let seconds):
                CountdownRing(seconds: seconds, total: model.prefs.countdownSeconds)
                VStack(alignment: .leading, spacing: 2) {
                    Text("Reaping \(collector.count) item\(collector.count == 1 ? "" : "s") in \(seconds)…")
                        .font(Theme.display(16, .bold))
                        .foregroundStyle(Theme.dangerC)
                        .neonGlow(Theme.dangerC, radius: 5)
                    Text("Changed your mind? Press Cancel or Esc.")
                        .font(.system(size: 12, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                }
                Spacer()
                Button("Cancel") { session.cancelCountdown() }
                    .keyboardShortcut(.cancelAction)
                    .buttonStyle(.neon)

            case .deleting(let done, let total):
                ProgressView().controlSize(.small).tint(Theme.dangerC)
                VStack(alignment: .leading, spacing: 5) {
                    Text("Reaping \(done) of \(total)…").font(Theme.display(15, .semibold))
                    UsageGauge(fraction: Double(done) / Double(max(total, 1)), colors: [Theme.magentaC, Theme.dangerC], height: 6)
                        .frame(maxWidth: 280)
                }
                Spacer()

            case .idle:
                if collector.isEmpty {
                    if let outcome = session.lastOutcome {
                        outcomeView(outcome)
                    } else {
                        VStack(alignment: .leading, spacing: 3) {
                            Text("Collector").font(Theme.display(16, .bold))
                            Text("Drag items here, press ⌫, or click ⊕ to add them to the Collector for deletion.")
                                .font(.system(size: 12, design: .rounded))
                                .foregroundStyle(Theme.textSecondaryC)
                        }
                    }
                    Spacer()
                    Button { } label: { Label("Delete", systemImage: "trash") }
                        .buttonStyle(.neon(.ghost))
                        .disabled(true)
                } else {
                    CollectedBadge(bytes: collector.totalSize)
                    Button {
                        session.collectorExpanded.toggle()
                    } label: {
                        HStack(spacing: 8) {
                            VStack(alignment: .leading, spacing: 2) {
                                HStack(spacing: 4) {
                                    SizeText(collector.totalSize)
                                    Text("collected")
                                }
                                .font(Theme.display(16, .bold))
                                Text("\(collector.count) item\(collector.count == 1 ? "" : "s") · nothing is deleted until you click Delete")
                                    .font(.system(size: 12, design: .rounded))
                                    .foregroundStyle(Theme.textSecondaryC)
                            }
                            Image(systemName: session.collectorExpanded ? "chevron.down" : "chevron.up")
                                .font(.system(size: 11, weight: .bold))
                                .foregroundStyle(Theme.cyanC)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .help("Review collected items (⌘K)")
                    Spacer()
                    Button("Clear") { collector.clear() }
                        .buttonStyle(.neon(.ghost))
                    Button(role: .destructive) {
                        session.requestDelete()
                    } label: {
                        Label("Delete", systemImage: "trash")
                    }
                    .buttonStyle(.neonDanger)
                    .help("Delete all collected items (⌥⌘⌫)")
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 12)
        .frame(minHeight: 70)
    }

    @ViewBuilder
    private func outcomeView(_ outcome: DeletionOutcome) -> some View {
        Image(systemName: outcome.failureCount == 0 ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
            .font(.system(size: 24))
            .foregroundStyle(outcome.failureCount == 0 ? Theme.mintC : Theme.orangeC)
            .neonGlow(outcome.failureCount == 0 ? Theme.mintC : Theme.orangeC, radius: 5)
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 4) {
                if outcome.mode == .trash {
                    Text("Moved \(outcome.count) item\(outcome.count == 1 ? "" : "s") to the Trash ·")
                    SizeText(outcome.bytes)
                } else {
                    Text("Reaped \(outcome.count) item\(outcome.count == 1 ? "" : "s") · freed")
                    SizeText(outcome.bytes)
                }
            }
            .font(Theme.display(15, .bold))
            Text(outcome.failureCount > 0
                 ? "\(outcome.failureCount) item\(outcome.failureCount == 1 ? "" : "s") couldn't be deleted."
                 : (outcome.mode == .trash ? "Empty the Trash to reclaim the space." : "The space is available right away."))
                .font(.system(size: 12, design: .rounded))
                .foregroundStyle(Theme.textSecondaryC)
        }
        if outcome.canUndo {
            Button("Put Back") { session.undoLastDeletion() }
                .buttonStyle(.neon(.normal, compact: true))
        }
        if outcome.mode == .trash {
            Button("Open Trash") {
                NSWorkspace.shared.open(FileManager.default.urls(for: .trashDirectory, in: .userDomainMask).first
                                        ?? URL(fileURLWithPath: NSHomeDirectory() + "/.Trash"))
            }
            .buttonStyle(.neon(.ghost, compact: true))
        }
        Button { session.dismissOutcome() } label: { Image(systemName: "xmark") }
            .buttonStyle(.plain)
            .foregroundStyle(Theme.textSecondaryC)
    }
}

/// Glowing round total, e.g. "11,1 GB".
private struct CollectedBadge: View {
    let bytes: Int64
    @Environment(AppModel.self) private var model

    var body: some View {
        let parts = ByteFormat.parts(bytes, decimal: model.prefs.decimalUnits)
        ZStack {
            Circle().fill(Theme.magentaC.opacity(0.14))
            Circle().strokeBorder(Theme.hotGradient, lineWidth: 2.5)
            VStack(spacing: -2) {
                Text(parts.number)
                    .font(Theme.display(parts.number.count > 4 ? 12 : 15, .bold))
                    .monospacedDigit()
                    .minimumScaleFactor(0.6)
                Text(parts.unit)
                    .font(.system(size: 9, weight: .semibold, design: .rounded))
                    .foregroundStyle(Theme.textSecondaryC)
            }
            .padding(4)
        }
        .frame(width: 50, height: 50)
        .shadow(color: Theme.magentaC.opacity(0.6), radius: 8)
    }
}

private struct CountdownRing: View {
    let seconds: Int
    let total: Int

    var body: some View {
        ZStack {
            Circle().stroke(Theme.dangerC.opacity(0.2), lineWidth: 4)
            Circle()
                .trim(from: 0, to: CGFloat(seconds) / CGFloat(max(total, 1)))
                .stroke(Theme.dangerC, style: StrokeStyle(lineWidth: 4, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .animation(.linear(duration: 1), value: seconds)
            Text("\(seconds)").font(Theme.display(20, .bold)).foregroundStyle(Theme.dangerC)
        }
        .frame(width: 50, height: 50)
        .shadow(color: Theme.dangerC.opacity(0.7), radius: 8)
    }
}

/// The expanded collector: every collected item with its size; preview, locate, or remove it.
private struct CollectorList: View {
    let session: ScanSession

    var body: some View {
        ScrollView {
            LazyVStack(spacing: 2) {
                ForEach(session.collector.sortedItems) { item in
                    CollectorRow(session: session, item: item)
                }
            }
            .padding(.vertical, 8)
            .padding(.horizontal, 10)
            .padding(.leading, 84)
        }
    }
}

private struct CollectorRow: View {
    let session: ScanSession
    let item: Collector.Item
    @State private var hovering = false

    var body: some View {
        let node = item.node
        HStack(spacing: 10) {
            Image(nsImage: IconCache.icon(for: node))
                .resizable()
                .frame(width: 24, height: 24)
            VStack(alignment: .leading, spacing: 0) {
                Text(node.name).lineLimit(1).truncationMode(.middle)
                    .font(.system(size: 13, weight: .medium, design: .rounded))
                Text(((item.path as NSString).deletingLastPathComponent as NSString).abbreviatingWithTildeInPath)
                    .font(.system(size: 11, design: .rounded))
                    .foregroundStyle(Theme.textTertiaryC)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer(minLength: 12)
            if hovering {
                rowButton("eye", "Quick Look") { QuickLook.shared.toggle(urls: [node.url]) }
                rowButton("list.bullet.indent", "Show in Tree") { session.showInTree(node) }
                rowButton("folder", "Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting([node.url]) }
            }
            SizeText(node.size)
                .font(.system(size: 13, weight: .semibold, design: .rounded))
                .frame(minWidth: 76, alignment: .trailing)
            rowButton("xmark.circle.fill", "Remove from Collector") { session.collector.remove([node]) }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .hoverHighlight(hovering, cornerRadius: 8)
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onDrag { NSItemProvider(object: node.url as NSURL) }
        .contextMenu {
            Button("Quick Look") { QuickLook.shared.toggle(urls: [node.url]) }
            Button("Show in Tree") { session.showInTree(node) }
            Button("Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting([node.url]) }
            Divider()
            Button("Remove from Collector") { session.collector.remove([node]) }
        }
    }

    private func rowButton(_ symbol: String, _ help: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: symbol)
        }
        .buttonStyle(.plain)
        .foregroundStyle(Theme.textSecondaryC)
        .help(help)
    }
}
