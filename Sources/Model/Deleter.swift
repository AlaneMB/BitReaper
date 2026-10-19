import Foundation
import Darwin

enum DeleteResult: Sendable {
    case removed
    case trashed(URL?)
    case failed(String)
}

enum Deleter {
    /// Deletes (or trashes) each path in turn. Runs off the main thread.
    static func delete(paths: [String], mode: DeleteMode, progress: @Sendable (Int) -> Void) -> [DeleteResult] {
        var results: [DeleteResult] = []
        results.reserveCapacity(paths.count)
        for (index, path) in paths.enumerated() {
            let url = URL(fileURLWithPath: path)
            do {
                switch mode {
                case .trash:
                    results.append(.trashed(try trash(url)))
                case .permanent:
                    try removeForcefully(url)
                    results.append(.removed)
                }
            } catch {
                results.append(.failed(describe(error)))
            }
            progress(index + 1)
        }
        return results
    }

    /// Moving a folder needs write permission on the folder itself (its ".." entry changes), so a
    /// read-only folder you own is made writable and retried, as Finder does.
    private static func trash(_ url: URL) throws -> URL? {
        var resulting: NSURL?
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: &resulting)
        } catch {
            var st = stat()
            guard lstat(url.path, &st) == 0, st.st_uid == getuid() else { throw error }
            makeWritable(url.path)
            try FileManager.default.trashItem(at: url, resultingItemURL: &resulting)
        }
        return resulting as URL?
    }

    /// `removeItem` fails on read-only folders (Go module caches, some git objects, locked files);
    /// on failure, make the subtree writable and unlocked, then try again.
    private static func removeForcefully(_ url: URL) throws {
        do {
            try FileManager.default.removeItem(at: url)
        } catch {
            guard FileManager.default.fileExists(atPath: url.path) || isSymlink(url.path) else { return }
            makeWritable(url.path)
            if let enumerator = FileManager.default.enumerator(atPath: url.path) {
                for case let relative as String in enumerator {
                    makeWritable(url.path + "/" + relative)
                }
            }
            try FileManager.default.removeItem(at: url)
        }
    }

    private static func isSymlink(_ path: String) -> Bool {
        var st = stat()
        return lstat(path, &st) == 0 && (st.st_mode & S_IFMT) == S_IFLNK
    }

    private static func makeWritable(_ path: String) {
        var st = stat()
        guard lstat(path, &st) == 0, (st.st_mode & S_IFMT) != S_IFLNK else { return }
        if st.st_flags & UInt32(UF_IMMUTABLE | UF_APPEND) != 0 {
            _ = chflags(path, st.st_flags & ~UInt32(UF_IMMUTABLE | UF_APPEND))
        }
        if st.st_mode & S_IWUSR == 0 {
            _ = chmod(path, (st.st_mode & 0o7777) | S_IWUSR | ((st.st_mode & S_IFMT) == S_IFDIR ? S_IXUSR | S_IRUSR : 0))
        }
    }

    private static func describe(_ error: Error) -> String {
        let nsError = error as NSError
        if let underlying = nsError.userInfo[NSUnderlyingErrorKey] as? NSError,
           underlying.domain == NSPOSIXErrorDomain {
            switch Int32(underlying.code) {
            case EPERM, EACCES: return "Permission denied. The item may be protected by macOS or owned by another user."
            case EBUSY: return "The item is in use."
            default: break
            }
        }
        return nsError.localizedDescription
    }
}
