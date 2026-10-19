import SwiftUI

@main
struct BitReaperApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @State private var model = AppModel.shared

    var body: some Scene {
        Window("BitReaper", id: "main") {
            RootView()
                .environment(model)
                .frame(minWidth: 1000, minHeight: 620)
                .preferredColorScheme(.dark)
                .tint(Theme.magentaC)
        }
        .defaultSize(width: 1240, height: 800)
        .windowToolbarStyle(.unified(showsTitle: false))
        .commands { BitReaperCommands(model: model) }

        Settings {
            SettingsView()
                .environment(model)
                .preferredColorScheme(.dark)
                .tint(Theme.magentaC)
        }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationWillFinishLaunching(_ notification: Notification) {
        // The synthwave theme is designed for a dark appearance only.
        NSApp.appearance = NSAppearance(named: .darkAqua)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    /// Folders and disks dropped on the Dock icon (or opened with "Open With") are scanned.
    func application(_ application: NSApplication, open urls: [URL]) {
        guard let url = urls.first else { return }
        AppModel.shared.scan(url: url)
    }
}

struct BitReaperCommands: Commands {
    let model: AppModel

    var body: some Commands {
        let session = model.activeSession

        CommandGroup(replacing: .newItem) {
            Button("Scan Folder…") { model.chooseFolder() }
                .keyboardShortcut("o")
                .disabled(model.screen == .scanning)
            Button("Disks and Folders") { model.goHome() }
                .keyboardShortcut("d", modifiers: [.command, .shift])
                .disabled(model.screen == .scanning)
            Button("Back to Results") { model.showResults() }
                .disabled(model.session == nil || model.screen != .home)
            Divider()
            Button("Rescan Folder") { session?.rescan() }
                .keyboardShortcut("r")
                .disabled(session == nil || session?.isRescanning == true)
        }

        CommandGroup(after: .textEditing) {
            Button("Find…") { session?.requestSearchFocus() }
                .keyboardShortcut("f")
                .disabled(session == nil)
        }

        CommandMenu("Go") {
            Button("Back") { session?.goBack() }
                .keyboardShortcut("[")
                .disabled(!(session?.canGoBack ?? false))
            Button("Forward") { session?.goForward() }
                .keyboardShortcut("]")
                .disabled(!(session?.canGoForward ?? false))
            Button("Enclosing Folder") { session?.goUp() }
                .keyboardShortcut(.upArrow)
                .disabled(!(session?.canGoUp ?? false))
            Button("Zoom Into Selection") { session?.zoomIntoSelection() }
                .keyboardShortcut(.downArrow)
                .disabled(!(session?.selection.contains(where: \.isDirectory) ?? false))
            Divider()
            Button("Tree") { session?.viewMode = .tree }
                .keyboardShortcut("1")
                .disabled(session == nil)
            Button("Largest Files") { session?.viewMode = .largest }
                .keyboardShortcut("2")
                .disabled(session == nil)
        }

        CommandMenu("Collector") {
            Button(session?.selectionIsCollected == true ? "Remove Selection from Collector" : "Add Selection to Collector") {
                if let session { session.toggleCollect(session.selection) }
            }
            .keyboardShortcut(.delete)
            .disabled(session?.selection.isEmpty ?? true)
            Button(session?.collectorExpanded == true ? "Hide Collected Items" : "Show Collected Items") {
                session?.collectorExpanded.toggle()
            }
            .keyboardShortcut("k")
            .disabled(session?.collector.isEmpty ?? true)
            Button("Clear Collector") { session?.collector.clear() }
                .disabled(session?.collector.isEmpty ?? true)
            Divider()
            Button("Delete Collected Items…") { session?.requestDelete() }
                .keyboardShortcut(.delete, modifiers: [.command, .option])
                .disabled(session?.collector.isEmpty ?? true)
            Button("Put Back Last Deletion") { session?.undoLastDeletion() }
                .disabled(!(session?.lastOutcome?.canUndo ?? false))
        }
    }
}
