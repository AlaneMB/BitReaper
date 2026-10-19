import SwiftUI
import AppKit

/// Synthwave look: deep indigo night, neon magenta / cyan / purple accents, glowing outlines.
enum Theme {
    // Base colours (sRGB hex)
    static let night = NSColor(hex: 0x0B0824)
    static let nightDeep = NSColor(hex: 0x05030F)
    static let panel = NSColor(hex: 0x120D30)
    static let panelRaised = NSColor(hex: 0x1A1342)
    static let line = NSColor(hex: 0x3A2A8C)

    static let magenta = NSColor(hex: 0xFF2BD6)
    static let pink = NSColor(hex: 0xFF3D9A)
    static let purple = NSColor(hex: 0xA24BFF)
    static let violet = NSColor(hex: 0x6C3BFF)
    static let blue = NSColor(hex: 0x2F8BFF)
    static let cyan = NSColor(hex: 0x22E4F5)
    static let yellow = NSColor(hex: 0xFFC93C)
    static let orange = NSColor(hex: 0xFF7A3D)
    static let mint = NSColor(hex: 0x3DFFC5)
    static let danger = NSColor(hex: 0xFF3D6E)

    static let text = NSColor(hex: 0xF4F0FF)
    static let textSecondary = NSColor(hex: 0xA99BD8)
    static let textTertiary = NSColor(hex: 0x6E62A0)

    // SwiftUI mirrors
    static let nightC = Color(nsColor: night)
    static let panelC = Color(nsColor: panel)
    static let panelRaisedC = Color(nsColor: panelRaised)
    static let lineC = Color(nsColor: line)
    static let magentaC = Color(nsColor: magenta)
    static let pinkC = Color(nsColor: pink)
    static let purpleC = Color(nsColor: purple)
    static let violetC = Color(nsColor: violet)
    static let blueC = Color(nsColor: blue)
    static let cyanC = Color(nsColor: cyan)
    static let yellowC = Color(nsColor: yellow)
    static let orangeC = Color(nsColor: orange)
    static let mintC = Color(nsColor: mint)
    static let dangerC = Color(nsColor: danger)
    static let textC = Color(nsColor: text)
    static let textSecondaryC = Color(nsColor: textSecondary)
    static let textTertiaryC = Color(nsColor: textTertiary)

    static let neonBorder = LinearGradient(colors: [cyanC, violetC, magentaC], startPoint: .leading, endPoint: .trailing)
    static let hotGradient = LinearGradient(colors: [magentaC, violetC], startPoint: .leading, endPoint: .trailing)
    static let sunsetGradient = LinearGradient(colors: [yellowC, orangeC, pinkC, magentaC, purpleC], startPoint: .top, endPoint: .bottom)

    static func display(_ size: CGFloat, _ weight: Font.Weight = .bold) -> Font {
        .system(size: size, weight: weight, design: .rounded)
    }
}

extension NSColor {
    convenience init(hex: UInt32, alpha: CGFloat = 1) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255,
                  alpha: alpha)
    }
}

// MARK: - Window background

/// Night-sky gradient with a faint perspective grid, the sunset skyline behind the toolbar.
struct SynthwaveBackground: View {
    var showsSkyline = true

