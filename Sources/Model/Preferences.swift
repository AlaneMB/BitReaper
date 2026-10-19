import Foundation
import Observation

enum DeleteMode: String, CaseIterable, Identifiable, Sendable {
    case trash
    case permanent

    var id: String { rawValue }

    var title: String {
        switch self {
        case .trash: return "Move to Trash"
        case .permanent: return "Delete Immediately"
        }
    }
}

enum PreferenceKey {
    static let decimalUnits = "decimalUnits"
    static let deleteMode = "deleteMode"
    static let confirmDelete = "confirmDelete"
    static let countdownSeconds = "countdownSeconds"
    static let dedupeHardLinks = "dedupeHardLinks"
    static let starredFolders = "starredFolders"
    static let recentFolders = "recentFolders"
}

@MainActor
@Observable
final class Preferences {
    @ObservationIgnored private let defaults = UserDefaults.standard

    /// 1 KB = 1000 bytes (like Finder) when true, 1 KiB = 1024 bytes otherwise.
    var decimalUnits: Bool { didSet { defaults.set(decimalUnits, forKey: PreferenceKey.decimalUnits) } }
    var deleteMode: DeleteMode { didSet { defaults.set(deleteMode.rawValue, forKey: PreferenceKey.deleteMode) } }
    var confirmDelete: Bool { didSet { defaults.set(confirmDelete, forKey: PreferenceKey.confirmDelete) } }
    /// Safety countdown: seconds during which a started deletion can still be cancelled.
    var countdownSeconds: Int { didSet { defaults.set(countdownSeconds, forKey: PreferenceKey.countdownSeconds) } }
    var dedupeHardLinks: Bool { didSet { defaults.set(dedupeHardLinks, forKey: PreferenceKey.dedupeHardLinks) } }
    var starredFolders: [String] { didSet { defaults.set(starredFolders, forKey: PreferenceKey.starredFolders) } }
    var recentFolders: [String] { didSet { defaults.set(recentFolders, forKey: PreferenceKey.recentFolders) } }

    init() {
        defaults.register(defaults: [
            PreferenceKey.decimalUnits: true,
            PreferenceKey.deleteMode: DeleteMode.trash.rawValue,
            PreferenceKey.confirmDelete: true,
            PreferenceKey.countdownSeconds: 5,
            PreferenceKey.dedupeHardLinks: true,
        ])
        decimalUnits = defaults.bool(forKey: PreferenceKey.decimalUnits)
        deleteMode = DeleteMode(rawValue: defaults.string(forKey: PreferenceKey.deleteMode) ?? "") ?? .trash
        confirmDelete = defaults.bool(forKey: PreferenceKey.confirmDelete)
        countdownSeconds = defaults.integer(forKey: PreferenceKey.countdownSeconds)
        dedupeHardLinks = defaults.bool(forKey: PreferenceKey.dedupeHardLinks)
        starredFolders = defaults.stringArray(forKey: PreferenceKey.starredFolders) ?? []
        recentFolders = defaults.stringArray(forKey: PreferenceKey.recentFolders) ?? []
    }

    func isStarred(_ path: String) -> Bool { starredFolders.contains(path) }

    func toggleStar(_ path: String) {
        if let index = starredFolders.firstIndex(of: path) {
            starredFolders.remove(at: index)
        } else {
            starredFolders.append(path)
            recentFolders.removeAll { $0 == path }
        }
    }

    func noteRecent(_ path: String) {
        guard !isStarred(path) else { return }
        var recents = recentFolders.filter { $0 != path }
        recents.insert(path, at: 0)
        recentFolders = Array(recents.prefix(6))
    }

    func forget(_ path: String) {
        recentFolders.removeAll { $0 == path }
        starredFolders.removeAll { $0 == path }
    }
}
