import Foundation
import CryptoKit
import AppKit
import Network

/// Authorization Code with PKCE. No client secret, so nothing sensitive ships
/// in the binary. Spotify permits plain-http redirects only on the loopback IP,
/// and requires 127.0.0.1 rather than "localhost".
final class SpotifyAuth {
    static let shared = SpotifyAuth()

    /// Set this in Config.swift after registering your app at
    /// https://developer.spotify.com/dashboard
    var clientID: String { Config.clientID }

    let port: UInt16 = 8888
    var redirectURI: String { "http://127.0.0.1:\(port)/callback" }
    let scopes = "user-read-recently-played user-top-read user-read-currently-playing "
               + "user-read-email user-read-private"

    private var verifier = ""
    private var state = ""
    private var listener: NWListener?
    private var completion: ((Result<Void, Error>) -> Void)?

    var isSignedIn: Bool { Keychain.get("refresh_token") != nil }

    // MARK: Sign in

    func signIn(completion: @escaping (Result<Void, Error>) -> Void) {
        guard Config.hasClientID else {
            completion(.failure(Err("No Spotify Client ID yet \u{2014} "
                                  + "run Spotify Setup from the menu bar.")))
            return
        }
        self.completion = completion
        verifier = Self.randomString(64)
        state = Self.randomString(16)

        let challenge = Self.base64URL(Data(SHA256.hash(data: Data(verifier.utf8))))
        var c = URLComponents(string: "https://accounts.spotify.com/authorize")!
        c.queryItems = [
            .init(name: "client_id", value: clientID),
            .init(name: "response_type", value: "code"),
            .init(name: "redirect_uri", value: redirectURI),
            .init(name: "code_challenge_method", value: "S256"),
            .init(name: "code_challenge", value: challenge),
            .init(name: "state", value: state),
            .init(name: "scope", value: scopes),
        ]
        do { try startListener() } catch { completion(.failure(error)); return }
        NSWorkspace.shared.open(c.url!)
    }

    func signOut() {
        Keychain.delete("refresh_token")
        Keychain.delete("access_token")
        Keychain.delete("expires_at")
    }

    // MARK: Loopback listener

    private func startListener() throws {
        listener?.cancel()
        let l = try NWListener(using: .tcp, on: NWEndpoint.Port(rawValue: port)!)
        l.newConnectionHandler = { [weak self] conn in
            conn.start(queue: .main)
            conn.receive(minimumIncompleteLength: 1, maximumLength: 8192) { data, _, _, _ in
                guard let self, let data, let req = String(data: data, encoding: .utf8) else { return }
                let body = self.handle(request: req)
                let resp = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n"
                    + "Content-Length: \(body.utf8.count)\r\nConnection: close\r\n\r\n\(body)"
                conn.send(content: Data(resp.utf8), completion: .contentProcessed { _ in
                    conn.cancel()
                })
            }
        }
        l.start(queue: .main)
        listener = l
    }

    /// Pulls ?code= and ?state= out of the GET request line.
    private func handle(request: String) -> String {
        guard let line = request.components(separatedBy: "\n").first,
              let path = line.split(separator: " ").dropFirst().first,
              let comps = URLComponents(string: "http://127.0.0.1\(path)") else {
            return page("Something went wrong.")
        }
        // Browsers issue extra requests to this origin (favicon, prefetch,
        // preconnect). Only the redirect path carries the authorization result;
        // anything else is answered and ignored.
        guard comps.path == "/callback" else { return page("") }

        let items = comps.queryItems ?? []
        let got = { (n: String) -> String? in items.first { $0.name == n }?.value }

        if let err = got("error") {
            finish(.failure(Err("Spotify returned: \(err)")))
            return page("Authorization denied.")
        }
        guard let code = got("code"), got("state") == state else {
            finish(.failure(Err("Bad callback (state mismatch).")))
            return page("Something went wrong.")
        }
        exchange(code: code)
        return page("Signed in. You can close this tab and return to Album Cover Screen Saver.")
    }

    private func page(_ msg: String) -> String {
        """
        <!doctype html><meta charset="utf-8"><title>AlbumCoverScreenSaver</title>
        <body style="font:16px -apple-system,system-ui,sans-serif;background:#121212;color:#eee;
        display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
        <p>\(msg)</p></body>
        """
    }

    // MARK: Token exchange / refresh

    private func exchange(code: String) {
        post(["grant_type": "authorization_code",
              "code": code,
              "redirect_uri": redirectURI,
              "client_id": clientID,
              "code_verifier": verifier]) { [weak self] result in
            switch result {
            case .success: self?.finish(.success(()))
            case .failure(let e): self?.finish(.failure(e))
            }
        }
    }

    /// Returns a usable access token, refreshing first if it's within 60s of expiry.
    func accessToken(_ done: @escaping (Result<String, Error>) -> Void) {
        let exp = Double(Keychain.get("expires_at") ?? "0") ?? 0
        if let tok = Keychain.get("access_token"), Date().timeIntervalSince1970 < exp - 60 {
            done(.success(tok)); return
        }
        guard let refresh = Keychain.get("refresh_token") else {
            done(.failure(Err("Not signed in."))); return
        }
        post(["grant_type": "refresh_token",
              "refresh_token": refresh,
              "client_id": clientID]) { result in
            switch result {
            case .success:
                if let tok = Keychain.get("access_token") { done(.success(tok)) }
                else { done(.failure(Err("Refresh succeeded but no token stored."))) }
            case .failure(let e):
                done(.failure(e))
            }
        }
    }

    private func post(_ fields: [String: String],
                      done: @escaping (Result<Void, Error>) -> Void) {
        var r = URLRequest(url: URL(string: "https://accounts.spotify.com/api/token")!)
        r.httpMethod = "POST"
        r.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        r.httpBody = Data(fields.map { k, v in
            "\(k)=\(v.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? v)"
        }.joined(separator: "&").utf8)

        URLSession.shared.dataTask(with: r) { data, _, err in
            DispatchQueue.main.async {
                if let err { done(.failure(err)); return }
                guard let data,
                      let j = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
                    done(.failure(Err("Unreadable token response."))); return
                }
                if let e = j["error_description"] as? String ?? j["error"] as? String {
                    done(.failure(Err(e))); return
                }
                guard let access = j["access_token"] as? String else {
                    done(.failure(Err("No access token in response."))); return
                }
                Keychain.set(access, for: "access_token")
                let ttl = (j["expires_in"] as? Double) ?? 3600
                Keychain.set(String(Date().timeIntervalSince1970 + ttl), for: "expires_at")
                // Spotify rotates refresh tokens; store the new one when present.
                if let rt = j["refresh_token"] as? String { Keychain.set(rt, for: "refresh_token") }
                done(.success(()))
            }
        }.resume()
    }

    private func finish(_ r: Result<Void, Error>) {
        DispatchQueue.main.async {
            // First result wins; later callbacks are ignored.
            guard let done = self.completion else { return }
            self.completion = nil
            self.listener?.cancel(); self.listener = nil
            done(r)
        }
    }

    // MARK: PKCE helpers

    static func randomString(_ n: Int) -> String {
        let set = Array("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~")
        return String((0..<n).map { _ in set[Int.random(in: 0..<set.count)] })
    }

    static func base64URL(_ d: Data) -> String {
        d.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}

struct Err: LocalizedError {
    let msg: String
    init(_ m: String) { msg = m }
    var errorDescription: String? { msg }
}
