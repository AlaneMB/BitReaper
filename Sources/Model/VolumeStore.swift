import AppKit
import Observation

struct VolumeInfo: Identifiable, Equatable, Sendable {
    let id: String          // mount path
    let url: URL
    let name: String
    let format: String
    let total: Int64
    let available: Int64            // strictly free
    let availableImportant: Int64   // free + purgeable
    let isRoot: Bool
    let isInternal: Bool
    let isRemovable: Bool
    let isLocal: Bool

    var purgeable: Int64 { max(0, availableImportant - available) }

    func free(purgeableAsFree: Bool) -> Int64 { purgeableAsFree ? availableImportant : available }
    func used(purgeableAsFree: Bool) -> Int64 { max(0, total - free(purgeableAsFree: purgeableAsFree)) }

    var symbolName: String {
        if !isLocal { return "server.rack" }
        if isRemovable || !isInternal { return "externaldrive.fill" }
        return "internaldrive.fill"
    }
}

@MainActor
@Observable
final class VolumeStore {
    private(set) var volumes: [VolumeInfo] = []
    @ObservationIgnored private var observers: [NSObjectProtocol] = []

    init() {
        volumes = Self.load()
        let center = NSWorkspace.shared.notificationCenter
        for name in [NSWorkspace.didMountNotification, NSWorkspace.didUnmountNotification, NSWorkspace.didRenameVolumeNotification] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.refresh() }
            })
        }
    }

    func refresh() {
        Task {
            let loaded = await Task.detached(priority: .userInitiated) { Self.load() }.value
            if loaded != volumes { volumes = loaded }
        }
    }

    func volume(id: String) -> VolumeInfo? { volumes.first { $0.id == id } }

    nonisolated static func load() -> [VolumeInfo] {
        let keys: [URLResourceKey] = [
            .volumeNameKey, .volumeLocalizedNameKey, .volumeLocalizedFormatDescriptionKey,
            .volumeTotalCapacityKey, .volumeAvailableCapacityKey, .volumeAvailableCapacityForImportantUsageKey,
            .volumeIsRootFileSystemKey, .volumeIsInternalKey, .volumeIsRemovableKey, .volumeIsLocalKey,
            .volumeIsBrowsableKey,
        ]
        let urls = FileManager.default.mountedVolumeURLs(includingResourceValuesForKeys: keys, options: [.skipHiddenVolumes]) ?? []
        var result: [VolumeInfo] = []
        for url in urls {
            guard let values = try? url.resourceValues(forKeys: Set(keys)) else { continue }
            guard values.volumeIsBrowsable ?? true, let total = values.volumeTotalCapacity, total > 0 else { continue }
            let available = Int64(values.volumeAvailableCapacity ?? 0)
            let important = values.volumeAvailableCapacityForImportantUsage ?? available
            result.append(VolumeInfo(
                id: url.path,
                url: url,
                name: values.volumeLocalizedName ?? values.volumeName ?? url.lastPathComponent,
                format: values.volumeLocalizedFormatDescription ?? "",
                total: Int64(total),
                available: available,
                availableImportant: max(important, available),
                isRoot: values.volumeIsRootFileSystem ?? (url.path == "/"),
                isInternal: values.volumeIsInternal ?? false,
                isRemovable: values.volumeIsRemovable ?? false,
                isLocal: values.volumeIsLocal ?? true
            ))
        }
        return result.sorted { lhs, rhs in
            if lhs.isRoot != rhs.isRoot { return lhs.isRoot }
            if lhs.isInternal != rhs.isInternal { return lhs.isInternal }
            return lhs.name.localizedStandardCompare(rhs.name) == .orderedAscending
        }
    }
}
