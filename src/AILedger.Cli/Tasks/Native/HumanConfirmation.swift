import AppKit
import CryptoKit
import LocalAuthentication

struct Request: Decodable {
    let task: String
    let title: String
    let actor: String
    let reason: String
    let ledgerRoot: String
    let version: Int64
    let activeRuns: Int
    let unfinishedWork: Int
}

// No environment switch, stdin answer, cached approval, or command-line yes path.
let bytes = FileHandle.standardInput.readDataToEndOfFile()
guard bytes.count <= 32768,
      let request = try? JSONDecoder().decode(Request.self, from: bytes),
      !request.reason.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { exit(2) }
let app = NSApplication.shared
app.setActivationPolicy(.accessory)
app.activate(ignoringOtherApps: true)
let alert = NSAlert()
alert.messageText = "Close this task successfully by human override?"
alert.informativeText = "This closes the entire task, including work added while this dialog is open, without completing its remaining workflow. Existing findings and failed checks stay recorded. Active runs lose kernel authority; their physical termination may still be pending."
alert.addButton(withTitle: "Cancel")
alert.addButton(withTitle: "Confirm successful closure")
let details = NSTextView(frame: NSRect(x: 0, y: 0, width: 560, height: 230))
details.isEditable = false
details.isSelectable = true
details.string = "Task: \(request.task)\nTitle: \(request.title)\nLedger: \(request.ledgerRoot)\nVersion: \(request.version)\nActor: \(request.actor)\nActive runs: \(request.activeRuns)\nUnfinished work: \(request.unfinishedWork)\n\nReason: \(request.reason)"
let scroll = NSScrollView(frame: details.frame)
scroll.documentView = details
scroll.hasVerticalScroller = true
alert.accessoryView = scroll
guard alert.runModal() == .alertSecondButtonReturn else { exit(1) }

let context = LAContext()
context.touchIDAuthenticationAllowableReuseDuration = 0
var error: NSError?
guard context.canEvaluatePolicy(.deviceOwnerAuthentication, error: &error) else { exit(1) }
context.evaluatePolicy(.deviceOwnerAuthentication,
    localizedReason: "Confirm successful closure of AILedger task '\(request.task)'. Reason: \(request.reason)") { success, _ in
    guard success else { exit(1) }
    let digest = SHA256.hash(data: bytes).map { String(format: "%02x", $0) }.joined()
    let response = ["RequestSha256": digest, "Identity": NSUserName()]
    guard let json = try? JSONSerialization.data(withJSONObject: response) else { exit(2) }
    FileHandle.standardOutput.write(json)
    exit(0)
}
app.run()
