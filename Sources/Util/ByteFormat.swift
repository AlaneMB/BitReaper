import Foundation

enum ByteFormat {
    private static let formatter: NumberFormatter = {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.minimumFractionDigits = 0
        formatter.maximumFractionDigits = 1
        return formatter
    }()

    private static let countFormatter: NumberFormatter = {
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        return formatter
    }()

    static var decimalUnits: Bool {
        UserDefaults.standard.object(forKey: PreferenceKey.decimalUnits) as? Bool ?? true
    }

    /// Splits a size into a number and a unit, e.g. ("261.5", "GB").
    static func parts(_ bytes: Int64, decimal: Bool = decimalUnits) -> (number: String, unit: String) {
        let base: Double = decimal ? 1000 : 1024
        let units = decimal
            ? ["bytes", "KB", "MB", "GB", "TB", "PB", "EB"]
            : ["bytes", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"]
        var value = Double(max(bytes, 0))
        var index = 0
        while value >= base && index < units.count - 1 {
            value /= base
            index += 1
        }
        if index == 0 {
            return (countFormatter.string(from: NSNumber(value: bytes)) ?? "\(bytes)", bytes == 1 ? "byte" : "bytes")
        }
        return (formatter.string(from: NSNumber(value: value)) ?? String(format: "%.1f", value), units[index])
    }

    static func string(_ bytes: Int64, decimal: Bool = decimalUnits) -> String {
        let parts = parts(bytes, decimal: decimal)
        return "\(parts.number) \(parts.unit)"
    }

    static func count(_ value: Int) -> String {
        countFormatter.string(from: NSNumber(value: value)) ?? "\(value)"
    }

    static func percent(_ part: Int64, of whole: Int64) -> String {
        guard whole > 0 else { return "—" }
        let fraction = Double(part) / Double(whole) * 100
        if part > 0 && fraction < 0.1 { return "<0.1 %" }
        return String(format: fraction >= 10 ? "%.0f %%" : "%.1f %%", fraction)
    }

    private static let dateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.dateStyle = .medium
        formatter.timeStyle = .none
        formatter.doesRelativeDateFormatting = true
        return formatter
    }()

    static func date(_ date: Date) -> String { dateFormatter.string(from: date) }
}
