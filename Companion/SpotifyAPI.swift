import Foundation

/// Minimal read-only client for the two endpoints the collage needs.
enum SpotifyAPI {

    private static func get(_ path: String,
                            done: @escaping (Result<[String: Any], Error>) -> Void) {
        SpotifyAuth.shared.accessToken { result in
            switch result {
            case .failure(let e): done(.failure(e))
            case .success(let token):
                var r = URLRequest(url: URL(string: "https://api.spotify.com/v1" + path)!)
                r.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
                URLSession.shared.dataTask(with: r) { data, resp, err in
                    DispatchQueue.main.async {
                        if let err { done(.failure(err)); return }
                        if let http = resp as? HTTPURLResponse, http.statusCode == 429 {
                            done(.failure(Err("Rate limited by Spotify; will retry next poll.")))
                            return
                        }
                        if let http = resp as? HTTPURLResponse, http.statusCode == 204 {
                            done(.success([:]))   // nothing playing
                            return
                        }
                        guard let data,
                              let j = try? JSONSerialization.jsonObject(with: data) as? [String: Any]
                        else { done(.failure(Err("Unreadable response from \(path)"))); return }
                        if let e = (j["error"] as? [String: Any])?["message"] as? String {
                            done(.failure(Err(e))); return
                        }
                        done(.success(j))
                    }
                }.resume()
            }
        }
    }

    /// Last 50 plays. `after` is a ms timestamp — passing the previous high-water
    /// mark means we only ingest genuinely new plays.
    static func recentlyPlayed(after ms: Int,
                               done: @escaping (Result<[Play], Error>) -> Void) {
        var path = "/me/player/recently-played?limit=50"
        if ms > 0 { path += "&after=\(ms)" }
        get(path) { result in
            done(result.map { j in
                (j["items"] as? [[String: Any]] ?? []).compactMap { item in
                    guard let track = item["track"] as? [String: Any],
                          let album = parseAlbum(track["album"] as? [String: Any]),
                          let ts = item["played_at"] as? String,
                          let date = isoDate(ts) else { return nil }
                    return Play(album: album, playedAt: date)
                }
            })
        }
    }

    /// Seeds the archive with depth on first run: 50 tracks per time range.
    static func topTracks(range: String,
                          done: @escaping (Result<[AlbumEntry], Error>) -> Void) {
        get("/me/top/tracks?limit=50&time_range=\(range)") { result in
            done(result.map { j in
                (j["items"] as? [[String: Any]] ?? []).compactMap {
                    parseAlbum($0["album"] as? [String: Any])
                }
            })
        }
    }

    /// What's playing right now. Returns an entry with `isPlaying == false`
    /// when Spotify is idle, so callers can clear a stale feature.
    static func currentlyPlaying(done: @escaping (Result<NowPlaying, Error>) -> Void) {
        get("/me/player/currently-playing") { result in
            done(result.map { j in
                guard !j.isEmpty,
                      let item = j["item"] as? [String: Any],
                      let album = parseAlbum(item["album"] as? [String: Any])
                else { return NowPlaying(isPlaying: false, updated: Date()) }
                return NowPlaying(albumID: album.id,
                                  albumName: album.name,
                                  artist: album.artist,
                                  track: item["name"] as? String ?? "",
                                  imageURL: album.imageURL,
                                  isPlaying: (j["is_playing"] as? Bool) ?? false,
                                  updated: Date(),
                                  progressMs: (j["progress_ms"] as? Int) ?? 0,
                                  durationMs: (item["duration_ms"] as? Int) ?? 0)
            })
        }
    }

    /// The signed-in account, for showing who the app is connected as.
    static func me(done: @escaping (Result<(name: String, email: String), Error>) -> Void) {
        get("/me") { result in
            done(result.map { j in
                (j["display_name"] as? String ?? "",
                 j["email"] as? String ?? "")
            })
        }
    }

    // MARK: parsing

    private static func parseAlbum(_ a: [String: Any]?) -> AlbumEntry? {
        guard let a, let id = a["id"] as? String, let name = a["name"] as? String else { return nil }
        let artist = (a["artists"] as? [[String: Any]])?.first?["name"] as? String ?? "Unknown"
        let images = a["images"] as? [[String: Any]] ?? []
        // images come largest-first; prefer ~300px, fall back to the largest available.
        let url = (images.first { ($0["width"] as? Int ?? 0) <= 400 }
                   ?? images.first)?["url"] as? String
        guard let url else { return nil }
        return AlbumEntry(id: id, name: name, artist: artist, imageURL: url,
                          firstSeen: Date(), lastPlayed: .distantPast, playCount: 0)
    }

    private static func isoDate(_ s: String) -> Date? {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f.date(from: s) ?? {
            let g = ISO8601DateFormatter()
            g.formatOptions = [.withInternetDateTime]
            return g.date(from: s)
        }()
    }
}
