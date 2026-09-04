import AppKit

/// Settings live here rather than in the screen saver's Options sheet, because
/// macOS's redesigned Screen Saver pane no longer offers an Options button for
/// third-party savers. The app writes to the shared container; the saver
/// re-reads it every 15 seconds, so changes appear without reinstalling.
final class SettingsWindowController: NSWindowController, NSWindowDelegate {
    static let shared = SettingsWindowController()

    private var s = Settings.default
    private let modePopup = NSPopUpButton(frame: .zero, pullsDown: false)
    private var modeBox = NSView(frame: NSRect(x: 0, y: 132, width: 520, height: 196))
    private var modeHeader = NSTextField(labelWithString: "")
    private var valueLabels: [Int: NSTextField] = [:]
    private var rowLabels: [Int: NSTextField] = [:]
    private var formatters: [Int: (Double) -> String] = [:]
    private let gridNote = NSTextField(labelWithString: "")

    // control tags
    private enum T {
        static let mode = 99
        static let tileSize = 1, tempo = 2, recency = 3, showLabel = 4
        static let flipsAtOnce = 10, flipDuration = 11, mosaicRandom = 12
        static let driftCount = 20, driftSpeed = 21, driftScale = 22, driftRandom = 23
        static let wallHold = 30, wallBuild = 31
        static let heroSize = 40, heroDim = 41, heroInterval = 42, heroFollow = 43
        static let vinylSeconds = 50, vinylRPM = 51, vinylFollow = 52, vinylSleeves = 53, vinylSleeveScale = 54, vinylFallback = 55
        static let featureSeconds = 60, ambientMotion = 61
        static let crateCount = 62, galleryColumns = 63
        static let flowCount = 64, orbitCount = 65, orbitSpeed = 66, crtAmber = 67
        static let cdCases = 68, polaroids = 69, zoetropeCount = 70
    }

