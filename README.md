# Octo-Sync

Sync selected playlists from music services into a Subsonic-compatible server (for example Navidrome).

Octo-Sync runs as a small background worker. On every cycle, it fetches configured source playlists, resolves tracks in the target library, and updates the target playlist.

If used used with [Octo-Fiesta](https://github.com/V1ck3s/octo-fiesta), a Subsonic API proxy server that integrates multiple music streaming providers as sources, Octo-Sync will trigger downloads of tracks that are not yet downloaded to the local library.

## Supported sources
- TIDAL playlists
- ListenBrainz user-based playlists
- CSV playlist exports

## Requirements

- Docker Compose (recommended)
- A running Subsonic-compatible server (Navidrome works)
- Credentials of the source playlist providers you want to sync from

### Docker Installation

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

## Configuration

All runtime configuration is supplied through environment variables in `.env`.

### Getting Credentials

**TIDAL**: You need to create an app in the [TIDAL developers portal](https://developer.tidal.com/dashboard) to get a client ID and secret.
**ListenBrainz**: You can get your user token from the [ListenBrainz settings](https://listenbrainz.org/settings/).

### CSV playlists

- Put playlist export files into `./playlists` (or set `CSV_MOUNT_PATH` / `CSV_BASE_PATH` differently).
- All `.csv` files in the configured folder are imported automatically.
- Supported are common CSV layouts with differing column names/order and delimiters.
- Required fields are title and artist (for example `Track Name` + `Artist Name(s)`).
- Supported exporters: [Exportify](https://exportify.net/) (Spotify)

## Contributing

Contributions are welcome!

```bash
cd OctoSync
dotnet build
dotnet run
```

Set the same config values in `appsettings.Development.json`.

## License

GPL-3.0

## Acknowledgments

- [Octo-Fiesta](https://github.com/V1ck3s/octo-fiesta) - The Subsonic API proxy server that inspired this project
- [Navidrome](https://www.navidrome.org/) - The excellent self-hosted music server
- [Subsonic API](http://www.subsonic.org/pages/api.jsp) - The API specification
