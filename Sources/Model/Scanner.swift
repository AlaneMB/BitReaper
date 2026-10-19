import Foundation
import Darwin

struct ScanStats {
    var files = 0
    var folders = 0
    var bytes: Int64 = 0
    var unreadable = 0
    var currentPath = ""
}

/// Fast, parallel directory scanner.
///
/// Folders are listed with `readdir` + `fstatat` (no `URL`/`FileManager` overhead) and big subtrees are
/// fanned out over an operation queue. Sizes are physical (allocated blocks), and files with several
/// hard links are counted once.
final class Scanner: @unchecked Sendable {
    private struct InodeKey: Hashable {
        let device: Int32
        let inode: UInt64
    }

    private let lock = NSLock()
    private var stats = ScanStats()
    private var cancelled = false
    private var inFlight = 0
    private let maxInFlight: Int

    private let linkLock = NSLock()
    private var seenLinks = Set<InodeKey>()
    private let dedupeHardLinks: Bool

    private var excluded = Set<String>()
    private let queue = OperationQueue()
    private let group = DispatchGroup()

    init(dedupeHardLinks: Bool = true) {
        self.dedupeHardLinks = dedupeHardLinks
        let cores = ProcessInfo.processInfo.activeProcessorCount
        queue.maxConcurrentOperationCount = max(4, cores * 2)
        queue.qualityOfService = .userInitiated
        maxInFlight = queue.maxConcurrentOperationCount * 4
    }

    var snapshot: ScanStats {
        lock.lock(); defer { lock.unlock() }
        return stats
    }

    var isCancelled: Bool {
        lock.lock(); defer { lock.unlock() }
        return cancelled
    }

    func cancel() {
        lock.lock(); cancelled = true; lock.unlock()
    }

    /// Scans `path` synchronously. Returns nil when cancelled or when the path can't be read.
    /// - Parameters:
    ///   - rootName: name for the returned node (defaults to the absolute path, as a tree root needs).
    ///   - scanRoot: the root of the whole scan, used to decide which mounted volumes to skip.
    func scan(path: String, rootName: String? = nil, scanRoot: String? = nil) -> FileNode? {
        let resolved = Scanner.realPath(path)
        excluded = Scanner.exclusions(forScanRoot: scanRoot.map(Scanner.realPath) ?? resolved, scanning: resolved)

        var st = stat()
        guard lstat(resolved, &st) == 0 else { return nil }
        let isDirectory = (st.st_mode & S_IFMT) == S_IFDIR
        let root = FileNode(name: rootName ?? resolved, parent: nil, flags: isDirectory ? .directory : [])
        root.modified = st.st_mtimespec.tv_sec
        if isDirectory {
            root.size = Int64(st.st_blocks) * 512
            scanDirectory(root, path: resolved)
            group.wait()
        } else {
            root.size = Int64(st.st_blocks) * 512
        }
        if isCancelled { return nil }
        root.finalizeTree()
        return root
    }

    // MARK: - Internals

    private func scanDirectory(_ node: FileNode, path: String) {
        if isCancelled { return }
        guard let dir = opendir(path) else {
            node.flags.insert(.unreadable)
            lock.lock(); stats.unreadable += 1; lock.unlock()
            return
        }
        let fd = dirfd(dir)
        let prefix = path.hasSuffix("/") ? path : path + "/"
        var children: [FileNode] = []
        var subdirectories: [(FileNode, String)] = []
        var fileCount = 0
        var bytes: Int64 = 0

        while let entry = readdir(dir) {
            let name = withUnsafePointer(to: &entry.pointee.d_name) { pointer in
                pointer.withMemoryRebound(to: CChar.self, capacity: Int(MAXNAMLEN) + 1) { String(cString: $0) }
            }
            if name == "." || name == ".." { continue }

            var st = stat()
            guard fstatat(fd, name, &st, AT_SYMLINK_NOFOLLOW) == 0 else { continue }
            let type = st.st_mode & S_IFMT

            if type == S_IFDIR {
                let childPath = prefix + name
                if excluded.contains(childPath) { continue }
                let child = FileNode(name: name, parent: node, flags: .directory)
                child.modified = st.st_mtimespec.tv_sec
                child.size = Int64(st.st_blocks) * 512
                children.append(child)
                subdirectories.append((child, childPath))
            } else {
                var size = Int64(st.st_blocks) * 512
                if dedupeHardLinks && type == S_IFREG && st.st_nlink > 1 && !registerHardLink(st) {
                    size = 0
                }
                let child = FileNode(name: name, parent: node, flags: type == S_IFLNK ? .symlink : [])
                child.modified = st.st_mtimespec.tv_sec
                child.size = size
                children.append(child)
                fileCount += 1
                bytes += size
            }
        }
        closedir(dir)
        node.children = children

        lock.lock()
        stats.files += fileCount
        stats.folders += subdirectories.count
        stats.bytes += bytes
        stats.currentPath = path
        lock.unlock()

        for (child, childPath) in subdirectories {
            if reserveWorker() {
                group.enter()
                queue.addOperation { [self] in
                    scanDirectory(child, path: childPath)
                    releaseWorker()
                    group.leave()
                }
            } else {
                scanDirectory(child, path: childPath)
            }
        }
    }

    private func reserveWorker() -> Bool {
        lock.lock(); defer { lock.unlock() }
        guard inFlight < maxInFlight else { return false }
        inFlight += 1
        return true
    }

    private func releaseWorker() {
        lock.lock(); inFlight -= 1; lock.unlock()
    }

    /// Returns true the first time a hard-linked inode is seen.
    private func registerHardLink(_ st: stat) -> Bool {
        let key = InodeKey(device: st.st_dev, inode: st.st_ino)
        linkLock.lock(); defer { linkLock.unlock() }
        return seenLinks.insert(key).inserted
    }

    // MARK: - Paths and mount points

    static func realPath(_ path: String) -> String {
        guard let resolved = realpath(path, nil) else { return path }
        defer { free(resolved) }
        return String(cString: resolved)
    }

    static func mountPoints() -> [String] {
        var buffer: UnsafeMutablePointer<statfs>?
        let count = getmntinfo(&buffer, MNT_NOWAIT)
        guard count > 0, let buffer else { return [] }
        return (0..<Int(count)).map { index in
            var fs = buffer[index]
            return withUnsafePointer(to: &fs.f_mntonname) { pointer in
                pointer.withMemoryRebound(to: CChar.self, capacity: Int(MAXPATHLEN)) { String(cString: $0) }
            }
        }
    }

    /// Other volumes mounted inside the scanned folder are skipped so each disk is only counted once.
    /// On the startup disk, `/System/Volumes` is skipped too: the Data volume's contents are already
    /// reachable through the firmlinks at `/Users`, `/Applications`, `/Library`, …
    static func exclusions(forScanRoot scanRoot: String, scanning path: String) -> Set<String> {
        var result = Set(mountPoints().filter { $0 != scanRoot && $0 != path && $0 != "/" })
        if scanRoot == "/" { result.insert("/System/Volumes") }
        return result
    }
}
