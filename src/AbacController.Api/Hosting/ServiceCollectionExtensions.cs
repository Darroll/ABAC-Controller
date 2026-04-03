using AbacController.Api.Auth;
using AbacController.Api.Configuration;
using AbacController.Api.Health;
using AbacController.Api.Observability;
using AbacController.Api.Runtime;
using AbacController.Audit;
using AbacController.Core.Constants;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using AbacController.Pap;
using AbacController.Pdp;
using AbacController.Pep;
using AbacController.Pep.Codecs;
using AbacController.Pep.Codecs.Xml;
using AbacController.Pip;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

namespace AbacController.Api.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAbacControllerHost(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        AbacControllerOptions options)
    {
        services.AddMemoryCache();
        services.AddSingleton(options);
        services.AddSingleton<AppRuntimeState>();
        services.AddSingleton<ApiMetrics>();

        services.AddDbContext<AbacDbContext>(dbOptions =>
        {
            var connStr = options.Database.ConnectionString ?? "Data Source=abac-controller.db";
            dbOptions.UseSqlite(connStr);
        });

        services.AddSingleton<IXmlSignatureVerifier, RejectingXmlSignatureVerifier>();
        services.AddSingleton<ISpifParser, SpifParser>();
        services.AddSingleton<IAcdfEvaluator, AcdfEvaluator>();
        services.AddSingleton<IDecisionCache, DecisionCache>();
        services.AddSingleton<ILabelCodecRegistry, LabelCodecRegistry>();
        services.AddSingleton<IMarkingGenerator, MarkingGenerator>();
        services.AddSingleton<ISpifRegistry, SpifRegistry>();
        services.AddSingleton<IPipResolver, PipResolver>();
        services.AddSingleton<IPipCacheManager, PipCacheManager>();
        services.AddSingleton<LabelValidator>();
        services.AddSingleton<IStanag4778MetadataBinder, Stanag4778MetadataBinder>();
        services.AddSingleton<IAuditWriter, AuditWriter>();
        services.AddHostedService<AuditBatchWriterService>();
        services.AddSingleton<ILabelCodec, XmlStanag4774Codec>();
        services.AddScoped<IPdpEngine, PdpEngine>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IAuditReader, AuditRepository>();

        services.AddGrpc().AddJsonTranscoding();
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddRazorComponents().AddInteractiveServerComponents();

        services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("startup", tags: ["startup"])
            .AddCheck<ReadinessHealthCheck>("readiness", tags: ["ready"]);

        services.AddAuthorizationCore();
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        ConfigureAuthentication(services, environment, options);
        ConfigureAuthorization(services);
        ConfigureRateLimiting(services, options);

        return services;
    }

    public static void UseAbacControllerHost(this WebApplication app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        app.MapHealthChecks("/health/startup", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("startup")
        });
    }

    private static void ConfigureAuthentication(
        IServiceCollection services,
        IWebHostEnvironment environment,
        AbacControllerOptions options)
    {
        var hasAuthority = !string.IsNullOrWhiteSpace(options.Auth.Authority);
        var allowDevelopmentAuth = environment.IsDevelopment() && options.Auth.EnableDevelopmentAuth;

        if (!hasAuthority && !allowDevelopmentAuth)
        {
            throw new InvalidOperationException(
                "Authentication is not configured. Set Auth:Authority for JWT bearer validation or explicitly enable Auth:EnableDevelopmentAuth in Development only.");
        }

        if (hasAuthority)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.Authority = options.Auth.Authority;
                    jwtOptions.Audience = options.Auth.Audience;
                    jwtOptions.RequireHttpsMetadata = options.Auth.RequireHttpsMetadata;
                });

            return;
        }

        services.AddAuthentication(DevelopmentAuthenticationDefaults.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentAuthHandler>(
                DevelopmentAuthenticationDefaults.SchemeName,
                _ => { });
    }

    private static void ConfigureAuthorization(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            AddScopePolicy(options, "Evaluate", Scopes.Evaluate);
            AddScopePolicy(options, "EvaluateExplain", Scopes.EvaluateExplain);
            AddScopePolicy(options, "PolicyRead", Scopes.PolicyRead);
            AddScopePolicy(options, "PolicyWrite", Scopes.PolicyWrite);
            AddScopePolicy(options, "PolicyAdmin", Scopes.PolicyAdmin);
            AddScopePolicy(options, "PipRead", Scopes.PipRead);
            AddScopePolicy(options, "PipAdmin", Scopes.PipAdmin);
            AddScopePolicy(options, "PepRead", Scopes.PepRead);
            AddScopePolicy(options, "PepAdmin", Scopes.PepAdmin);
            AddScopePolicy(options, "PepLabel", Scopes.PepLabel);
            AddScopePolicy(options, "AuditRead", Scopes.AuditRead);
            AddScopePolicy(options, "SysRead", Scopes.SysRead);
            AddScopePolicy(options, "SysAdmin", Scopes.SysAdmin);
        });
    }

    private static void ConfigureRateLimiting(IServiceCollection services, AbacControllerOptions options)
    {
        services.AddRateLimiter(rateLimiterOptions =>
        {
            rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiterOptions.AddFixedWindowLimiter("pdp", limiterOptions =>
            {
                limiterOptions.PermitLimit = options.RateLimiting.PdpPermitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(options.RateLimiting.WindowSeconds);
                limiterOptions.QueueLimit = 0;
            });
        });
    }

    private static void AddScopePolicy(AuthorizationOptions options, string policyName, string scope)
    {
        options.AddPolicy(policyName, policy => policy.RequireScope(scope));
    }
}
