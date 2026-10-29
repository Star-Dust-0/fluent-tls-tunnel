# Changelog

## v0.2.1

Republished under the Star-Dust-0 repository, recovered from the original source. Same app code as the original v0.2.1; packaging changes only: the installer now installs to C:\Program Files\FluentTlsTunnel (requires administrator) and offers a desktop shortcut checked by default, and the in-app update check now looks at the Star-Dust-0 repository.

Original v0.2.1 changes:
- Fixed browsing failing with connection-closed errors while connected: DNS answers now always stay on IPv4 (AAAA queries receive an empty answer), keeping the IPv6 routes inside the tunnel so no IPv6 traffic leaks.
- After routing is applied, the app tests HTTPS by hostname; if browsing does not work, routing is restored automatically and a clear message is shown.
- Improved restoring the window from the tray on a second launch.
- The connection log now reports a rate-limited summary of SOCKS channel failures.
- Updated the single-instance test to match closing to the tray.
- Three bundled-runtime downloads: portable, installer and full offline kit.


## v0.2.0
- Added pooled HTTPS DNS queries through the tunnel, with fallback across Cloudflare resolvers, retries and short-lived caching.
- Enlarged and widened the connection log and added a Clear log button.
- Closing the window now minimizes the app to the system tray; the tray menu has a Quit command.
- Encrypted .ftlsc imports hide values in both the confirmation dialog and settings fields. The real values remain in use; typing into a field replaces its hidden value.
- Three bundled-runtime downloads: portable, installer and full offline kit.


## v0.1.9
- Fixed automatic routing being blocked by ordinary Windows DNS policies for specific domains (for example Windows DNS resiliency rules).
- Remove only this app's own stale DNS rules at startup; rules owned by Windows or other software are never modified.
- Stop only when another catch-all DNS policy is active, and name that policy in the message.
- Three bundled-runtime downloads: portable, installer and full offline kit.


## v0.1.8
- Fixed connection failure when the Windows credential vault is unavailable.
- Store passwords with current-user Windows DPAPI encryption, keeping them out of profile/export files.
- Continue connecting with the entered password if secure persistence is unavailable.
- Three bundled-runtime downloads: portable, installer and full offline kit.

