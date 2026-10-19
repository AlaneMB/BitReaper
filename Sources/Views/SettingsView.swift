import SwiftUI

struct SettingsView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        @Bindable var prefs = model.prefs
        Form {
            Section("Sizes") {
                Picker("Units", selection: $prefs.decimalUnits) {
                    Text("Decimal — 1 KB = 1,000 bytes (like Finder)").tag(true)
                    Text("Binary — 1 KiB = 1,024 bytes").tag(false)
                }
                Toggle("Count hard-linked files only once", isOn: $prefs.dedupeHardLinks)
                    .help("Matches the space files really take. Applies to the next scan.")
            }
            Section("Deleting") {
                Picker("Default method", selection: $prefs.deleteMode) {
                    ForEach(DeleteMode.allCases) { Text($0.title).tag($0) }
                }
                Toggle("Review items before deleting", isOn: $prefs.confirmDelete)
                Picker("Safety countdown", selection: $prefs.countdownSeconds) {
                    Text("Off").tag(0)
                    Text("3 seconds").tag(3)
                    Text("5 seconds").tag(5)
                    Text("10 seconds").tag(10)
                }
                .help("Time during which a started deletion can still be cancelled.")
            }
            Section("Access") {
                LabeledContent("Full Disk Access") {
                    HStack {
                        Image(systemName: model.hasFullDiskAccess ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                            .foregroundStyle(model.hasFullDiskAccess ? Theme.mintC : Theme.orangeC)
                        Text(model.hasFullDiskAccess ? "Granted" : "Not granted")
                    }
                }
                HStack {
                    Button("Open Privacy Settings") { SystemAccess.openFullDiskAccessSettings() }
                        .buttonStyle(.neon(.primary, compact: true))
                    Button("Check Again") { model.refreshAccess() }
                        .buttonStyle(.neon(.normal, compact: true))
                }
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.hidden)
        .background(SynthwaveBackground(showsSkyline: false))
        .frame(width: 500)
        .fixedSize(horizontal: false, vertical: true)
        .onAppear { model.refreshAccess() }
    }
}
