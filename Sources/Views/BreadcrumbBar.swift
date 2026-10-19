import SwiftUI

/// Back / forward / up, then the path: [Disks and Folders] › Macintosh HD › Users › you
struct BreadcrumbBar: View {
    let session: ScanSession
    @Environment(AppModel.self) private var model

    var body: some View {
        HStack(spacing: 8) {
            HStack(spacing: 6) {
                Button { session.goBack() } label: { Image(systemName: "chevron.left") }
                    .disabled(!session.canGoBack)
                    .help("Back (⌘[)")
                Button { session.goForward() } label: { Image(systemName: "chevron.right") }
                    .disabled(!session.canGoForward)
                    .help("Forward (⌘])")
                Button { session.goUp() } label: { Image(systemName: "arrow.up") }
                    .disabled(!session.canGoUp)
                    .help("Enclosing folder (⌘↑)")
            }
            .buttonStyle(NeonIconButtonStyle(size: 28))

            ScrollViewReader { proxy in
                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 4) {
                        BreadcrumbChip(title: "Disks and Folders", isRoot: true, isCurrent: false) {
                            model.goHome()
                        }
                        let crumbs = session.breadcrumb
                        ForEach(Array(crumbs.enumerated()), id: \.offset) { index, node in
                            Image(systemName: "chevron.right")
                                .foregroundStyle(Theme.textTertiaryC)
                                .font(.system(size: 10, weight: .bold))
                            BreadcrumbChip(title: session.displayName(of: node), isRoot: false, isCurrent: index == crumbs.count - 1) {
                                session.navigate(to: node)
                            }
                            .id(index)
                        }
                    }
                    .padding(.vertical, 6)
                    .padding(.horizontal, 2)
                }
                .onChange(of: session.historyVersion) {
                    proxy.scrollTo(session.breadcrumb.count - 1, anchor: .trailing)
                }
            }
        }
    }
}

struct BreadcrumbChip: View {
    let title: String
    let isRoot: Bool
    let isCurrent: Bool
    let action: () -> Void
    @State private var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                if isRoot {
                    Image(systemName: "externaldrive.connected.to.line.below").font(.system(size: 11, weight: .semibold))
                }
                Text(title)
                    .lineLimit(1)
            }
            .font(.system(size: 13, weight: isCurrent || isRoot ? .semibold : .medium, design: .rounded))
            .foregroundStyle(isRoot || isCurrent ? Theme.textC : Theme.textSecondaryC)
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(background, in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(border, lineWidth: 1.1))
            .shadow(color: (isRoot ? Theme.blueC : Theme.violetC).opacity(hovering || isRoot ? 0.45 : 0), radius: 7)
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }

    private var background: AnyShapeStyle {
        if isRoot {
            return AnyShapeStyle(LinearGradient(colors: [Theme.blueC.opacity(hovering ? 0.75 : 0.6), Theme.violetC.opacity(0.5)],
                                                startPoint: .leading, endPoint: .trailing))
        }
        return AnyShapeStyle(Theme.panelC.opacity(isCurrent || hovering ? 0.95 : 0.7))
    }

    private var border: AnyShapeStyle {
        if isRoot { return AnyShapeStyle(Theme.cyanC.opacity(0.8)) }
        return AnyShapeStyle(isCurrent ? AnyShapeStyle(Theme.neonBorder.opacity(0.8)) : AnyShapeStyle(Theme.lineC))
    }
}
