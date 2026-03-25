using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Services;
using OctoSync.Core.Sources;
using OctoSync.Core.Targets;

using Serilog;
using Serilog.Events;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog(config => config
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} {Level:u3} | {Message:lj}{NewLine}{Exception}")
);

builder.Services.Configure<CsvOptions>(builder.Configuration.GetSection("Sources:Csv"));
builder.Services.Configure<TidalOptions>(builder.Configuration.GetSection("Sources:Tidal"));
builder.Services.Configure<DeezerOptions>(builder.Configuration.GetSection("Sources:Deezer"));
builder.Services.Configure<ListenBrainzOptions>(builder.Configuration.GetSection("Sources:ListenBrainz"));
builder.Services.Configure<SpotifyOptions>(builder.Configuration.GetSection("Sources:Spotify"));
builder.Services.Configure<SubsonicOptions>(builder.Configuration.GetSection("Subsonic"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("SyncSettings"));

builder.Services.AddTransient<IPlaylistSource, CsvSource>();
builder.Services.AddHttpClient<IPlaylistSource, TidalSource>();
builder.Services.AddHttpClient<IPlaylistSource, DeezerSource>();
builder.Services.AddHttpClient<IPlaylistSource, ListenBrainzSource>();
builder.Services.AddHttpClient<IPlaylistSource, SpotifySource>();

builder.Services.AddHttpClient<IPlaylistTarget, SubsonicTarget>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddSingleton<ISyncStateManager, SyncStateManager>();

builder.Services.AddTransient<ITrackResolver, TrackResolver>();
builder.Services.AddTransient<IPlaylistSyncEngine, PlaylistSyncEngine>();
builder.Services.AddTransient<ISyncOrchestrator, SyncOrchestrator>();

builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
host.Run();