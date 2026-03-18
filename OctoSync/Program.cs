using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Services;
using OctoSync.Core.Sources;
using OctoSync.Core.Targets;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Bind configuration
builder.Services.Configure<TidalOptions>(builder.Configuration.GetSection("Sources:Tidal"));

// Register HttpClient specifically for TidalSource
builder.Services.AddHttpClient<IPlaylistSource, TidalSource>();

builder.Services.Configure<SubsonicOptions>(builder.Configuration.GetSection("Subsonic"));
builder.Services.AddHttpClient<IPlaylistTarget, SubsonicTarget>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("SyncSettings"));
builder.Services.AddHostedService<SyncWorker>();

// Additional services can be registered here later.
// builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
host.Run();