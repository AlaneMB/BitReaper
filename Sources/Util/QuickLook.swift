import AppKit
import Quartz

/// Shared Quick Look controller, used by the tree, the sidebar and the collector.
final class QuickLook: NSObject, QLPreviewPanelDataSource, QLPreviewPanelDelegate {
    static let shared = QuickLook()

    private var urls: [URL] = []
    /// Receives key presses made while the panel is key (arrows, space), so you can keep browsing.
    weak var keyHandler: NSResponder?

    var isVisible: Bool { QLPreviewPanel.sharedPreviewPanelExists() && QLPreviewPanel.shared().isVisible }

    func toggle(urls: [URL], keyHandler: NSResponder? = nil) {
        guard let panel = QLPreviewPanel.shared() else { return }
        if panel.isVisible {
            panel.orderOut(nil)
            return
        }
        guard !urls.isEmpty else { return }
        self.urls = urls
        self.keyHandler = keyHandler
        panel.dataSource = self
        panel.delegate = self
        panel.reloadData()
        panel.makeKeyAndOrderFront(nil)
    }

    func updateIfVisible(urls: [URL]) {
        guard isVisible, !urls.isEmpty, let panel = QLPreviewPanel.shared() else { return }
        self.urls = urls
        panel.reloadData()
    }

    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { urls.count }

    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! {
        urls[index] as NSURL
    }

    func previewPanel(_ panel: QLPreviewPanel!, handle event: NSEvent!) -> Bool {
        guard event.type == .keyDown, let keyHandler else { return false }
        keyHandler.keyDown(with: event)
        return true
    }
}
