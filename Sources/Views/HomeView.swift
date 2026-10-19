import SwiftUI
import UniformTypeIdentifiers

/// "Disks and Folders": every mounted volume with its usage, plus starred and recent folders.
struct HomeView: View {
    @Environment(AppModel.self) private var model
    @State private var dropTargeted = false

    private var folders: [String] {
        model.prefs.starredFolders + model.prefs.recentFolders.filter { !model.prefs.starredFolders.contains($0) }
    }

    var body: some View {
        VStack(spacing: 0) {
            ScrollView {
                VStack(alignment: .leading, spacing: 26) {
                    HomeHero()
                    if !model.hasFullDiskAccess {
                        FullDiskAccessBanner()
                    }
                    section("Disks") {
                        ForEach(model.volumes.volumes) { volume in
                            VolumeRow(volume: volume)
                        }
                        if model.volumes.volumes.isEmpty {
                            Text("No disks found.").foregroundStyle(Theme.textSecondaryC)
                        }
                    }
                    if !folders.isEmpty {
                        section("Folders") {
                            ForEach(folders, id: \.self) { path in
                                FolderRow(path: path)
                            }
                        }
                    }
                }
                .padding(.horizontal, 32)
                .padding(.top, 8)
                .padding(.bottom, 28)
                .frame(maxWidth: 900)
                .frame(maxWidth: .infinity)
            }
            footer
        }
        .overlay {
            if dropTargeted {
                RoundedRectangle(cornerRadius: 18)
                    .strokeBorder(Theme.neonBorder, style: StrokeStyle(lineWidth: 3, dash: [12, 7]))
                    .background(Theme.cyanC.opacity(0.06), in: RoundedRectangle(cornerRadius: 18))
                    .overlay(
                        VStack(spacing: 10) {
                            Image("ReaperFolder").resizable().aspectRatio(contentMode: .fit).frame(height: 140)
                            Text("Drop to scan").font(Theme.display(24, .bold)).neonGlow(Theme.magentaC)
                        }
                    )
                    .shadow(color: Theme.cyanC.opacity(0.6), radius: 14)
                    .padding(12)
                    .allowsHitTesting(false)
            }
        }
        .onDrop(of: [.fileURL], isTargeted: $dropTargeted) { providers in
            loadFileURLs(from: providers) { urls in
                if let url = urls.first { model.scan(url: url) }
            }
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                BrandMark().padding(.leading, 4)
            }
            ToolbarItem(placement: .primaryAction) {
                Button { model.chooseFolder() } label: {
                    Label("Scan Folder…", systemImage: "folder.badge.plus")
                }
                .buttonStyle(.neon(.normal, compact: true))
                .help("Choose a folder to scan (⌘O)")
            }
        }
        .onAppear {
            model.volumes.refresh()
            model.refreshAccess()
        }
    }

    private func section<Content: View>(_ title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 10) {
                Text(title.uppercased())
                    .font(.system(size: 12, weight: .heavy, design: .rounded))
                    .tracking(2)
                    .foregroundStyle(Theme.cyanC)
                    .neonGlow(Theme.cyanC, radius: 4)
                Rectangle().fill(LinearGradient(colors: [Theme.cyanC.opacity(0.6), .clear], startPoint: .leading, endPoint: .trailing))
                    .frame(height: 1)
            }
            content()
        }
    }

    private var footer: some View {
        HStack(spacing: 12) {
            Button { model.chooseFolder() } label: {
                Label("Scan Folder…", systemImage: "folder.badge.plus")
            }
            .buttonStyle(.neon)
            Text("or drop any folder or disk onto this window")
                .foregroundStyle(Theme.textSecondaryC)
                .font(.system(size: 12, design: .rounded))
            Spacer()
            if let session = model.session {
                Button {
                    model.showResults()
                } label: {
                    Label("Back to “\(session.target.displayName)”", systemImage: "arrow.uturn.forward")
                }
                .buttonStyle(.neonPrimary)
            }
        }
        .padding(.horizontal, 20)
        .padding(.vertical, 12)
        .background(Theme.panelC.opacity(0.85))
        .overlay(alignment: .top) { Rectangle().fill(Theme.neonBorder.opacity(0.5)).frame(height: 1) }
    }
}

/// Logo, mascot and the synthwave sunset.
private struct HomeHero: View {
    var body: some View {
        ZStack(alignment: .bottom) {
            Image("SunsetBanner")
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(maxWidth: .infinity)
                .opacity(0.9)
                .offset(y: 30)
            HStack(alignment: .center, spacing: 22) {
                Image("ReaperStanding")
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(height: 180)
                    .neonGlow(Theme.magentaC, radius: 10)
                VStack(alignment: .leading, spacing: 12) {
                    Image("Logo")
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(maxWidth: 440)
                    Text("See what's filling your disk, collect what you don't need, reap it in one go.")
                        .font(Theme.display(15, .medium))
                        .foregroundStyle(Theme.textSecondaryC)
                        .shadow(color: .black, radius: 4)
                }
                .padding(.bottom, 92)
                Spacer(minLength: 0)
            }
        }
        .frame(height: 230)
    }
}

struct VolumeRow: View {
    let volume: VolumeInfo
    @Environment(AppModel.self) private var model
    @State private var hovering = false

