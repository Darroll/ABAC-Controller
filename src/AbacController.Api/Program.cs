using AbacController.Api.Configuration;
using AbacController.Api.Grpc;
using AbacController.Api.Hosting;
using AbacController.Api.Observability;
using AbacController.Api.Runtime;
using AbacController.Blazor.Components;
using AbacController.Core.Domain.Classifications;
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

// Structured JSON logging when not in development
if (!builder.Environment.IsDevelopment())
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}

var config = builder.Configuration.Get<AbacControllerOptions>() ?? new();

// Fail fast with clear messages on invalid configuration
var configErrors = AbacController.Api.Configuration.ConfigurationValidator.Validate(config, builder.Environment.IsDevelopment());
if (configErrors.Count > 0)
{
    foreach (var error in configErrors)
    {
        Console.Error.WriteLine($"[FATAL] Configuration error: {error}");
    }
    throw new InvalidOperationException(
        $"ABAC Controller configuration is invalid ({configErrors.Count} error(s)). See log output above.");
}

builder.Services.AddAbacControllerHost(builder.Configuration, builder.Environment, config);

var app = builder.Build();

await InitializeAsync(app);
RegisterLabelCodecs(app);

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ABAC Controller API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "ABAC Controller API";
});
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

    await BootstrapEmailClassificationAsync(scope, app);

    var runtimeState = scope.ServiceProvider.GetRequiredService<AppRuntimeState>();
    runtimeState.MarkStartupCompleted();
}

static async Task BootstrapEmailClassificationAsync(IServiceScope scope, WebApplication app)
{
    var bootstrap = app.Configuration
        .GetSection("EmailClassificationBootstrap")
        .Get<EmailClassificationBootstrapOptions>();

    if (bootstrap is null || !bootstrap.Enabled) return;

    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EmailClassificationBootstrap");
    var repository = scope.ServiceProvider.GetRequiredService<IApplicationRepository>();

    var existing = await repository.GetByIdAsync(bootstrap.ApplicationId);
    var registration = new ApplicationRegistration
    {
        Id = bootstrap.ApplicationId,
        Name = bootstrap.ApplicationName,
        Description = bootstrap.ApplicationDescription,
        DefaultPolicyOid = string.IsNullOrWhiteSpace(bootstrap.DefaultPolicyOid) ? null : bootstrap.DefaultPolicyOid,
        AllowedClassificationLacvs = bootstrap.AllowedClassificationLacvs,
        MaxClassificationHierarchy = bootstrap.MaxClassificationHierarchy,
        AllowedTagSetOids = bootstrap.AllowedTagSetOids,
        IsActive = true
    };

    await repository.UpsertAsync(registration);
    logger.LogInformation(
        "EmailClassificationBootstrap: {Action} application '{Id}' (defaultPolicyOid={Oid})",
        existing is null ? "seeded" : "updated",
        bootstrap.ApplicationId,
        bootstrap.DefaultPolicyOid ?? "(none)");
}

static void RegisterLabelCodecs(WebApplication app)
{
    var codecRegistry = app.Services.GetRequiredService<ILabelCodecRegistry>();
    foreach (var codec in app.Services.GetServices<ILabelCodec>())
    {
        codecRegistry.Register(codec);
    }
}
