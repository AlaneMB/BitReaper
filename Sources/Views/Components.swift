import SwiftUI
import AppKit
import Observation
import UniformTypeIdentifiers

/// A size that re-renders when the unit preference changes.
struct SizeText: View {
    let bytes: Int64
    @Environment(AppModel.self) private var model

    init(_ bytes: Int64) { self.bytes = bytes }

    var body: some View {
        Text(ByteFormat.string(bytes, decimal: model.prefs.decimalUnits))
            .monospacedDigit()
    }
}

/// Horizontal neon gauge.
struct UsageGauge: View {
    let fraction: Double
    let colors: [Color]
    var height: CGFloat = 8

    init(fraction: Double, color: Color, height: CGFloat = 8) {
        self.init(fraction: fraction, colors: [color, color.opacity(0.75)], height: height)
    }

    init(fraction: Double, colors: [Color], height: CGFloat = 8) {
        self.fraction = fraction
        self.colors = colors
        self.height = height
    }

    var body: some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Capsule().fill(Theme.lineC.opacity(0.55))
                Capsule()
                    .fill(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                    .frame(width: max(height, proxy.size.width * min(max(fraction, 0), 1)))
                    .shadow(color: (colors.last ?? .clear).opacity(0.7), radius: 5)
            }
        }
        .frame(height: height)
    }
}

struct ColorDot: View {
    let color: Color
    var size: CGFloat = 9

    var body: some View {
        Circle().fill(color).frame(width: size, height: size).shadow(color: color.opacity(0.8), radius: 3)
    }
}

/// Tracks the ⌥ key (holding it counts purgeable space as used on the disk list).
@MainActor
@Observable
final class ModifierKeys {
    static let shared = ModifierKeys()
    private(set) var option = false
    @ObservationIgnored private var monitor: Any?

    private init() {
        monitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { event in
            MainActor.assumeIsolated {
                ModifierKeys.shared.option = event.modifierFlags.contains(.option)
            }
            return event
        }
    }
}

struct FullDiskAccessBanner: View {
    @Environment(AppModel.self) private var model
    var compact = false

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "lock.shield.fill")
                .font(.system(size: compact ? 18 : 26))
                .foregroundStyle(Theme.orangeC)
                .neonGlow(Theme.orangeC, radius: 5)
            VStack(alignment: .leading, spacing: 4) {
                Text("Give BitReaper Full Disk Access for complete results")
                    .font(Theme.display(compact ? 13 : 15, .semibold))
                Text("Without it, macOS hides Mail, Messages, Safari and other apps' data, which then shows up as hidden space. Add BitReaper in System Settings › Privacy & Security › Full Disk Access, then rescan.")
                    .font(.system(size: compact ? 11 : 13, design: .rounded))
                    .foregroundStyle(Theme.textSecondaryC)
                    .fixedSize(horizontal: false, vertical: true)
                HStack {
                    Button("Open System Settings") { SystemAccess.openFullDiskAccessSettings() }
                        .buttonStyle(.neon(.primary, compact: true))
                    Button("Check Again") { model.refreshAccess() }
                        .buttonStyle(.neon(.normal, compact: true))
                }
                .padding(.top, 4)
            }
            Spacer(minLength: 0)
        }
        .padding(compact ? 12 : 16)
        .neonPanel(cornerRadius: 12, glow: Theme.orangeC, fill: Theme.orangeC.opacity(0.08))
    }
}

func loadFileURLs(from providers: [NSItemProvider], completion: @escaping @MainActor ([URL]) -> Void) -> Bool {
    let relevant = providers.filter { $0.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) }
    guard !relevant.isEmpty else { return false }
    let group = DispatchGroup()
    let lock = NSLock()
    var urls: [URL] = []
    for provider in relevant {
        group.enter()
        _ = provider.loadObject(ofClass: URL.self) { url, _ in
            if let url {
                lock.lock(); urls.append(url); lock.unlock()
            }
            group.leave()
        }
    }
    group.notify(queue: .main) {
        MainActor.assumeIsolated { completion(urls) }
    }
    return true
}

extension View {
    @ViewBuilder
    func hoverHighlight(_ isHovering: Bool, cornerRadius: CGFloat = 6) -> some View {
        background(
            RoundedRectangle(cornerRadius: cornerRadius)
                .fill(isHovering ? Theme.violetC.opacity(0.16) : Color.clear)
        )
    }
}
