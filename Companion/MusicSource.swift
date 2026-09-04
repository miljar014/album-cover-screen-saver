import Foundation

/// One observed play.
struct Play {
    let album: AlbumEntry
    let playedAt: Date
}

/// Where listening history comes from.
///
/// Spotify caps a development-mode app at 5 authorised listeners and has closed
/// Extended Quota Mode to individuals, so it cannot serve a wide audience unless
/// every user registers their own app. Last.fm has no such cap: one API key
/// serves everyone, and reading a history needs only a username.
enum MusicSourceKind: String, Codable, CaseIterable {
    case local
    case lastfm
    case spotify

    var title: String {
        switch self {
        case .local:   return "This Mac"
        case .lastfm:  return "Last.fm"
        case .spotify: return "Spotify"
        }
    }

    var blurb: String {
        switch self {
        case .local:
            return "Reads whatever is playing on this Mac — Spotify, Apple Music, "
                 + "anything. No account, nothing to set up. Builds its collage from "
                 + "what you play from now on."
        case .lastfm:
            return "A free Last.fm account records everything you play, on any "
                 + "service. Gives the collage your whole listening history from "
                 + "day one."
        case .spotify:
            return "Advanced. Your full Spotify history straight away, with no Last.fm "
                 + "account — but Spotify caps any app at 5 listeners, so this needs "
                 + "your own developer registration and Spotify Premium. Most people "
                 + "want Last.fm instead."
        }
    }
}

protocol MusicSource {
    /// Ready to make requests.
    var isConfigured: Bool { get }
    /// What to show as the connected account.
    var accountLabel: String? { get }
    /// Plays newer than `afterMs` (0 = as much as the service will give).
    func recentPlays(afterMs: Int, done: @escaping (Result<[Play], Error>) -> Void)
    /// A deeper seed so the collage starts full.
    func topAlbums(done: @escaping (Result<[AlbumEntry], Error>) -> Void)
    /// What is playing right now.
    func nowPlaying(done: @escaping (Result<NowPlaying, Error>) -> Void)
}

enum Sources {
    static var active: MusicSource {
        switch Config.sourceKind {
        case .local:   return LocalPlayerSource()
        case .lastfm:  return LastFMSource()
        case .spotify: return SpotifySource()
        }
    }
}

/// Adapter over the existing Spotify client.
struct SpotifySource: MusicSource {
    var isConfigured: Bool { Config.hasClientID && SpotifyAuth.shared.isSignedIn }
    var accountLabel: String? { Config.accountLabel }

    func recentPlays(afterMs: Int, done: @escaping (Result<[Play], Error>) -> Void) {
        SpotifyAPI.recentlyPlayed(after: afterMs, done: done)
    }
    func topAlbums(done: @escaping (Result<[AlbumEntry], Error>) -> Void) {
        var all: [AlbumEntry] = []
        let ranges = ["short_term", "medium_term", "long_term"]
        var remaining = ranges.count
        for r in ranges {
            SpotifyAPI.topTracks(range: r) { result in
                if case .success(let albums) = result { all.append(contentsOf: albums) }
                remaining -= 1
                if remaining == 0 { done(.success(all)) }
            }
        }
    }
    func nowPlaying(done: @escaping (Result<NowPlaying, Error>) -> Void) {
        SpotifyAPI.currentlyPlaying(done: done)
    }
}
