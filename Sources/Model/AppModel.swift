import AppKit
import Observation

struct ScanTarget: Equatable {
    let url: URL
    let displayName: String
    /// Set when a whole disk is scanned; lets the results show free, purgeable and hidden space.
    let volumeID: String?
}

@MainActor
@Observable
final class ScanJob {
    let target: ScanTarget
    let expectedBytes: Int64?
    let startDate = Date()
    private(set) var stats = ScanStats()
    @ObservationIgnored let scanner: Scanner
    @ObservationIgnored private var timer: Timer?

    init(target: ScanTarget, expectedBytes: Int64?, dedupeHardLinks: Bool) {
        self.target = target
        self.expectedBytes = expectedBytes
        self.scanner = Scanner(dedupeHardLinks: dedupeHardLinks)
    }

    var fraction: Double? {
        guard let expectedBytes, expectedBytes > 0 else { return nil }
        return min(0.99, Double(stats.bytes) / Double(expectedBytes))
    }

    func start(completion: @escaping @MainActor (FileNode?) -> Void) {
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.stats = self.scanner.snapshot
            }
        }
        let scanner = self.scanner
        let path = target.url.path
        let thread = Thread {
            let root = scanner.scan(path: path)
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    self.timer?.invalidate()
                    self.stats = scanner.snapshot
                    completion(scanner.isCancelled ? nil : root)
                }
            }
        }
        thread.stackSize = 16 * 1024 * 1024
        thread.qualityOfService = .userInitiated
        thread.start()
    }

    func cancel() { scanner.cancel() }
}

@MainActor
@Observable
final class AppModel {
    static let shared = AppModel()

    enum Screen { case home, scanning, results }

    var screen: Screen = .home
    let prefs = Preferences()
    let volumes = VolumeStore()
    private(set) var session: ScanSession?
    private(set) var scanJob: ScanJob?
    var errorMessage: String?
    private(set) var hasFullDiskAccess = SystemAccess.hasFullDiskAccess

    // MARK: - Scanning

    func scan(volume: VolumeInfo) {
        start(ScanTarget(url: volume.url, displayName: volume.name, volumeID: volume.id),
              expectedBytes: volume.used(purgeableAsFree: true))
    }

    func scan(url: URL) {
        let path = url.standardizedFileURL.path
        if let volume = volumes.volumes.first(where: { $0.id == path }) ?? VolumeStore.load().first(where: { $0.id == path }) {
            scan(volume: volume)
            return
        }
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory), isDirectory.boolValue else {
            errorMessage = "“\(url.lastPathComponent)” isn't a folder. Drop a folder or a disk to scan it."
            return
        }
        prefs.noteRecent(path)
        start(ScanTarget(url: URL(fileURLWithPath: path), displayName: FileManager.default.displayName(atPath: path), volumeID: nil),
              expectedBytes: nil)
    }

    func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.treatsFilePackagesAsDirectories = true
        panel.prompt = "Scan"
        panel.message = "Choose a folder to scan"
        if panel.runModal() == .OK, let url = panel.url {
            scan(url: url)
        }
    }

    private func start(_ target: ScanTarget, expectedBytes: Int64?) {
        scanJob?.cancel()
        QuickLook.shared.keyHandler = nil
        let job = ScanJob(target: target, expectedBytes: expectedBytes, dedupeHardLinks: prefs.dedupeHardLinks)
        scanJob = job
        screen = .scanning
        job.start { [weak self] root in
            guard let self, self.scanJob === job else { return }
            self.scanJob = nil
            guard let root else {
                if !job.scanner.isCancelled {
                    self.errorMessage = "BitReaper couldn't read “\(target.displayName)”."
                }
                self.screen = .home
                return
            }
            self.session = ScanSession(root: root, target: target, unreadableCount: job.stats.unreadable,
                                       duration: Date().timeIntervalSince(job.startDate))
            self.screen = .results
            self.volumes.refresh()
        }
    }

    func cancelScan() {
        scanJob?.cancel()
    }

    // MARK: - Navigation

    func goHome() {
        guard screen != .scanning else { return }
        screen = .home
        volumes.refresh()
        refreshAccess()
    }

    func showResults() {
        if session != nil { screen = .results }
    }

    func refreshAccess() {
        hasFullDiskAccess = SystemAccess.hasFullDiskAccess
    }

    /// The active session when the results screen is on screen (used by menu commands).
    var activeSession: ScanSession? { screen == .results ? session : nil }
}
