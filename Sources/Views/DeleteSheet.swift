import SwiftUI

struct DeleteSheet: View {
    let session: ScanSession
    @Environment(AppModel.self) private var model
    @State private var mode: DeleteMode = AppModel.shared.prefs.deleteMode

    var body: some View {
        let collector = session.collector
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 14) {
                Image("ReaperFolder")
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(width: 70)
                    .neonGlow(mode == .trash ? Theme.magentaC : Theme.dangerC, radius: 8)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Delete \(collector.count) collected item\(collector.count == 1 ? "" : "s")?")
                        .font(Theme.display(22, .bold))
                    HStack(spacing: 4) {
                        Text("Total:")
                        SizeText(collector.totalSize).fontWeight(.semibold)
                    }
                    .foregroundStyle(Theme.textSecondaryC)
                }
            }

            List(collector.sortedItems) { item in
                HStack(spacing: 8) {
                    Image(nsImage: IconCache.icon(for: item.node)).resizable().frame(width: 18, height: 18)
                    VStack(alignment: .leading, spacing: 0) {
                        Text(item.node.name).lineLimit(1).truncationMode(.middle)
                        Text((item.path as NSString).abbreviatingWithTildeInPath)
                            .font(.caption)
                            .foregroundStyle(Theme.textSecondaryC)
                            .lineLimit(1)
                            .truncationMode(.middle)
                    }
                    Spacer()
                    SizeText(item.node.size).foregroundStyle(Theme.textSecondaryC)
                }
            }
            .frame(height: 220)
            .scrollContentBackground(.hidden)
            .listStyle(.plain)
            .neonPanel(cornerRadius: 10)

            Picker("Method", selection: $mode) {
                ForEach(DeleteMode.allCases) { Text($0.title).tag($0) }
            }
            .pickerStyle(.radioGroup)
            .horizontalRadioGroupLayout()

            Text(mode == .trash
                 ? "Items go to the Trash. You can put them back right after; the space is reclaimed when the Trash is emptied."
                 : "Items are removed permanently and the space is reclaimed right away. This can't be undone.")
                .font(.callout)
                .foregroundStyle(mode == .permanent ? Theme.dangerC : Theme.textSecondaryC)
                .fixedSize(horizontal: false, vertical: true)

            if model.prefs.countdownSeconds > 0 {
                Text("You'll have \(model.prefs.countdownSeconds) seconds to cancel after confirming.")
                    .font(.caption)
                    .foregroundStyle(Theme.textSecondaryC)
            }

            HStack {
                Toggle("Don't ask again", isOn: Binding(
                    get: { !model.prefs.confirmDelete },
                    set: { model.prefs.confirmDelete = !$0 }
                ))
                .toggleStyle(.checkbox)
                .font(.callout)
                Spacer()
                Button("Cancel", role: .cancel) { session.showDeleteSheet = false }
                    .keyboardShortcut(.cancelAction)
                    .buttonStyle(.neon)
                Button(mode == .trash ? "Move to Trash" : "Delete Permanently", role: .destructive) {
                    session.confirmDelete(mode: mode)
                }
                .keyboardShortcut(.defaultAction)
                .buttonStyle(.neonDanger)
            }
        }
        .padding(24)
        .frame(width: 580)
        .background(SynthwaveBackground(showsSkyline: false))
        .foregroundStyle(Theme.textC)
    }
}

struct FailureSheet: View {
    let report: FailureReport
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Label("\(report.failures.count) item\(report.failures.count == 1 ? "" : "s") couldn't be deleted",
                  systemImage: "exclamationmark.triangle.fill")
                .font(.title3.weight(.semibold))
                .foregroundStyle(Theme.orangeC)
            List(report.failures) { failure in
                VStack(alignment: .leading, spacing: 2) {
                    Text((failure.path as NSString).abbreviatingWithTildeInPath)
                        .lineLimit(2)
                        .truncationMode(.middle)
                        .textSelection(.enabled)
                    Text(failure.reason).font(.caption).foregroundStyle(Theme.textSecondaryC)
                }
                .padding(.vertical, 2)
            }
            .frame(height: 200)
            .scrollContentBackground(.hidden)
            .listStyle(.plain)
            .neonPanel(cornerRadius: 10)
            Text("They are still in the collector. Items protected by macOS can't be removed; others may need Full Disk Access or an administrator.")
                .font(.callout)
                .foregroundStyle(Theme.textSecondaryC)
                .fixedSize(horizontal: false, vertical: true)
            HStack {
                Spacer()
                Button("OK") { dismiss() }
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(.neonPrimary)
            }
        }
        .padding(22)
        .frame(width: 540)
        .background(SynthwaveBackground(showsSkyline: false))
        .foregroundStyle(Theme.textC)
    }
}
