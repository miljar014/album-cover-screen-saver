import Foundation
import AppKit

/// Keeps the archive growing and the art cache filled.
///
/// The master archive lives outside the screen saver's sandbox container so it
/// survives a container reset; every save mirrors it into the container where
/// the saver can read it.
final class Poller {
    static let shared = Poller()

    private(set) var archive = Archive()
    private var timer: Timer?
    private var nowPlayingTimer: Timer?
    private var nowPlayingInterval: Double = 0
    private var lastPublish = Date.distantPast
    private(set) var nowPlaying = NowPlaying()
    var onChange: (() -> Void)?
    private(set) var lastStatus = "Idle"
    private var running = false

    func start() {
        SharedStore.migrateLegacyFolders()
        archive = SharedStore.loadArchive()   // newest of every shared location
        SharedStore.ensureDirs()
        timer?.invalidate()
        let t = Timer(timeInterval: Config.pollMinutes * 60, repeats: true) {
            [weak self] _ in self?.poll()
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
        scheduleNowPlaying(Config.nowPlayingSeconds)
        poll()
        pollNowPlaying()
        fetchAccount()
    }

    var albumCount: Int { archive.albums.count }

    // MARK: polling

    func poll(force: Bool = false) {
        guard Sources.active.isConfigured, !running else { return }
        running = true
        status("Checking Spotify…")

        let seedNeeded = archive.albums.isEmpty || force
        Sources.active.recentPlays(afterMs: archive.lastCursorMs) { [weak self] result in
            guard let self else { return }
            switch result {
            case .failure(let e):
                self.running = false
                self.status("Error: \(e.localizedDescription)")
            case .success(let plays):
                self.merge(plays: plays)
                if seedNeeded { self.seedFromTop() } else { self.finishPoll(added: plays.count) }
            }
        }
    }

    /// First run (or manual rebuild): pull top tracks across all three time ranges
    /// so the collage starts out full instead of showing four covers.
    private func seedFromTop() {
        status("Seeding from your top albums…")
        Sources.active.topAlbums { [weak self] result in
            guard let self else { return }
            if case .success(let albums) = result {
                for var a in albums where self.archive.albums[a.id] == nil {
                    // Seeded albums have no known play time; stagger them behind
                    // real plays so recency bias still favours actual listening.
                    a.lastPlayed = Date(timeIntervalSince1970: 1)
                    self.archive.albums[a.id] = a
                }
            }
            self.finishPoll(added: 0)
        }
    }

    private func merge(plays: [Play]) {
        for p in plays {
            let ms = Int(p.playedAt.timeIntervalSince1970 * 1000)
            archive.lastCursorMs = max(archive.lastCursorMs, ms)
            if var existing = archive.albums[p.album.id] {
                existing.playCount += 1
                existing.lastPlayed = max(existing.lastPlayed, p.playedAt)
                existing.imageURL = p.album.imageURL
                archive.albums[p.album.id] = existing
            } else {
                var a = p.album
                a.playCount = 1
                a.lastPlayed = p.playedAt
                a.firstSeen = Date()
                archive.albums[a.id] = a
            }
        }
    }

    private func finishPoll(added: Int) {
        archive.updated = Date()
        let wrote = save()
        downloadMissingArt { [weak self] in
            guard let self else { return }
            self.running = false
            let warn = wrote < SharedStore.roots.count ? " (⚠︎ \(wrote)/\(SharedStore.roots.count) locations)" : ""
            self.status(added > 0
                ? "Added \(added) play\(added == 1 ? "" : "s") · \(self.albumCount) albums\(warn)"
                : "Up to date · \(self.albumCount) albums\(warn)")
        }
    }

    /// Writes to every shared location; returns how many accepted the write.
    @discardableResult
    func save() -> Int {
        SharedStore.ensureDirs()
        return SharedStore.saveArchive(archive)
    }

    /// Looks up who we are signed in as. Email needs `user-read-email`, so this
    /// stays empty until the user re-authorises after that scope was added.
    func fetchAccount() {
        if Config.sourceKind == .lastfm {
            Config.setAccountLabel(Sources.active.accountLabel)
            onChange?()
            return
        }
        guard SpotifyAuth.shared.isSignedIn else { return }
        SpotifyAPI.me { [weak self] result in
            guard case .success(let me) = result else { return }
            let label = !me.email.isEmpty ? me.email
                      : (!me.name.isEmpty ? me.name : nil)
            Config.setAccountLabel(label)
            self?.onChange?()
        }
    }

    // MARK: now playing

    /// Publishes the currently playing track for the Hero + Grid style, and folds
    /// its album into the archive straight away so the cover is on disk by the
    /// time the screen saver wants to show it.
    /// Polls fast while music plays, slowly when it doesn't.
    private func scheduleNowPlaying(_ interval: Double) {
        guard interval != nowPlayingInterval else { return }
        nowPlayingInterval = interval
        nowPlayingTimer?.invalidate()
        let t = Timer(timeInterval: interval, repeats: true) { [weak self] _ in
            self?.pollNowPlaying()
        }
        RunLoop.main.add(t, forMode: .common)
        nowPlayingTimer = t
    }

    /// Publishes only when the track actually changed, or every 10s to keep the
    /// position snapshot honest. Avoids rewriting the file every few seconds.
    private func publish(_ np: NowPlaying) {
        let changed = np.albumID != nowPlaying.albumID
                   || np.track != nowPlaying.track
                   || np.isPlaying != nowPlaying.isPlaying
        if changed || Date().timeIntervalSince(lastPublish) > 10 {
            lastPublish = Date()
            SharedStore.saveNowPlaying(np)
        }
    }

    func pollNowPlaying() {
        guard Sources.active.isConfigured else { return }
        Sources.active.nowPlaying { [weak self] result in
            guard let self, case .success(let np) = result else { return }

            guard np.isPlaying, !np.albumID.isEmpty else {
                self.scheduleNowPlaying(Config.nowPlayingIdleSeconds)
                self.publish(np)
                self.nowPlaying = np
                self.onChange?()
                return
            }
            self.scheduleNowPlaying(Config.nowPlayingSeconds)

            // A track can easily be the first play of an album we've never seen;
            // record it now rather than waiting for the next 30-minute sweep.
            if self.archive.albums[np.albumID] == nil {
                self.archive.albums[np.albumID] = AlbumEntry(
                    id: np.albumID, name: np.albumName, artist: np.artist,
                    imageURL: np.imageURL, firstSeen: Date(),
                    lastPlayed: Date(), playCount: 1)
                self.save()
            } else {
                self.archive.albums[np.albumID]?.lastPlayed = Date()
            }

            self.onChange?()

            if SharedStore.hasArt(np.albumID) {
                self.publish(np)
                self.nowPlaying = np
            } else if let u = URL(string: np.imageURL) {
                // Publish only once the art exists, so the saver never features an
                // album it has no cover for.
                DispatchQueue.global(qos: .utility).async {
                    if let d = try? Data(contentsOf: u), d.count > 512 {
                        SharedStore.writeArt(d, for: np.albumID)
                    }
                    DispatchQueue.main.async {
                        self.publish(np)
                        self.nowPlaying = np
                    }
                }
            }
        }
    }

    // MARK: art cache

    private func downloadMissingArt(done: @escaping () -> Void) {
        // "Missing" means missing from ANY shared location, so a root that the
        // saver might read never ends up with an archive but no covers.
        let missing = archive.albums.values.filter { !SharedStore.hasArt($0.id) }
        guard !missing.isEmpty else { done(); return }
        status("Fetching \(missing.count) album cover\(missing.count == 1 ? "" : "s")…")

        let group = DispatchGroup()
        // Modest concurrency — this runs in the background, no need to hammer the
        // CDN, and fewer threads means less contention on the shared cache.
        let sem = DispatchSemaphore(value: 3)
        for album in missing {
            group.enter()
            let fetch: (String?) -> Void = { urlString in
                DispatchQueue.global(qos: .utility).async {
                    sem.wait()
                    defer { sem.signal(); group.leave() }
                    guard let s = urlString, let url = URL(string: s),
                          let data = try? Data(contentsOf: url), data.count > 512 else { return }
                    SharedStore.writeArt(data, for: album.id)
                }
            }
            if album.imageURL.isEmpty {
                // Last.fm often has no artwork of its own; look it up rather than
                // leaving a hole in the collage. `fetch` always runs, even on
                // failure, so the group can never be left waiting.
                ArtworkLookup.find(artist: album.artist, album: album.name, done: fetch)
            } else {
                fetch(album.imageURL)
            }
        }
        group.notify(queue: .main) { done() }
    }

    private func status(_ s: String) {
        lastStatus = s
        DispatchQueue.main.async { self.onChange?() }
    }
}
