using AbacController.Api.Configuration;
using AbacController.Audit;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;
using AbacController.Pep.Codecs;
using AbacController.Pep.Codecs.Xml;
using AbacController.Pip;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("ABAC_");

var config = builder.Configuration.Get<AbacControllerOptions>() ?? new();

// ── Data Layer ──
builder.Services.AddDbContext<AbacDbContext>(options =>
{
    var connStr = config.Database.ConnectionString
        ?? "Data Source=abac-controller.db";
    options.UseSqlite(connStr);
});

// ── Core Services (Singleton) ──
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISpifParser, SpifParser>();
builder.Services.AddSingleton<IAcdfEvaluator, AcdfEvaluator>();
builder.Services.AddSingleton<IDecisionCache, DecisionCache>();
builder.Services.AddSingleton<ILabelCodecRegistry, LabelCodecRegistry>();
builder.Services.AddSingleton<IMarkingGenerator, MarkingGenerator>();
builder.Services.AddSingleton<ISpifRegistry, SpifRegistry>();

// ── PIP (Singleton) ──
builder.Services.AddSingleton<IPipResolver, PipResolver>();
builder.Services.AddSingleton<IPipCacheManager, PipCacheManager>();

// ── PEP (Singleton) ──
builder.Services.AddSingleton<LabelValidator>();

// ── Audit (Singleton) ──
builder.Services.AddSingleton<IAuditWriter, AuditWriter>();
builder.Services.AddHostedService<AuditBatchWriterService>();

// ── Label Codecs ──
builder.Services.AddSingleton<ILabelCodec, XmlStanag4774Codec>();

// ── Scoped Services ──
builder.Services.AddScoped<IPdpEngine, PdpEngine>();
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
builder.Services.AddScoped<IAuditReader, AuditRepository>();

// ── Controllers ──
builder.Services.AddControllers();

// ── Health Checks ──
builder.Services.AddHealthChecks();

// ── OpenAPI ──
builder.Services.AddEndpointsApiExplorer();

// ── Auth (conditional — skip in development without IdP) ──
if (!string.IsNullOrEmpty(config.Auth.Authority))
{
    builder.Services.AddAuthentication("Bearer")
        .AddJwtBearer(options =>
        {
            options.Authority = config.Auth.Authority;
            options.Audience = config.Auth.Audience;
            options.RequireHttpsMetadata = config.Auth.RequireHttpsMetadata;
        });
}
else
{
    // Development mode: no auth
    builder.Services.AddAuthentication("Development")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
            DevelopmentAuthHandler>("Development", _ => { });
}

builder.Services.AddAuthorization(options =>
{
    // In development mode, all policies are permissive
    options.AddPolicy("Evaluate", p => p.RequireAuthenticatedUser());
    options.AddPolicy("EvaluateExplain", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PolicyRead", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PolicyWrite", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PolicyAdmin", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PipRead", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PipAdmin", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PepRead", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PepAdmin", p => p.RequireAuthenticatedUser());
    options.AddPolicy("PepLabel", p => p.RequireAuthenticatedUser());
    options.AddPolicy("AuditRead", p => p.RequireAuthenticatedUser());
    options.AddPolicy("SysRead", p => p.RequireAuthenticatedUser());
    options.AddPolicy("SysAdmin", p => p.RequireAuthenticatedUser());
});

var app = builder.Build();

// ── Initialize Database ──
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AbacDbContext>();
    await db.Database.EnsureCreatedAsync();
    db.InitializeSqlite();
}

// ── Register Label Codecs ──
var codecRegistry = app.Services.GetRequiredService<ILabelCodecRegistry>();
foreach (var codec in app.Services.GetServices<ILabelCodec>())
{
    codecRegistry.Register(codec);
}

// ── Middleware Pipeline ──
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// ── Map Endpoints ──
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapControllers();

await app.RunAsync();

/// <summary>
/// Development authentication handler — auto-authenticates all requests.
/// </summary>
public class DevelopmentAuthHandler :
    Microsoft.AspNetCore.Authentication.AuthenticationHandler<
        Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions>
{
    public DevelopmentAuthHandler(
        Microsoft.Extensions.Options.IOptionsMonitor<
            Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = new System.Security.Claims.ClaimsIdentity("Development");
        identity.AddClaim(new System.Security.Claims.Claim("sub", "dev-user"));
        identity.AddClaim(new System.Security.Claims.Claim("scope", "abac:evaluate"));
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, "Development");
        return Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(ticket));
    }
}
