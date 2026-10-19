import AppKit
import UniformTypeIdentifiers

/// Icons are looked up per extension for plain files (fast) and per path for bundles (apps, libraries).
enum IconCache {
    private static var byExtension: [String: NSImage] = [:]
    private static let byPath = NSCache<NSString, NSImage>()
    private static let folder = NSWorkspace.shared.icon(for: .folder)
    private static let genericFile = NSWorkspace.shared.icon(for: .data)
    private static let symlink = NSWorkspace.shared.icon(for: .symbolicLink)

    static func icon(for node: FileNode) -> NSImage {
        if node.isDirectory {
            guard !node.fileExtension.isEmpty else { return folder }
            let key = node.path as NSString
            if let cached = byPath.object(forKey: key) { return cached }
            let image = NSWorkspace.shared.icon(forFile: node.path)
            byPath.setObject(image, forKey: key)
            return image
        }
        if node.flags.contains(.symlink) { return symlink }
        let ext = node.fileExtension
        guard !ext.isEmpty else { return genericFile }
        if let cached = byExtension[ext] { return cached }
        let image = UTType(filenameExtension: ext).map { NSWorkspace.shared.icon(for: $0) } ?? genericFile
        byExtension[ext] = image
        return image
    }

    static func kind(of node: FileNode) -> String {
        if node.flags.contains(.symlink) { return "Symbolic link" }
        let ext = node.fileExtension
        if let type = ext.isEmpty ? nil : UTType(filenameExtension: ext), let description = type.localizedDescription {
            if node.isDirectory && !type.conforms(to: .package) && !type.conforms(to: .bundle) { return "Folder" }
            return description.prefix(1).uppercased() + description.dropFirst()
        }
        return node.isDirectory ? "Folder" : "Document"
    }
}
