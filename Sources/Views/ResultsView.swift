import SwiftUI

struct ResultsView: View {
    @Bindable var session: ScanSession
    @Environment(AppModel.self) private var model

    var body: some View {
        VStack(spacing: 10) {
            BreadcrumbBar(session: session)
            HStack(alignment: .top, spacing: 12) {
                VStack(spacing: 0) {
                    ListHeader(session: session)
                    Rectangle().fill(Theme.lineC.opacity(0.6)).frame(height: 1)
                    FileOutlineView(
                        session: session,
                        focusID: ObjectIdentifier(session.focus),
                        treeVersion: session.treeVersion,
                        collectorVersion: session.collector.version,
                        flatVersion: session.flatVersion,
                        isFlat: session.flatResults != nil,
                        revealVersion: session.revealVersion,
                        decimalUnits: model.prefs.decimalUnits
                    )
                    .overlay {
                        if session.flatResults?.isEmpty == true && !session.isQuerying {
                            ContentUnavailableView.search(text: session.searchText)
                        }
                    }
                }
                .clipShape(RoundedRectangle(cornerRadius: 14))
                .neonPanel()
                .frame(minWidth: 560, maxWidth: .infinity, maxHeight: .infinity)
                .layoutPriority(1)

                InfoSidebar(session: session)
                    .frame(width: 330)
                    .frame(maxHeight: .infinity)
            }
            CollectorPanel(session: session)
        }
        .padding(.horizontal, 14)
        .padding(.bottom, 14)
        .padding(.top, 4)
        .overlay(alignment: .bottom) {
            if let toast = session.toast {
                ToastView(message: toast)
                    .padding(.bottom, 104)
                    .transition(.move(edge: .bottom).combined(with: .opacity))
            }
        }
        .animation(.easeOut(duration: 0.2), value: session.toast)
        .toolbar { toolbar }
        .sheet(isPresented: $session.showDeleteSheet) {
            DeleteSheet(session: session)
        }
        .sheet(item: $session.failureReport) { report in
            FailureSheet(report: report)
        }
    }

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItem(placement: .navigation) {
            BrandMark()
                .padding(.leading, 4)
        }
        ToolbarItem(placement: .principal) {
            NeonSegmented(options: [
                (ScanSession.ViewMode.tree, "Tree View", "list.bullet.indent"),
                (ScanSession.ViewMode.largest, "Largest Files", "chart.bar.fill"),
            ], selection: $session.viewMode)
            .help("Tree (⌘1) or the largest files in this folder (⌘2)")
        }
        ToolbarItemGroup(placement: .primaryAction) {
            Button { session.rescan() } label: { Image(systemName: "arrow.clockwise") }
                .buttonStyle(.neonIcon)
                .disabled(session.isRescanning)
                .help("Rescan this folder (⌘R)")
            Button { model.goHome() } label: { Image(systemName: "internaldrive") }
                .buttonStyle(.neonIcon)
                .help("Disks and Folders (⇧⌘D)")
            NeonSearchField(text: $session.searchText,
                            prompt: "Search in \(session.displayName(of: session.focus))",
                            focusRequest: session.searchFocusRequest)
                .frame(width: 260)
        }
    }
}

/// Toolbar search field in the neon style. Esc clears it.
struct NeonSearchField: View {
    @Binding var text: String
    let prompt: String
    let focusRequest: Int
    @FocusState private var focused: Bool

    var body: some View {
        HStack(spacing: 7) {
            Image(systemName: "magnifyingglass")
                .foregroundStyle(focused ? Theme.cyanC : Theme.textSecondaryC)
            TextField(prompt, text: $text)
                .textFieldStyle(.plain)
                .focused($focused)
                .onExitCommand { text = ""; focused = false }
            if text.isEmpty {
                KeyHint(text: "⌘F")
            } else {
                Button { text = "" } label: {
                    Image(systemName: "xmark.circle.fill").foregroundStyle(Theme.textSecondaryC)
                }
                .buttonStyle(.plain)
            }
        }
        .font(.system(size: 13, design: .rounded))
        .padding(.horizontal, 10)
        .padding(.vertical, 7)
        .background(Theme.panelC.opacity(0.95), in: RoundedRectangle(cornerRadius: 10))
        .overlay(
            RoundedRectangle(cornerRadius: 10)
                .strokeBorder(focused ? AnyShapeStyle(Theme.neonBorder) : AnyShapeStyle(Theme.lineC), lineWidth: focused ? 1.5 : 1)
        )
        .shadow(color: Theme.cyanC.opacity(focused ? 0.45 : 0), radius: 8)
        .onChange(of: focusRequest) { focused = true }
    }
}

/// Card header above the tree: the current folder, its size, and the disk format for volumes.
struct ListHeader: View {
    let session: ScanSession
    @Environment(AppModel.self) private var model

    var body: some View {
        let _ = session.treeVersion
        let focus = session.focus
        let isVolumeRoot = focus === session.root && session.target.volumeID != nil
        HStack(spacing: 12) {
            Image(nsImage: isVolumeRoot ? NSWorkspace.shared.icon(forFile: session.target.url.path) : IconCache.icon(for: focus))
                .resizable()
                .frame(width: 36, height: 36)
            VStack(alignment: .leading, spacing: 1) {
                Text(session.displayName(of: focus))
                    .font(Theme.display(16, .semibold))
                    .lineLimit(1)
                Group {
                    if let results = session.flatResults {
                        let query = session.searchText.trimmingCharacters(in: .whitespaces)
                        if query.isEmpty {
                            Text("\(ByteFormat.count(results.count)) largest files")
                        } else {
                            Text("\(ByteFormat.count(results.count)) matches for “\(query)”")
                        }
                    } else {
                        Text("\(ByteFormat.count(focus.itemCount)) items")
                    }
                }
                .font(.system(size: 12, design: .rounded))
                .foregroundStyle(Theme.textSecondaryC)
            }
            if session.isQuerying || session.isRescanning {
                ProgressView().controlSize(.small).padding(.leading, 4).tint(Theme.cyanC)
                Text(session.isRescanning ? "Rescanning…" : "Searching…")
                    .font(.caption)
                    .foregroundStyle(Theme.cyanC)
            }
            Spacer()
            VStack(alignment: .trailing, spacing: 1) {
                SizeText(focus.size)
                    .font(Theme.display(22, .bold))
                    .neonGlow(Theme.violetC, radius: 6)
                if isVolumeRoot, let volume = session.volume {
                    Text([volume.format, volume.isInternal ? "Internal" : "External"].filter { !$0.isEmpty }.joined(separator: " · "))
                        .font(.system(size: 11, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
    }
}

struct ToastView: View {
    let message: String

    var body: some View {
        Label(message, systemImage: "sparkles")
            .font(.system(size: 13, weight: .medium, design: .rounded))
            .foregroundStyle(Theme.textC)
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(Theme.panelRaisedC, in: Capsule())
            .overlay(Capsule().strokeBorder(Theme.neonBorder, lineWidth: 1.2))
            .shadow(color: Theme.magentaC.opacity(0.4), radius: 12)
            .frame(maxWidth: 640)
    }
}