    private init() {
        let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 610),
                         styleMask: [.titled, .closable], backing: .buffered, defer: false)
        w.title = "Album Cover Screen Saver Settings"
        w.isReleasedWhenClosed = false
        super.init(window: w)
        w.delegate = self
        build()
    }

    required init?(coder: NSCoder) { fatalError("unused") }

    func show() {
        s = SharedStore.loadSettings()
        syncFromSettings()
        rebuildModeSection()
        window?.center()
        NSApp.activate(ignoringOtherApps: true)
        showWindow(nil)
        window?.makeKeyAndOrderFront(nil)
    }

    // MARK: build

    private func build() {
        let content = window!.contentView!
        var y: CGFloat = 556

        func header(_ t: String) -> NSTextField {
            let l = NSTextField(labelWithString: t)
            l.font = .systemFont(ofSize: 12, weight: .semibold)
            l.textColor = .secondaryLabelColor
            l.frame = NSRect(x: 24, y: y, width: 472, height: 16)
            content.addSubview(l)
            y -= 28
            return l
        }

        func label(_ t: String) {
            let l = NSTextField(labelWithString: t)
            l.alignment = .right
            l.font = .systemFont(ofSize: 12)
            l.frame = NSRect(x: 12, y: y + 3, width: 168, height: 16)
            content.addSubview(l)
        }

        _ = header("Style")
        modePopup.addItems(withTitles: CollageMode.allCases.map(\.title))
        modePopup.tag = T.mode
        modePopup.target = self
        modePopup.action = #selector(changed(_:))
        modePopup.frame = NSRect(x: 24, y: y - 2, width: 472, height: 26)
        content.addSubview(modePopup)
        y -= 44

        _ = header("Layout & Timing")

        label("Tile size")
        addSlider(to: content, tag: T.tileSize, y: y, min: 90, max: 340,
                  format: { "\(Int($0)) pt" })
        y -= 30
        gridNote.font = .systemFont(ofSize: 10)
        gridNote.textColor = .tertiaryLabelColor
        gridNote.frame = NSRect(x: 188, y: y + 6, width: 300, height: 14)
        content.addSubview(gridNote)
        y -= 22

        label("Seconds between changes")
        addSlider(to: content, tag: T.tempo, y: y, min: 0.5, max: 15,
                  format: { String(format: "%.1f s", $0) })
        y -= 32

        label("Favour recent listening")
        addSlider(to: content, tag: T.recency, y: y, min: 0, max: 1,
                  format: { $0 < 0.05 ? "Whole archive" : ($0 > 0.95 ? "Only recent"
                            : "\(Int($0 * 100))%") })
        y -= 34

        let check = NSButton(checkboxWithTitle: "Show album and artist name",
                             target: self, action: #selector(changed(_:)))
        check.tag = T.showLabel
        check.frame = NSRect(x: 186, y: y, width: 300, height: 20)
        content.addSubview(check)
        y -= 34

        modeHeader = header("")

        modeBox.frame = NSRect(x: 0, y: 128, width: 520, height: y - 120)
        content.addSubview(modeBox)

        let note = NSTextField(labelWithString:
            "Changes save immediately \u{2014} the running screen saver picks them up "
            + "within a few seconds.")
        note.font = .systemFont(ofSize: 11)
        note.textColor = .tertiaryLabelColor
        note.frame = NSRect(x: 24, y: 92, width: 472, height: 16)
        content.addSubview(note)

        let reset = NSButton(title: "Reset to Defaults", target: self,
                             action: #selector(resetDefaults))
        reset.bezelStyle = .rounded
        reset.frame = NSRect(x: 24, y: 24, width: 160, height: 32)
        content.addSubview(reset)

        let close = NSButton(title: "Done", target: self, action: #selector(closeWindow))
        close.bezelStyle = .rounded
        close.keyEquivalent = "\r"
        close.frame = NSRect(x: 406, y: 24, width: 90, height: 32)
        content.addSubview(close)
    }

    @discardableResult
    private func addSlider(to parent: NSView, tag: Int, y: CGFloat,
                           min lo: Double, max hi: Double,
                           format: @escaping (Double) -> String) -> NSSlider {
        let sl = NSSlider(value: lo, minValue: lo, maxValue: hi,
                          target: self, action: #selector(changed(_:)))
        sl.tag = tag
        sl.isContinuous = true
        sl.frame = NSRect(x: 186, y: y, width: 232, height: 22)
        parent.addSubview(sl)

        let v = NSTextField(labelWithString: "")
        v.font = .monospacedDigitSystemFont(ofSize: 11, weight: .regular)
        v.textColor = .secondaryLabelColor
        v.frame = NSRect(x: 424, y: y + 3, width: 84, height: 16)
        parent.addSubview(v)

        valueLabels[tag] = v
        formatters[tag] = format
        return sl
    }

    // MARK: mode-specific section

    private func rebuildModeSection() {
        modeBox.subviews.forEach { $0.removeFromSuperview() }
        rowLabels.removeAll()
        modeHeader.stringValue = s.mode.title + " options"
        var y = modeBox.bounds.height - 26

        func row(_ tag: Int, _ title: String, _ lo: Double, _ hi: Double,
                 _ fmt: @escaping (Double) -> String) {
            let l = NSTextField(labelWithString: title)
            l.alignment = .right
            l.font = .systemFont(ofSize: 12)
            l.frame = NSRect(x: 12, y: y + 3, width: 168, height: 16)
            modeBox.addSubview(l)
            rowLabels[tag] = l
            addSlider(to: modeBox, tag: tag, y: y, min: lo, max: hi, format: fmt)
            y -= 32
        }

        switch s.mode {
        case .mosaic:
            let cb = NSButton(checkboxWithTitle: "Random cover size",
                              target: self, action: #selector(changed(_:)))
            cb.tag = T.mosaicRandom
            cb.state = s.mosaicRandomSize ? .on : .off
            cb.toolTip = "Promotes some covers to double-size blocks, so the wall "
                       + "reads as a mosaic rather than a uniform grid."
            cb.frame = NSRect(x: 186, y: y, width: 320, height: 20)
            modeBox.addSubview(cb)
            y -= 30

            row(T.flipsAtOnce, "Flip rate", 1, 20) { [weak self] v in
                let tempo = max(0.3, self?.s.tempo ?? 4)
                return String(format: "\u{2248} %.0f per minute", 60 * v / tempo)
            }
            row(T.flipDuration, "Flip duration", 0.2, 2.5) {
                String(format: "%.2f s average", $0)
            }

        case .drift:
            row(T.driftCount, "Covers on screen", 4, 44) { "\(Int($0))" }
            row(T.driftSpeed, "Drift speed", 0.2, 4) { String(format: "%.2f\u{00D7}", $0) }

            let cb = NSButton(checkboxWithTitle: "Random size (depth effect)",
                              target: self, action: #selector(changed(_:)))
            cb.tag = T.driftRandom
            cb.state = s.driftRandomSize ? .on : .off
            cb.toolTip = "Covers vary in size; smaller ones sit further back, "
                       + "drift more slowly and fade into the background."
            cb.frame = NSRect(x: 186, y: y, width: 300, height: 20)
            modeBox.addSubview(cb)
            y -= 30

            row(T.driftScale, "Cover size", 0.4, 2.2) { String(format: "%.2f\u{00D7}", $0) }

        case .ambient:
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }
            row(T.ambientMotion, "Colour drift", 0.1, 3) { String(format: "%.2f\u{00D7}", $0) }

        case .cassette:
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .crate:
            row(T.crateCount, "Sleeves in the crate", 6, 40) { "\(Int($0))" }
            row(T.featureSeconds, "Seconds per pass", 8, 300) { String(format: "%.0f s", $0) }

        case .gallery:
            row(T.galleryColumns, "Frames across", 2, 7) { "\(Int($0))" }
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .coverflow:
            row(T.flowCount, "Covers on the carousel", 5, 50) { "\(Int($0))" }
            row(T.featureSeconds, "Seconds per pass", 8, 300) { String(format: "%.0f s", $0) }

        case .jukebox:
            row(T.featureSeconds, "Seconds per record", 8, 300) { String(format: "%.0f s", $0) }

        case .starfield:
            row(T.orbitCount, "Albums in orbit", 3, 36) { "\(Int($0))" }
            row(T.orbitSpeed, "Orbit speed", 0.1, 4) { String(format: "%.2f\u{00D7}", $0) }
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .crt:
            let amber = NSButton(checkboxWithTitle: "Amber phosphor (off = green)",
                                 target: self, action: #selector(changed(_:)))
            amber.tag = T.crtAmber
            amber.state = s.crtAmber ? .on : .off
            amber.frame = NSRect(x: 186, y: y, width: 320, height: 20)
            modeBox.addSubview(amber)
            y -= 30
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .cdplayer:
            row(T.cdCases, "Jewel cases around it", 0, 20) { $0 < 0.5 ? "none" : "\(Int($0))" }
            row(T.featureSeconds, "Seconds per disc", 8, 300) { String(format: "%.0f s", $0) }

        case .polaroid:
            row(T.polaroids, "Photos kept on the board", 0, 24) {
                $0 < 0.5 ? "none" : "\(Int($0))"
            }
            row(T.featureSeconds, "Seconds per photo", 8, 300) { String(format: "%.0f s", $0) }

        case .zoetrope:
            row(T.zoetropeCount, "Covers on the drum", 6, 30) { "\(Int($0))" }
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .newsstand, .vaporwave, .subway:
            row(T.featureSeconds, "Seconds per album", 8, 300) { String(format: "%.0f s", $0) }

        case .wall:
            row(T.wallHold, "Hold when full", 1, 40) { String(format: "%.0f s", $0) }
            row(T.wallBuild, "Build speed", 0.2, 5) { String(format: "%.2f\u{00D7}", $0) }

        case .vinyl:
            let fbLabel = NSTextField(labelWithString: "When nothing is playing")
            fbLabel.alignment = .right
            fbLabel.font = .systemFont(ofSize: 12)
            fbLabel.frame = NSRect(x: 12, y: y + 3, width: 168, height: 16)
            modeBox.addSubview(fbLabel)

            let fb = NSPopUpButton(frame: NSRect(x: 186, y: y - 3, width: 232, height: 25),
                                   pullsDown: false)
            fb.addItems(withTitles: fallbackModes.map(\.title))
            if let i = fallbackModes.firstIndex(of: s.vinylFallbackMode) {
                fb.selectItem(at: i)
            }
            fb.tag = T.vinylFallback
            fb.target = self
            fb.action = #selector(changed(_:))
            fb.toolTip = "The Record Player needs music. This style is shown when "
                       + "Spotify isn\u{2019}t playing."
            modeBox.addSubview(fb)
            y -= 32

            let vFollow = NSButton(checkboxWithTitle: "Play what\u{2019}s playing on Spotify",
                                   target: self, action: #selector(changed(_:)))
            vFollow.tag = T.vinylFollow
            vFollow.state = s.vinylFollowNowPlaying ? .on : .off
            vFollow.toolTip = "The record on the platter follows the current track; "
                            + "otherwise it changes on a timer."
            vFollow.frame = NSRect(x: 186, y: y, width: 320, height: 20)
            modeBox.addSubview(vFollow)
            y -= 30

            row(T.vinylSleeves, "Records kept on the table", 0, 40) {
                $0 < 0.5 ? "none" : "\(Int($0))"
            }
            row(T.vinylSleeveScale, "Sleeve size", 0.6, 1.9) {
                String(format: "%.2f\u{00D7}", $0)
            }
            row(T.vinylSeconds, "Seconds per record", 20, 600) { String(format: "%.0f s", $0) }
            row(T.vinylRPM, "Spin speed", 2, 45) {
                abs($0 - 33.33) < 1.0 ? "33\u{2153} RPM (true LP)"
                                      : String(format: "%.0f RPM", $0)
            }

        case .hero:
            let follow = NSButton(checkboxWithTitle: "Feature what\u{2019}s playing on Spotify",
                                  target: self, action: #selector(changed(_:)))
            follow.tag = T.heroFollow
            follow.state = s.heroFollowNowPlaying ? .on : .off
            follow.toolTip = "While music is playing, the large cover stays on the "
                           + "current track instead of rotating."
            follow.frame = NSRect(x: 186, y: y, width: 320, height: 20)
            modeBox.addSubview(follow)
            y -= 30

            row(T.heroSize, "Featured cover size", 0.25, 0.9) { "\(Int($0 * 100))% of height" }
            row(T.heroDim, "Background dimming", 0, 0.85) { "\(Int($0 * 100))%" }
            row(T.heroInterval, "Seconds between features", 3, 90) { String(format: "%.0f s", $0) }
        }
        syncFromSettings()
        updateDriftEnabled()
    }

    /// Record Player can't be its own fallback.
    private var fallbackModes: [CollageMode] { CollageMode.allCases.filter { $0 != .vinyl } }

    /// Cover size is meaningless when sizes are randomised by depth, so grey it out
    /// rather than leaving a control that silently does nothing.
    private func updateDriftEnabled() {
        guard s.mode == .drift else { return }
        let random = s.driftRandomSize
        slider(T.driftScale)?.isEnabled = !random
        rowLabels[T.driftScale]?.textColor = random ? .disabledControlTextColor : .labelColor
        valueLabels[T.driftScale]?.stringValue = random
            ? "randomised"
            : (formatters[T.driftScale]?(s.driftScale) ?? "")
    }

    // MARK: sync

    private func slider(_ tag: Int) -> NSSlider? {
        for v in [window!.contentView!, modeBox] {
            if let sl = v.subviews.first(where: { ($0 as? NSSlider)?.tag == tag }) as? NSSlider {
                return sl
            }
        }
        return nil
    }

    private func set(_ tag: Int, _ value: Double) {
        slider(tag)?.doubleValue = value
        valueLabels[tag]?.stringValue = formatters[tag]?(value) ?? ""
    }

    private func syncFromSettings() {
        if let i = CollageMode.allCases.firstIndex(of: s.mode) { modePopup.selectItem(at: i) }
        set(T.tileSize, s.tileSize)
        set(T.tempo, s.tempo)
        set(T.recency, s.recencyBias)
        if let c = window?.contentView?.subviews
            .first(where: { ($0 as? NSButton)?.tag == T.showLabel }) as? NSButton {
            c.state = s.showTrackLabel ? .on : .off
        }
        set(T.flipsAtOnce, Double(s.flipsAtOnce)); set(T.flipDuration, s.flipDuration)
        set(T.driftCount, Double(s.driftCount)); set(T.driftSpeed, s.driftSpeed)
        set(T.driftScale, s.driftScale)
        set(T.wallHold, s.wallHold); set(T.wallBuild, s.wallBuildSpeed)
        set(T.heroSize, s.heroSize); set(T.heroDim, s.heroDim)
        set(T.heroInterval, s.heroInterval)
        set(T.vinylSeconds, s.vinylSecondsPerRecord); set(T.vinylRPM, s.vinylRPM)
        set(T.vinylSleeves, Double(s.vinylSleeveCount))
        set(T.vinylSleeveScale, s.vinylSleeveScale)
        set(T.featureSeconds, s.featureSeconds); set(T.ambientMotion, s.ambientMotion)
        set(T.crateCount, Double(s.crateCount))
        set(T.galleryColumns, Double(s.galleryColumns))
        set(T.flowCount, Double(s.flowCount)); set(T.orbitCount, Double(s.orbitCount))
        set(T.orbitSpeed, s.orbitSpeed)
        set(T.cdCases, Double(s.cdCaseCount)); set(T.polaroids, Double(s.polaroidCount))
        set(T.zoetropeCount, Double(s.zoetropeCount))
        updateGridNote()
    }

    /// Turns the abstract "tile size" number into the grid it actually produces
    /// on this Mac's display.
    private func updateGridNote() {
        guard let screen = NSScreen.main else { gridNote.stringValue = ""; return }
        let f = screen.frame
        let cols = max(3, Int((f.width / CGFloat(s.tileSize)).rounded()))
        let cellW = f.width / CGFloat(cols)
        let rows = max(2, Int((f.height / cellW).rounded()))
        gridNote.stringValue = "≈ \(cols) × \(rows) = \(cols * rows) covers on this display"
    }

    // MARK: actions

    @objc private func changed(_ sender: NSControl) {
        let v = sender.doubleValue
        switch sender.tag {
        case T.mode:
            s.mode = CollageMode.allCases[max(0, modePopup.indexOfSelectedItem)]
            save(); rebuildModeSection(); return
        case T.tileSize:     s.tileSize = v; updateGridNote()
        case T.tempo:
            s.tempo = v
            if s.mode == .mosaic || s.mode == .hero { set(T.flipsAtOnce, Double(s.flipsAtOnce)) }
        case T.recency:      s.recencyBias = v
        case T.showLabel:    s.showTrackLabel = (sender as? NSButton)?.state == .on
        case T.flipsAtOnce:  s.flipsAtOnce = Int(v.rounded())
        case T.flipDuration: s.flipDuration = v
        case T.mosaicRandom:
            s.mosaicRandomSize = (sender as? NSButton)?.state == .on
            save(); return
        case T.driftCount:   s.driftCount = Int(v.rounded())
        case T.driftSpeed:   s.driftSpeed = v
        case T.driftScale:   s.driftScale = v
        case T.driftRandom:
            s.driftRandomSize = (sender as? NSButton)?.state == .on
            save(); updateDriftEnabled(); return
        case T.wallHold:     s.wallHold = v
        case T.wallBuild:    s.wallBuildSpeed = v
        case T.heroSize:     s.heroSize = v
        case T.heroDim:      s.heroDim = v
        case T.heroInterval: s.heroInterval = v
        case T.heroFollow:
            s.heroFollowNowPlaying = (sender as? NSButton)?.state == .on
            save(); return
        case T.vinylFallback:
            if let pop = sender as? NSPopUpButton {
                let i = max(0, min(fallbackModes.count - 1, pop.indexOfSelectedItem))
                s.vinylFallbackMode = fallbackModes[i]
            }
            save(); return
        case T.vinylSleeves: s.vinylSleeveCount = Int(v.rounded())
        case T.vinylSleeveScale: s.vinylSleeveScale = v
        case T.featureSeconds:  s.featureSeconds = v
        case T.ambientMotion:   s.ambientMotion = v
        case T.crateCount:      s.crateCount = Int(v.rounded())
        case T.flowCount:       s.flowCount = Int(v.rounded())
        case T.cdCases:         s.cdCaseCount = Int(v.rounded())
        case T.polaroids:       s.polaroidCount = Int(v.rounded())
        case T.zoetropeCount:   s.zoetropeCount = Int(v.rounded())
        case T.orbitCount:      s.orbitCount = Int(v.rounded())
        case T.orbitSpeed:      s.orbitSpeed = v
        case T.crtAmber:
            s.crtAmber = (sender as? NSButton)?.state == .on
            save(); return
        case T.galleryColumns:  s.galleryColumns = Int(v.rounded())
        case T.vinylSeconds: s.vinylSecondsPerRecord = v
        case T.vinylRPM:
            // Snap to true LP / single speeds if the slider lands near them.
            for standard in [33.33, 45.0] where abs(v - standard) < 1.2 {
                s.vinylRPM = standard
                slider(T.vinylRPM)?.doubleValue = standard
                valueLabels[T.vinylRPM]?.stringValue = formatters[T.vinylRPM]?(standard) ?? ""
                save(); return
            }
            s.vinylRPM = v
        case T.vinylFollow:
            s.vinylFollowNowPlaying = (sender as? NSButton)?.state == .on
            save(); return
        default: break
        }
        if let l = valueLabels[sender.tag], let f = formatters[sender.tag] {
            l.stringValue = f(v)
        }
        save()
    }

    @objc private func resetDefaults() {
        s = Settings.default
        save()
        rebuildModeSection()
    }

    @objc private func closeWindow() { window?.close() }

    private func save() { SharedStore.saveSettings(s) }
}
