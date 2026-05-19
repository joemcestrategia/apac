import SwiftUI

@main
struct ApacApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        WindowGroup {
            ContentView()
        }
        .windowResizability(.contentSize)
        .commands {
            CommandGroup(replacing: .newItem) {}
            CommandGroup(after: .importExport) {
                Divider()
                Button("Open APAC Data File...") {
                    openFile()
                }
                .keyboardShortcut("o", modifiers: [.command])
            }
        }
    }

    private func openFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.init(filenameExtension: "xlsx")!]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false

        if panel.runModal() == .OK, let url = panel.url {
            NotificationCenter.default.post(
                name: .openExcelFile,
                object: nil,
                userInfo: ["url": url]
            )
        }
    }
}

extension Notification.Name {
    static let openExcelFile = Notification.Name("openExcelFile")
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        true
    }
}
