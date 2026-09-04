import Foundation
import AppKit

/// Whatever is playing on this Mac, read straight from the player app.
///
/// Chosen over the private `MediaRemote` framework, which Apple broke for
/// directly-linked apps in macOS 15.4 and which now only works through a
/// bundled adapter that launches `/usr/bin/perl` to borrow its access. That is a
/// workaround around a deliberate restriction, and it would sit in the update
/// path of every user forever.
///
/// Player apps publish their own scripting interfaces, which are supported,
/// stable, and give something MediaRemote-style access does not reliably
/// provide: exact playback position, so the tonearm and tape reels track the
/// real song.
///
/// The cost is one macOS permission prompt on first use, and that it only sees
/// the desktop apps — not the web player or a phone.
struct LocalPlayerSource: MusicSource {

    /// Nothing to configure. That is the entire point of this source.
    var isConfigured: Bool { true }
    var accountLabel: String? { "This Mac" }

    /// No history: a player knows what it is playing, not what you played last
    /// week. The archive is instead built from observation, one track at a time.
    func recentPlays(afterMs: Int, done: @escaping (Result<[Play], Error>) -> Void) {
        done(.success([]))
    }
    func topAlbums(done: @escaping (Result<[AlbumEntry], Error>) -> Void) {
        done(.success([]))
    }

    func nowPlaying(done: @escaping (Result<NowPlaying, Error>) -> Void) {
        Self.queue.async {
            let result = Self.query()
            DispatchQueue.main.async { done(.success(result)) }
        }
    }

    // NSAppleScript is not thread-safe; keep every call on one serial queue and
    // off the main thread so a slow player cannot stutter the UI.
    private static let queue = DispatchQueue(label: "com.jaredmiller.AlbumCoverScreenSaver.localplayer")

    private static func query() -> NowPlaying {
        if let np = run(spotifyScript, source: "Spotify") { return np }
        if let np = run(musicScript, source: "Music") { return np }
        return NowPlaying(isPlaying: false, updated: Date())
    }

    /// `application "X" is running` deliberately does not launch the app — a
    /// `tell` block would, which is not something a screen saver should do.
    private static let spotifyScript = """
    if application "Spotify" is running then
      tell application "Spotify"
        if player state is playing then
          set t to current track
          return (name of t) & "\\n" & (artist of t) & "\\n" & (album of t) & "\\n" \\
               & (artwork url of t) & "\\n" & (duration of t) & "\\n" & (player position)
        end if
      end tell
    end if
    return ""
    """

    private static let musicScript = """
    if application "Music" is running then
      tell application "Music"
        if player state is playing then
          set t to current track
          return (name of t) & "\\n" & (artist of t) & "\\n" & (album of t) & "\\n" \\
               & "" & "\\n" & ((duration of t) * 1000) & "\\n" & (player position)
        end if
      end tell
    end if
    return ""
    """

    private static func run(_ source: String, source name: String) -> NowPlaying? {
        var error: NSDictionary?
        guard let script = NSAppleScript(source: source) else { return nil }
        let out = script.executeAndReturnError(&error)
        if let error {
            // -1743 is the user declining the automation prompt. Record it once so
            // the menu can explain rather than silently showing nothing.
            let code = error[NSAppleScript.errorNumber] as? Int ?? 0
            if code == -1743 { permissionDenied = true }
            return nil
        }
        guard let text = out.stringValue, !text.isEmpty else { return nil }

        let parts = text.components(separatedBy: "\n")
        guard parts.count >= 6 else { return nil }
        let track = parts[0], artist = parts[1], album = parts[2]
        guard !artist.isEmpty, !album.isEmpty else { return nil }

        let durationMs = Int(Double(parts[4]) ?? 0)
        let positionMs = Int((Double(parts[5]) ?? 0) * 1000)

        return NowPlaying(albumID: AlbumEntry.makeID(artist: artist, album: album),
                          albumName: album, artist: artist, track: track,
                          imageURL: parts[3],
                          isPlaying: true, updated: Date(),
                          progressMs: positionMs, durationMs: durationMs)
    }

    /// Set from the script queue, read from the main thread.
    private static let flagLock = NSLock()
    private static var _permissionDenied = false
    static var permissionDenied: Bool {
        get { flagLock.lock(); defer { flagLock.unlock() }; return _permissionDenied }
        set { flagLock.lock(); _permissionDenied = newValue; flagLock.unlock() }
    }
}