    var body: some View {
        let purgeableAsFree = !ModifierKeys.shared.option
        let free = volume.free(purgeableAsFree: purgeableAsFree)
        let used = volume.used(purgeableAsFree: purgeableAsFree)
        let fraction = volume.total > 0 ? Double(used) / Double(volume.total) : 0
        let hasResults = model.session?.target.volumeID == volume.id

        HStack(spacing: 18) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: volume.url.path))
                .resizable()
                .frame(width: 56, height: 56)
                .shadow(color: Theme.cyanC.opacity(0.35), radius: 8)
            VStack(alignment: .leading, spacing: 8) {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Text(volume.name).font(Theme.display(18, .bold))
                    if !volume.format.isEmpty {
                        Text(volume.format)
                            .font(.system(size: 10, weight: .semibold, design: .rounded))
                            .foregroundStyle(Theme.textSecondaryC)
                            .padding(.horizontal, 6)
                            .padding(.vertical, 2)
                            .overlay(RoundedRectangle(cornerRadius: 4).strokeBorder(Theme.lineC))
                    }
                    Spacer()
                    Text(ByteFormat.percent(used, of: volume.total) + " used")
                        .font(.system(size: 12, weight: .semibold, design: .rounded))
                        .foregroundStyle(fraction > 0.9 ? Theme.dangerC : Theme.textSecondaryC)
                }
                UsageGauge(fraction: fraction, colors: Palette.usageColors(usedFraction: fraction), height: 10)
                HStack(spacing: 4) {
                    if hovering {
                        SizeText(used).fontWeight(.bold).foregroundStyle(Theme.textC)
                        Text("used of")
                    } else {
                        SizeText(free).fontWeight(.bold).foregroundStyle(Theme.textC)
                        Text(purgeableAsFree ? "free of" : "free (purgeable counted as used) of")
                    }
                    SizeText(volume.total)
                    if purgeableAsFree && volume.purgeable > 0 {
                        Text("· incl.").foregroundStyle(Theme.textTertiaryC)
                        SizeText(volume.purgeable).foregroundStyle(Theme.textTertiaryC)
                        Text("purgeable").foregroundStyle(Theme.textTertiaryC)
                    }
                }
                .font(.system(size: 13, design: .rounded))
                .foregroundStyle(Theme.textSecondaryC)
            }
            VStack(spacing: 8) {
                if hasResults {
                    Button("Show Results") { model.showResults() }
                        .buttonStyle(.neonPrimary)
                    Button("Rescan") { model.scan(volume: volume) }
                        .buttonStyle(.neon(.normal, compact: true))
                } else {
                    Button { model.scan(volume: volume) } label: {
                        Label("Scan", systemImage: "scope").padding(.horizontal, 6)
                    }
                    .buttonStyle(.neonPrimary)
                }
            }
            .frame(width: 130)
        }
        .padding(18)
        .neonPanel(glow: hovering ? Theme.magentaC : Theme.violetC)
        .onHover { hovering = $0 }
        .help(volume.url.path)
    }
}

struct FolderRow: View {
    let path: String
    @Environment(AppModel.self) private var model
    @State private var hovering = false

    var body: some View {
        let starred = model.prefs.isStarred(path)
        let exists = FileManager.default.fileExists(atPath: path)
        let hasResults = model.session?.target.volumeID == nil && model.session?.root.name == Scanner.realPath(path)

        HStack(spacing: 14) {
            Button { model.prefs.toggleStar(path) } label: {
                Image(systemName: starred ? "star.fill" : "star")
                    .foregroundStyle(starred ? Theme.yellowC : Theme.textTertiaryC)
                    .shadow(color: starred ? Theme.yellowC.opacity(0.8) : .clear, radius: 5)
                    .font(.system(size: 15))
            }
            .buttonStyle(.plain)
            .help(starred ? "Unstar folder" : "Star folder to keep it in this list")

            Image(nsImage: NSWorkspace.shared.icon(forFile: path))
                .resizable()
                .frame(width: 34, height: 34)
                .opacity(exists ? 1 : 0.4)
            VStack(alignment: .leading, spacing: 2) {
                Text(FileManager.default.displayName(atPath: path)).font(Theme.display(15, .semibold))
                Text((path as NSString).abbreviatingWithTildeInPath)
                    .font(.system(size: 11, design: .rounded))
                    .foregroundStyle(Theme.textSecondaryC)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer()
            if !exists {
                Text("Not found").font(.caption).foregroundStyle(Theme.textSecondaryC)
            }
            if hovering && !starred {
                Button { model.prefs.forget(path) } label: { Image(systemName: "xmark.circle.fill") }
                    .buttonStyle(.plain)
                    .foregroundStyle(Theme.textTertiaryC)
                    .help("Remove from list")
            }
            if hasResults {
                Button("Show Results") { model.showResults() }
                    .buttonStyle(.neon(.primary, compact: true))
            }
            Button(hasResults ? "Rescan" : "Scan") { model.scan(url: URL(fileURLWithPath: path)) }
                .buttonStyle(.neon(.normal, compact: true))
                .disabled(!exists)
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 11)
        .neonPanel(cornerRadius: 12, glow: hovering ? Theme.magentaC : Theme.violetC, fill: Theme.panelC.opacity(hovering ? 0.9 : 0.7))
        .onHover { hovering = $0 }
    }
}
