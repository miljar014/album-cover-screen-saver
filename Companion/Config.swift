import Foundation

enum Config {
    /// The Client ID is supplied by whoever runs the app, not baked into the
    /// source.
    ///
    /// Spotify caps a Development Mode app at 5 authorised users, and Extended
    /// Quota Mode has been closed to individuals since May 2025 (it now requires
    /// a registered business with 250k+ monthly active users). So a shared build
    /// cannot use one common Client ID — the sixth person would simply be refused.
    /// Instead each person registers their own free Spotify app, which costs a few
    /// minutes and gives them their own allowance.
    private static let key = "spotifyClientID"

    /// Compiled into the build from the `ClientID` file, if there is one. When
    /// present the people you have added to your Spotify app just sign in; when
    /// absent each user registers their own app.
    static var bundledClientID: String {
        (Bundle.main.infoDictionary?["SpotifyClientID"] as? String ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    /// True when this build ships with an ID, so setup can be skipped entirely.
    static var usesBundledClientID: Bool {
        let id = bundledClientID
        return id.count == 32 && id.allSatisfy(\.isHexDigit)
    }

    /// A user-entered ID always wins, so a bundled build can still be pointed at
    /// someone else's registration.
    static var clientID: String {
        let stored = UserDefaults.standard.string(forKey: key)?
            .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return stored.isEmpty ? bundledClientID : stored
    }

    static func setClientID(_ id: String) {
        UserDefaults.standard.set(
            id.trimmingCharacters(in: .whitespacesAndNewlines), forKey: key)
    }

    /// Spotify Client IDs are 32 hex characters.
    static var hasClientID: Bool {
        let id = clientID
        return id.count == 32 && id.allSatisfy(\.isHexDigit)
    }

    // MARK: source

    private static let advancedKey = "showAdvancedSources"

    /// Spotify is kept but demoted: it needs Premium, a developer app, and caps
    /// out at 5 listeners, so it is the wrong answer for nearly everyone. Hidden
    /// rather than removed, because it is still the only source that gives a full
    /// Spotify history with no Last.fm account.
    static var showAdvancedSources: Bool {
        get { UserDefaults.standard.bool(forKey: advancedKey) }
        set { UserDefaults.standard.set(newValue, forKey: advancedKey) }
    }

    private static let sourceKey = "musicSource"
    private static let lastFMUserKey = "lastfmUser"

    static var sourceKind: MusicSourceKind {
        get {
            // Default to the source that needs nothing from the user.
            MusicSourceKind(rawValue: UserDefaults.standard.string(forKey: sourceKey) ?? "")
                ?? .local
        }
        set { UserDefaults.standard.set(newValue.rawValue, forKey: sourceKey) }
    }

    /// Baked in from the LastFMKey file, the same way the Spotify Client ID is.
    /// One key serves every user, so nobody has to register anything.
    static var lastFMKey: String {
        (Bundle.main.infoDictionary?["LastFMAPIKey"] as? String ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }
    static var usesBundledLastFMKey: Bool { lastFMKey.count >= 20 }

    static var lastFMUser: String {
        get {
            UserDefaults.standard.string(forKey: lastFMUserKey)?
                .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        }
        set { UserDefaults.standard.set(newValue.trimmingCharacters(in: .whitespacesAndNewlines),
                                        forKey: lastFMUserKey) }
    }

    /// Cached so the menu can name the account without a request on every open.
    private static let accountKey = "spotifyAccountLabel"

    static var accountLabel: String? {
        let v = UserDefaults.standard.string(forKey: accountKey)
        return (v?.isEmpty ?? true) ? nil : v
    }

    static func setAccountLabel(_ label: String?) {
        UserDefaults.standard.set(label ?? "", forKey: accountKey)
    }

    /// How often to poll Spotify for new plays, in minutes.
    /// Spotify only keeps the last 50 plays, so polling is what makes the
    /// archive grow beyond that window. 30 min is plenty for normal listening.
    static let pollMinutes: Double = 30

    /// How often to check what's playing, in seconds, while music is playing.
    /// Low so that skipping a track shows up almost immediately; it's one very
    /// small request, well inside Spotify's rate limits.
    static let nowPlayingSeconds: Double = 3

    /// Slower cadence when nothing is playing — no reason to poll hard then.
    static let nowPlayingIdleSeconds: Double = 20
}
