import SwiftUI

struct ScanningView: View {
    let job: ScanJob
    @Environment(AppModel.self) private var model

    var body: some View {
        ZStack(alignment: .bottom) {
            Image("SunsetBanner")
                .resizable()
                .aspectRatio(contentMode: .fit)
                .opacity(0.55)
                .frame(maxWidth: .infinity)
                .allowsHitTesting(false)

            VStack(spacing: 24) {
                Spacer()
                ring
                VStack(spacing: 8) {
                    Text("Scanning “\(job.target.displayName)”…")
                        .font(Theme.display(24, .bold))
                        .neonGlow(Theme.magentaC, radius: 6)
                    Text(job.stats.currentPath.isEmpty ? job.target.url.path : job.stats.currentPath)
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundStyle(Theme.textSecondaryC)
                        .lineLimit(1)
                        .truncationMode(.middle)
                        .frame(maxWidth: 620)
                }
                HStack(spacing: 12) {
                    stat("Files", ByteFormat.count(job.stats.files), Theme.cyanC)
                    stat("Folders", ByteFormat.count(job.stats.folders), Theme.purpleC)
                    stat("Elapsed", elapsed, Theme.yellowC)
                    if job.stats.unreadable > 0 {
                        stat("No access", ByteFormat.count(job.stats.unreadable), Theme.orangeC)
                    }
                }
                Button("Cancel") { model.cancelScan() }
                    .keyboardShortcut(.cancelAction)
                    .buttonStyle(.neon)
                Spacer()
                if !model.hasFullDiskAccess {
                    Text("Tip: grant Full Disk Access to avoid permission prompts and see everything.")
                        .font(.system(size: 11, design: .rounded))
                        .foregroundStyle(Theme.textSecondaryC)
                        .padding(.bottom, 20)
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .padding()
        }
        .toolbar {
            ToolbarItem(placement: .navigation) {
                BrandMark().padding(.leading, 4)
            }
        }
    }

    private var elapsed: String {
        let seconds = Int(Date().timeIntervalSince(job.startDate))
        return String(format: "%d:%02d", seconds / 60, seconds % 60)
    }

    /// Neon progress ring with the reaper bobbing inside it.
    private var ring: some View {
        TimelineView(.animation) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            ZStack {
                Circle().stroke(Theme.lineC.opacity(0.6), lineWidth: 12)
                Group {
                    if let fraction = job.fraction {
                        Circle()
                            .trim(from: 0, to: fraction)
                            .stroke(AngularGradient(colors: Palette.ring, center: .center),
                                    style: StrokeStyle(lineWidth: 12, lineCap: .round))
                            .rotationEffect(.degrees(-90))
                            .animation(.easeOut(duration: 0.2), value: fraction)
                    } else {
                        Circle()
                            .trim(from: 0, to: 0.3)
                            .stroke(AngularGradient(colors: Palette.ring, center: .center),
                                    style: StrokeStyle(lineWidth: 12, lineCap: .round))
                            .rotationEffect(.degrees(t.truncatingRemainder(dividingBy: 1.6) / 1.6 * 360))
                    }
                }
                .shadow(color: Theme.magentaC.opacity(0.8), radius: 10)

                VStack(spacing: 4) {
                    Image("ReaperDisk")
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(height: 130)
                        .offset(y: sin(t * 2.4) * 5)
                        .rotationEffect(.degrees(sin(t * 1.2) * 3))
                        .neonGlow(Theme.magentaC, radius: 8)
                    HStack(spacing: 6) {
                        SizeText(job.stats.bytes)
                            .font(Theme.display(20, .bold))
                        if let fraction = job.fraction {
                            Text(String(format: "%.0f %%", fraction * 100))
                                .font(Theme.display(14, .semibold))
                                .foregroundStyle(Theme.cyanC)
                        }
                    }
                }
            }
            .frame(width: 270, height: 270)
        }
    }

    private func stat(_ title: String, _ value: String, _ color: Color) -> some View {
        VStack(spacing: 2) {
            Text(value).font(Theme.display(18, .bold)).monospacedDigit().foregroundStyle(color).neonGlow(color, radius: 4)
            Text(title.uppercased()).font(.system(size: 10, weight: .semibold, design: .rounded)).tracking(1).foregroundStyle(Theme.textSecondaryC)
        }
        .frame(minWidth: 96)
        .padding(.vertical, 10)
        .neonPanel(cornerRadius: 10, glow: color)
    }
}
