using OctoSync.Core.Configuration;
using OctoSync.Core.Interfaces;
using OctoSync.Core.Services;
using OctoSync.Core.Sources;
using OctoSync.Core.Targets;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.Configure<TidalOptions>(builder.Configuration.GetSection("Sources:Tidal"));
builder.Services.Configure<ListenBrainzOptions>(builder.Configuration.GetSection("Sources:ListenBrainz"));

builder.Services.AddHttpClient<IPlaylistSource, TidalSource>();
builder.Services.AddHttpClient<IPlaylistSource, ListenBrainzSource>();

builder.Services.Configure<SubsonicOptions>(builder.Configuration.GetSection("Subsonic"));
builder.Services.AddHttpClient<IPlaylistTarget, SubsonicTarget>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("SyncSettings"));
builder.Services.AddHostedService<SyncWorker>();

var host = builder.Build();
host.Run();