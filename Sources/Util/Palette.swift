import AppKit
import SwiftUI

/// Item colours. Every top-level item of the current folder gets a neon colour slot by size rank
/// (pink, blue, cyan, purple, yellow, …); its descendants inherit it, a little paler at each level.
enum Palette {
    static let neon: [NSColor] = [Theme.pink, Theme.blue, Theme.cyan, Theme.purple, Theme.yellow,
                                  Theme.orange, Theme.magenta, Theme.mint]
    /// Items past the named slots share a calm blue, like the "smaller objects" in the overview.
    static let minor = NSColor(hex: 0x4F6BFF)

    static func nsColor(slot: Int?, depth: Int = 0) -> NSColor {
        guard let slot else { return Theme.textTertiary }
        let base = slot < neon.count ? neon[slot] : minor
        let fade = min(CGFloat(depth) * 0.13, 0.45)
        return base.blended(withFraction: fade, of: Theme.text) ?? base
    }

    static func color(slot: Int?, depth: Int = 0) -> Color {
        Color(nsColor: nsColor(slot: slot, depth: depth))
    }

    /// The warm end of an item's size-bar gradient (pink → orange, blue → violet, …).
    static func barEnd(for color: NSColor) -> NSColor {
        guard let rgb = color.usingColorSpace(.sRGB) else { return color }
        var h: CGFloat = 0, s: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        rgb.getHue(&h, saturation: &s, brightness: &b, alpha: &a)
        var shifted = h + 0.07
        if shifted > 1 { shifted -= 1 }
        return NSColor(hue: shifted, saturation: s, brightness: min(1, b * 1.05), alpha: a)
    }

    /// Gauge colour for disk usage: mint while at least half is free, hot pink when almost full.
    static func usageColors(usedFraction: Double) -> [Color] {
        switch usedFraction {
        case ..<0.5: return [Theme.cyanC, Theme.mintC]
        case ..<0.75: return [Theme.cyanC, Theme.yellowC]
        case ..<0.9: return [Theme.yellowC, Theme.orangeC]
        default: return [Theme.magentaC, Theme.dangerC]
        }
    }

    static let ring: [Color] = [Theme.cyanC, Theme.blueC, Theme.violetC, Theme.magentaC, Theme.pinkC, Theme.orangeC, Theme.yellowC, Theme.cyanC]
}
