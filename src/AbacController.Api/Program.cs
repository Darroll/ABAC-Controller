using AbacController.Api.Configuration;
using AbacController.Api.Grpc;
using AbacController.Api.Hosting;
using AbacController.Api.Observability;
using AbacController.Api.Runtime;
using AbacController.Blazor.Components;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(8080, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });

    options.ListenAnyIP(8081, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("ABAC_");

var config = builder.Configuration.Get<AbacControllerOptions>() ?? new();

builder.Services.AddAbacControllerHost(builder.Configuration, builder.Environment, config);

var app = builder.Build();

await InitializeAsync(app);
RegisterLabelCodecs(app);

app.UseRateLimiter();
app.UseAbacControllerHost();
app.MapGrpcService<PdpGrpcService>();
app.MapGrpcService<PapGrpcService>();
app.MapGrpcService<PipGrpcService>();
app.MapGrpcService<PepGrpcService>();
app.MapGrpcService<SystemGrpcService>();
app.MapControllers();
app.MapGet("/metrics", (ApiMetrics metrics, AppRuntimeState runtimeState) =>
{
    var snapshot = new AppRuntimeStateSnapshot(runtimeState.StartupCompleted, runtimeState.StartupCompleted);
    return Results.Text(metrics.RenderPrometheus(snapshot), "text/plain; version=0.0.4");
}).AllowAnonymous();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
return;

static async Task InitializeAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AbacDbContext>();
    await db.Database.EnsureCreatedAsync();
    db.InitializeSqlite();

    var runtimeState = scope.ServiceProvider.GetRequiredService<AppRuntimeState>();
    runtimeState.MarkStartupCompleted();
}

static void RegisterLabelCodecs(WebApplication app)
{
    var codecRegistry = app.Services.GetRequiredService<ILabelCodecRegistry>();
    foreach (var codec in app.Services.GetServices<ILabelCodec>())
    {
        codecRegistry.Register(codec);
    }
}