    var body: some View {
        ZStack(alignment: .top) {
            LinearGradient(colors: [Color(nsColor: Theme.night), Color(nsColor: Theme.nightDeep)],
                           startPoint: .top, endPoint: .bottom)
            Canvas { context, size in
                // Horizontal scanlines + vertical grid, very faint.
                var path = Path()
                stride(from: 0.0, through: size.height, by: 26).forEach { y in
                    path.move(to: CGPoint(x: 0, y: y))
                    path.addLine(to: CGPoint(x: size.width, y: y))
                }
                stride(from: 0.0, through: size.width, by: 26).forEach { x in
                    path.move(to: CGPoint(x: x, y: 0))
                    path.addLine(to: CGPoint(x: x, y: size.height))
                }
                context.stroke(path, with: .color(Theme.violetC.opacity(0.05)), lineWidth: 0.5)
            }
            RadialGradient(colors: [Theme.magentaC.opacity(0.10), .clear], center: .topTrailing, startRadius: 0, endRadius: 700)
            RadialGradient(colors: [Theme.cyanC.opacity(0.06), .clear], center: .bottomLeading, startRadius: 0, endRadius: 600)
            if showsSkyline {
                GeometryReader { proxy in
                    Image("SunsetBanner")
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(width: 560)
                        .mask(LinearGradient(colors: [.black, .black, .clear], startPoint: .top, endPoint: .bottom))
                        .opacity(0.85)
                        .position(x: proxy.size.width * 0.62, y: 34)
                }
                .frame(height: 70)
                .allowsHitTesting(false)
            }
        }
        .ignoresSafeArea()
    }
}

// MARK: - Panels

struct NeonPanel: ViewModifier {
    var cornerRadius: CGFloat = 14
    var glow: Color = Theme.violetC
    var fill: Color = Theme.panelC.opacity(0.82)
    var dashed = false

    func body(content: Content) -> some View {
        content
            .background(fill, in: RoundedRectangle(cornerRadius: cornerRadius))
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius)
                    .strokeBorder(Theme.neonBorder.opacity(0.55),
                                  style: StrokeStyle(lineWidth: 1.2, dash: dashed ? [6, 4] : []))
            )
            .shadow(color: glow.opacity(0.22), radius: 12)
    }
}

extension View {
    func neonPanel(cornerRadius: CGFloat = 14, glow: Color = Theme.violetC, fill: Color = Theme.panelC.opacity(0.82), dashed: Bool = false) -> some View {
        modifier(NeonPanel(cornerRadius: cornerRadius, glow: glow, fill: fill, dashed: dashed))
    }

    /// Soft neon bloom around text or shapes.
    func neonGlow(_ color: Color, radius: CGFloat = 6) -> some View {
        shadow(color: color.opacity(0.75), radius: radius)
    }
}

// MARK: - Buttons

struct NeonButtonStyle: ButtonStyle {
    enum Kind { case normal, primary, danger, ghost }
    var kind: Kind = .normal
    var compact = false
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        let pressed = configuration.isPressed
        configuration.label
            .font(.system(size: compact ? 12 : 13, weight: .semibold, design: .rounded))
            .foregroundStyle(foreground)
            .padding(.horizontal, compact ? 10 : 14)
            .padding(.vertical, compact ? 5 : 8)
            .background(background, in: RoundedRectangle(cornerRadius: compact ? 8 : 10))
            .overlay(RoundedRectangle(cornerRadius: compact ? 8 : 10).strokeBorder(border, lineWidth: 1.2))
            .shadow(color: glow.opacity(isEnabled ? (pressed ? 0.9 : 0.45) : 0), radius: pressed ? 10 : 7)
            .opacity(isEnabled ? 1 : 0.4)
            .scaleEffect(pressed ? 0.97 : 1)
            .animation(.easeOut(duration: 0.12), value: pressed)
            .contentShape(Rectangle())
    }

    private var foreground: Color {
        switch kind {
        case .primary, .danger: return .white
        case .normal: return Theme.textC
        case .ghost: return Theme.textSecondaryC
        }
    }

    private var background: AnyShapeStyle {
        switch kind {
        case .primary: return AnyShapeStyle(LinearGradient(colors: [Theme.magentaC, Theme.violetC], startPoint: .leading, endPoint: .trailing))
        case .danger: return AnyShapeStyle(LinearGradient(colors: [Theme.dangerC, Theme.magentaC.opacity(0.85)], startPoint: .leading, endPoint: .trailing))
        case .normal: return AnyShapeStyle(Theme.panelRaisedC.opacity(0.9))
        case .ghost: return AnyShapeStyle(Color.clear)
        }
    }

    private var border: AnyShapeStyle {
        switch kind {
        case .primary: return AnyShapeStyle(Color.white.opacity(0.25))
        case .danger: return AnyShapeStyle(Color.white.opacity(0.25))
        case .normal: return AnyShapeStyle(Theme.neonBorder.opacity(0.7))
        case .ghost: return AnyShapeStyle(Theme.lineC.opacity(0.7))
        }
    }

    private var glow: Color {
        switch kind {
        case .primary: return Theme.magentaC
        case .danger: return Theme.dangerC
        case .normal: return Theme.violetC
        case .ghost: return .clear
        }
    }
}

