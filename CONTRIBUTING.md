## Contributing

Contributions are welcome!

```bash
cd OctoSync
dotnet build
dotnet run
```

Set the same config values in `appsettings.Development.json`.

### Local Playwright setup (Spotify source)

When running locally (without Docker), Playwright browser binaries must be installed once.

```bash
dotnet tool install --global Microsoft.Playwright.CLI
~/.dotnet/tools/playwright install chromium
```

### Docker

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```
