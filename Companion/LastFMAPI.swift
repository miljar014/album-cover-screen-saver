import Foundation

/// Last.fm listening history.
///
/// No OAuth and no per-user registration: one API key plus a username. That is
/// what makes it viable for an audience of any size. It also scrobbles from
/// Spotify, Apple Music and YouTube Music alike, and works with free accounts.
struct LastFMSource: MusicSource {
    private var key: String { Config.lastFMKey }
    private var user: String { Config.lastFMUser }

    var isConfigured: Bool { !key.isEmpty && !user.isEmpty }
    var accountLabel: String? { user.isEmpty ? nil : "\(user) · Last.fm" }

    private func url(_ method: String, _ extra: [String: String] = [:]) -> URL? {
        var c = URLComponents(string: "https://ws.audioscrobbler.com/2.0/")!
        var items = [URLQueryItem(name: "method", value: method),
                     URLQueryItem(name: "user", value: user),
                     URLQueryItem(name: "api_key", value: key),
                     URLQueryItem(name: "format", value: "json")]
        items += extra.map { URLQueryItem(name: $0.key, value: $0.value) }
        c.queryItems = items
        return c.url
    }

    private func get(_ method: String, _ extra: [String: String] = [:],
                     done: @escaping (Result<[String: Any], Error>) -> Void) {
        guard isConfigured, let u = url(method, extra) else {
            done(.failure(Err("Last.fm isn't set up yet."))); return
        }
        URLSession.shared.dataTask(with: u) { data, _, err in
            DispatchQueue.main.async {
                if let err { done(.failure(err)); return }
                guard let data,
                      let j = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
                else { done(.failure(Err("Unreadable response from Last.fm."))); return }
                if let message = j["message"] as? String { done(.failure(Err(message))); return }
                done(.success(j))
            }
        }.resume()
    }

    // MARK: history

    func recentPlays(afterMs: Int, done: @escaping (Result<[Play], Error>) -> Void) {
        var extra = ["limit": "200"]
        if afterMs > 0 { extra["from"] = String(afterMs / 1000) }   // Last.fm uses seconds
        get("user.getrecenttracks", extra) { result in
            done(result.map { j in
                Self.tracks(from: j).compactMap { t in
                    // The currently playing track has no timestamp; it is reported
                    // separately by nowPlaying() and must not enter the history.
                    guard let dateNode = t["date"] as? [String: Any],
                          let uts = dateNode["uts"] as? String, let secs = Double(uts),
                          let album = Self.album(from: t) else { return nil }
                    return Play(album: album, playedAt: Date(timeIntervalSince1970: secs))
                }
            })
        }
    }

    func topAlbums(done: @escaping (Result<[AlbumEntry], Error>) -> Void) {
        get("user.gettopalbums", ["period": "overall", "limit": "150"]) { result in
            done(result.map { j in
                let root = j["topalbums"] as? [String: Any]
                let list = root?["album"] as? [[String: Any]] ?? []
                return list.compactMap { a -> AlbumEntry? in
                    guard let name = a["name"] as? String,
                          let artist = (a["artist"] as? [String: Any])?["name"] as? String
                    else { return nil }
                    return AlbumEntry(id: AlbumEntry.makeID(artist: artist, album: name),
                                      name: name, artist: artist,
                                      imageURL: Self.image(from: a["image"]),
                                      firstSeen: Date(), lastPlayed: Date(timeIntervalSince1970: 1),
                                      playCount: 0)
                }
            })
        }
    }

    func nowPlaying(done: @escaping (Result<NowPlaying, Error>) -> Void) {
        get("user.getrecenttracks", ["limit": "1"]) { result in
            done(result.map { j in
                guard let t = Self.tracks(from: j).first,
                      let attr = t["@attr"] as? [String: Any],
                      (attr["nowplaying"] as? String) == "true",
                      let album = Self.album(from: t) else {
                    return NowPlaying(isPlaying: false, updated: Date())
                }
                // Last.fm reports no playback position, so duration is left at 0
                // and the visuals fall back to their timers.
                return NowPlaying(albumID: album.id, albumName: album.name,
                                  artist: album.artist,
                                  track: t["name"] as? String ?? "",
                                  imageURL: album.imageURL,
                                  isPlaying: true, updated: Date())
            })
        }
    }

    // MARK: parsing

    private static func tracks(from j: [String: Any]) -> [[String: Any]] {
        let root = j["recenttracks"] as? [String: Any]
        if let many = root?["track"] as? [[String: Any]] { return many }
        if let one = root?["track"] as? [String: Any] { return [one] }   // single result
        return []
    }

    private static func album(from t: [String: Any]) -> AlbumEntry? {
        let artist = (t["artist"] as? [String: Any])?["#text"] as? String
                  ?? (t["artist"] as? [String: Any])?["name"] as? String ?? ""
        let name = (t["album"] as? [String: Any])?["#text"] as? String ?? ""
        guard !artist.isEmpty, !name.isEmpty else { return nil }   // singles have no album
        return AlbumEntry(id: AlbumEntry.makeID(artist: artist, album: name),
                          name: name, artist: artist,
                          imageURL: image(from: t["image"]),
                          firstSeen: Date(), lastPlayed: Date(), playCount: 0)
    }

    /// Last.fm serves a grey star placeholder for artwork it doesn't have, which
    /// is common. Treating that as a real image would fill the collage with
    /// identical grey squares, so it is rejected and looked up elsewhere.
    private static let placeholder = "2a96cbd8b46e442fc41c2b86b821562f"

    private static func image(from node: Any?) -> String {
        guard let images = node as? [[String: Any]] else { return "" }
        let preferred = ["extralarge", "large", "medium"]
        for size in preferred {
            if let hit = images.first(where: { ($0["size"] as? String) == size }),
               let url = hit["#text"] as? String,
               !url.isEmpty, !url.contains(placeholder) {
                return url
            }
        }
        return ""
    }
}

/// Cover art of last resort.
///
/// Free, keyless, and with far better coverage than Last.fm's own images.
enum ArtworkLookup {
    static func find(artist: String, album: String,
                     done: @escaping (String?) -> Void) {
        var c = URLComponents(string: "https://itunes.apple.com/search")!
        c.queryItems = [.init(name: "term", value: "\(artist) \(album)"),
                        .init(name: "entity", value: "album"),
                        .init(name: "limit", value: "1")]
        guard let u = c.url else { done(nil); return }
        URLSession.shared.dataTask(with: u) { data, _, _ in
            guard let data,
                  let j = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let first = (j["results"] as? [[String: Any]])?.first,
                  let art = first["artworkUrl100"] as? String else { done(nil); return }
            // The 100px thumbnail URL resizes simply by substitution.
            done(art.replacingOccurrences(of: "100x100bb", with: "600x600bb"))
        }.resume()
    }
}
