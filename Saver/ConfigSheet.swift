import AppKit

/// Options sheet, built in code so the saver bundle needs no nib.
final class ConfigController: NSObject {
    let window: NSWindow
    private let modePopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private let tempoSlider = NSSlider()
    private let biasSlider = NSSlider()
    private let labelCheck = NSButton(checkboxWithTitle: "Show album and artist name",
                                      target: nil, action: nil)
    private let statusLabel = NSTextField(labelWithString: "")

    override init() {
        window = ConfigWindow(contentRect: NSRect(x: 0, y: 0, width: 440, height: 300),
                              styleMask: [.titled], backing: .buffered, defer: false)
        super.init()
        (window as! ConfigWindow).controller = self
        window.title = "AlbumCoverScreenSaver"
        build()
        load()
    }

    private func build() {
        let content = window.contentView!
        var y: CGFloat = 244

        func heading(_ s: String) {
            let l = NSTextField(labelWithString: s)
            l.font = .systemFont(ofSize: 12, weight: .semibold)
            l.textColor = .secondaryLabelColor
            l.frame = NSRect(x: 24, y: y, width: 392, height: 16)
            content.addSubview(l)
            y -= 22
        }

        heading("Style")
        modePopup.addItems(withTitles: CollageMode.allCases.map(\.title))
        modePopup.frame = NSRect(x: 24, y: y - 4, width: 392, height: 26)
        content.addSubview(modePopup)
        y -= 44

        heading("Seconds between changes")
        tempoSlider.minValue = 1.5
        tempoSlider.maxValue = 12
        tempoSlider.frame = NSRect(x: 24, y: y - 4, width: 392, height: 24)
        content.addSubview(tempoSlider)
        y -= 44

        heading("Favour recent listening over the whole archive")
        biasSlider.minValue = 0
        biasSlider.maxValue = 1
        biasSlider.frame = NSRect(x: 24, y: y - 4, width: 392, height: 24)
        content.addSubview(biasSlider)
        y -= 40

        labelCheck.frame = NSRect(x: 22, y: y, width: 392, height: 20)
        content.addSubview(labelCheck)
        y -= 30

        statusLabel.font = .systemFont(ofSize: 11)
        statusLabel.textColor = .tertiaryLabelColor
        statusLabel.frame = NSRect(x: 24, y: y, width: 392, height: 16)
        content.addSubview(statusLabel)

        let done = NSButton(title: "Done", target: self, action: #selector(save))
        done.bezelStyle = .rounded
        done.keyEquivalent = "\r"
        done.frame = NSRect(x: 330, y: 16, width: 90, height: 32)
        content.addSubview(done)

        let cancel = NSButton(title: "Cancel", target: self, action: #selector(close))
        cancel.bezelStyle = .rounded
        cancel.frame = NSRect(x: 236, y: 16, width: 90, height: 32)
        content.addSubview(cancel)
    }

    private func load() {
        let s = SharedStore.loadSettings()
        if let i = CollageMode.allCases.firstIndex(of: s.mode) { modePopup.selectItem(at: i) }
        tempoSlider.doubleValue = s.tempo
        biasSlider.doubleValue = s.recencyBias
        labelCheck.state = s.showTrackLabel ? .on : .off

        let archive = SharedStore.loadArchive()
        statusLabel.stringValue = archive.albums.isEmpty
            ? "No archive yet — sign in from the AlbumCoverScreenSaver menu bar app."
            : "\(archive.albums.count) albums archived."
    }

    @objc private func save() {
        var s = SharedStore.loadSettings()
        s.mode = CollageMode.allCases[max(0, modePopup.indexOfSelectedItem)]
        s.tempo = tempoSlider.doubleValue
        s.recencyBias = biasSlider.doubleValue
        s.showTrackLabel = labelCheck.state == .on
        SharedStore.saveSettings(s)
        close()
    }

    @objc private func close() {
        if let parent = window.sheetParent { parent.endSheet(window) }
        else { window.close() }
    }
}

/// Holds a strong reference to the controller for as long as the sheet lives.
final class ConfigWindow: NSWindow {
    var controller: ConfigController?
}

enum ConfigSheet {
    static func make() -> NSWindow { ConfigController().window }
}
