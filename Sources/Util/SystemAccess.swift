import AppKit
import Darwin

enum SystemAccess {
    /// Full Disk Access is required to read Mail, Messages, Safari, other apps' containers, etc.
    /// The TCC database is only readable by processes that have it.
    static var hasFullDiskAccess: Bool {
        let probe = "/Library/Application Support/com.apple.TCC/TCC.db"
        let fd = open(probe, O_RDONLY)
        if fd >= 0 {
            close(fd)
            return true
        }
        return false
    }

    static func openFullDiskAccessSettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AllFiles") {
            NSWorkspace.shared.open(url)
        }
    }

    private static let protectedExactPaths: Set<String> = {
        let home = NSHomeDirectory()
        var paths: Set<String> = [
            "/", "/System", "/Library", "/Applications", "/Users", "/Volumes", "/Network",
            "/bin", "/sbin", "/usr", "/etc", "/var", "/tmp", "/dev", "/opt", "/cores",
            "/private", "/private/var", "/private/etc", "/private/tmp",
            "/Library/Application Support", "/Library/Caches", "/Library/Preferences",
            "/usr/local", "/opt/homebrew",
            home,
        ]
        for folder in ["Library", "Desktop", "Documents", "Downloads", "Pictures", "Movies", "Music",
                       "Applications", "Public", "Library/Application Support", "Library/Caches",
                       "Library/Preferences", "Library/Containers", "Library/Group Containers",
                       "Library/Mobile Documents"] {
            paths.insert(home + "/" + folder)
        }
        return paths
    }()

    /// Returns a user-facing reason when `path` must never be collected for deletion.
    static func protectionReason(for path: String) -> String? {
        let name = FileManager.default.displayName(atPath: path)
        if protectedExactPaths.contains(path) {
            return "“\(name)” is an essential folder and can't be collected. Open it and collect what's inside instead."
        }
        if (path as NSString).deletingLastPathComponent == "/Users" {
            return "“\(name)” is a user's home folder and can't be collected."
        }
        let systemPrefixes = ["/System/", "/bin/", "/sbin/", "/private/var/db/", "/private/var/vm/", "/Library/Apple/"]
        if systemPrefixes.contains(where: path.hasPrefix) || (path.hasPrefix("/usr/") && !path.hasPrefix("/usr/local/")) {
            return "“\(name)” is part of macOS and can't be collected."
        }
        if Scanner.mountPoints().contains(path) {
            return "“\(name)” is a disk. Open it and collect what's inside instead."
        }
        return nil
    }

    /// Local APFS (Time Machine) snapshots on a volume. They show up as hidden/purgeable space.
    static func localSnapshots(onVolume path: String) async -> [String] {
        await Task.detached(priority: .utility) {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/tmutil")
            process.arguments = ["listlocalsnapshots", path]
            let pipe = Pipe()
            process.standardOutput = pipe
            process.standardError = Pipe()
            do {
                try process.run()
            } catch {
                return []
            }
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            let output = String(decoding: data, as: UTF8.self)
            return output.split(separator: "\n").map(String.init).filter { $0.hasPrefix("com.apple.") }
        }.value
    }
}