/// Square neon icon button (toolbar refresh, disks, back/forward…).
struct NeonIconButtonStyle: ButtonStyle {
    var size: CGFloat = 32
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: size * 0.44, weight: .semibold))
            .foregroundStyle(Theme.textC)
            .frame(width: size, height: size)
            .background(Theme.panelRaisedC.opacity(configuration.isPressed ? 1 : 0.85), in: RoundedRectangle(cornerRadius: size * 0.28))
            .overlay(RoundedRectangle(cornerRadius: size * 0.28).strokeBorder(Theme.neonBorder.opacity(0.65), lineWidth: 1.1))
            .shadow(color: Theme.violetC.opacity(configuration.isPressed ? 0.8 : 0.35), radius: 6)
            .opacity(isEnabled ? 1 : 0.35)
            .contentShape(Rectangle())
    }
}

extension ButtonStyle where Self == NeonButtonStyle {
    static var neon: NeonButtonStyle { NeonButtonStyle() }
    static var neonPrimary: NeonButtonStyle { NeonButtonStyle(kind: .primary) }
    static var neonDanger: NeonButtonStyle { NeonButtonStyle(kind: .danger) }
    static var neonGhost: NeonButtonStyle { NeonButtonStyle(kind: .ghost) }
    static func neon(_ kind: NeonButtonStyle.Kind, compact: Bool = false) -> NeonButtonStyle { NeonButtonStyle(kind: kind, compact: compact) }
}

extension ButtonStyle where Self == NeonIconButtonStyle {
    static var neonIcon: NeonIconButtonStyle { NeonIconButtonStyle() }
}

/// Neon segmented control.
struct NeonSegmented<Value: Hashable>: View {
    let options: [(value: Value, title: String, symbol: String)]
    @Binding var selection: Value

    var body: some View {
        HStack(spacing: 4) {
            ForEach(Array(options.enumerated()), id: \.offset) { _, option in
                let selected = option.value == selection
                Button {
                    selection = option.value
                } label: {
                    Label(option.title, systemImage: option.symbol)
                        .labelStyle(.titleAndIcon)
                        .font(.system(size: 13, weight: .semibold, design: .rounded))
                        .foregroundStyle(selected ? Theme.textC : Theme.textSecondaryC)
                        .padding(.horizontal, 16)
                        .padding(.vertical, 6)
                        .background {
                            if selected {
                                RoundedRectangle(cornerRadius: 8)
                                    .fill(LinearGradient(colors: [Theme.violetC.opacity(0.55), Theme.blueC.opacity(0.35)], startPoint: .top, endPoint: .bottom))
                                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Theme.cyanC.opacity(0.8), lineWidth: 1))
                                    .shadow(color: Theme.cyanC.opacity(0.45), radius: 6)
                            }
                        }
                        .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
        }
        .padding(3)
        .background(Theme.panelC.opacity(0.9), in: RoundedRectangle(cornerRadius: 11))
        .overlay(RoundedRectangle(cornerRadius: 11).strokeBorder(Theme.lineC, lineWidth: 1))
    }
}

/// Keyboard shortcut hint chip, e.g. "⌘F".
struct KeyHint: View {
    let text: String

    var body: some View {
        Text(text)
            .font(.system(size: 10, weight: .semibold, design: .rounded))
            .foregroundStyle(Theme.textTertiaryC)
            .padding(.horizontal, 5)
            .padding(.vertical, 2)
            .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Theme.lineC, lineWidth: 1))
    }
}
