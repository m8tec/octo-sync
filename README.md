# Octo-Sync

Sync selected playlists from music services into a Subsonic-compatible server (for example Navidrome), with support for downloading missing tracks if used with the Subsonic API proxy server [Octo-Fiesta](https://github.com/V1ck3s/octo-fiesta).

On every cycle, Octo-Sync fetches configured source playlists, resolves tracks in the target library, and updates the target playlist.

<img width="1194" height="808" alt="Playlists Screenshot" src="https://github.com/user-attachments/assets/82aaa84c-cd37-45a4-83ab-3eacfa22e016" />

### Supported Playlist Sources
| Source         | Credentials required       | Private playlists |
|----------------|----------------------------|-------------------|
| Apple Music    | No                         | No                |
| Deezer         | No                         | No                |
| ListenBrainz   | Yes                        | Yes               |
| Last.fm        | No                         | No                |
| Qobuz          | No                         | No                |
| Spotify        | No                         | No                |
| TIDAL          | Yes                        | Yes               |
| YouTube Music  | No                         | No                |
| Csv files      | No                         | No                |

See the [Supported Playlist Sources](https://github.com/m8tec/octo-sync/wiki/Supported-Playlist-Sources) wiki page for detailed information.

## Quick Start

### Requirements

- Docker Compose (recommended)
- A running Navidrome instance
- Playlists you want to mirror :)

### Docker Installation (Recommended)

```bash
# Clone the repository
git clone https://github.com/m8tec/octo-sync.git
cd octo-sync

# Configure
cp .env.example .env
nano .env  # Edit with your settings

# Start
docker-compose up -d

# Watch logs
docker-compose logs -f
```

See the [Installation](https://github.com/m8tec/octo-sync/wiki/Installation) wiki page for detailed instructions.

## Configuration

All runtime configuration is supplied through environment variables in `.env`.

For the playlist configuration, see the [Supported Playlist Sources](https://github.com/m8tec/octo-sync/wiki/Supported-Playlist-Sources) wiki page.

## License

GPL-3.0

## Acknowledgments

- [Octo-Fiesta](https://github.com/V1ck3s/octo-fiesta) - The Subsonic API proxy server that led me to create Octo-Sync in the first place and heavily inspired it
- [Navidrome](https://www.navidrome.org/) - The excellent self-hosted music server
