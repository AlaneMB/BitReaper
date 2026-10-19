import SwiftUI

struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        Group {
            switch model.screen {
            case .home:
                HomeView()
            case .scanning:
                if let job = model.scanJob {
                    ScanningView(job: job)
                } else {
                    HomeView()
                }
            case .results:
                if let session = model.session {
                    ResultsView(session: session)
                        .id(ObjectIdentifier(session))
                } else {
                    HomeView()
                }
            }
        }
        .foregroundStyle(Theme.textC)
        .background(SynthwaveBackground(showsSkyline: model.screen == .results))
        .toolbarBackground(.hidden, for: .windowToolbar)
        .alert("BitReaper", isPresented: Binding(
            get: { model.errorMessage != nil },
            set: { if !$0 { model.errorMessage = nil } }
        )) {
            Button("OK") {}
        } message: {
            Text(model.errorMessage ?? "")
        }
    }
}

/// Mascot + "BIT REAPER" wordmark, used at the top-left of every screen.
struct BrandMark: View {
    var height: CGFloat = 30

    var body: some View {
        HStack(spacing: 6) {
            Image("ReaperPeek")
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(height: height * 1.35)
                .neonGlow(Theme.magentaC, radius: 4)
            Image("Logo")
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(height: height * 0.72)
        }
        .accessibilityLabel("BitReaper")
    }
}
