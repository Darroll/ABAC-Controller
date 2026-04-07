using AbacController.Api.Auth;
using AbacController.Api.Middleware;
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
using Microsoft.OpenApi.Models;
using System.Threading.RateLimiting;

namespace AbacController.Api.Hosting;

/// <summary>
/// Extension methods for configuring all ABAC Controller host services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all ABAC Controller services, persistence, authentication, and middleware.
    /// </summary>
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
        services.AddScoped<HttpTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
        services.AddSingleton<TenantSpifRegistryStore>();
        services.AddScoped<ISpifRegistry, TenantSpifRegistry>();
        services.AddScoped<IPipSourceCatalog, DatabasePipSourceCatalog>();
        services.AddScoped<IPipResolver, PipResolver>();
        services.AddSingleton<IPipCacheManager, PipCacheManager>();
        services.AddScoped<PipHealthMonitor>();
        services.AddSingleton<LabelValidator>();
        services.AddSingleton<IStanag4778MetadataBinder, Stanag4778MetadataBinder>();
        services.AddSingleton<BindingDataHeaderCodec>();
        services.AddSingleton<AuditWriter>();
        services.AddSingleton<IAuditWriter>(sp => sp.GetRequiredService<AuditWriter>());
        services.AddSingleton<IAuditChannelReader>(sp => sp.GetRequiredService<AuditWriter>());
        services.AddHostedService<AuditBatchWriterService>();
        services.AddSingleton<ILabelCodec, XmlStanag4774Codec>();
        services.AddScoped<IPdpEngine, PdpEngine>();
        services.AddScoped<IPolicyRepository, PolicyRepository>();
        services.AddScoped<IAuditReader, AuditRepository>();

        // Async evaluation support
        services.AddSingleton<AsyncEvaluationQueue>();
        services.AddHostedService<AsyncEvaluationService>();
        services.AddHttpClient("AsyncEvalCallback", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddGrpc().AddJsonTranscoding();
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ABAC Controller API",
                Version = "v1",
                Description = "Feature-complete ABAC/PDP/PAP/PIP/PEP controller with AuthZEN and XACML JSON support."
            });

            swagger.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT bearer token. Format: Bearer {token}"
            });

            swagger.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Name = ApiKeyAuthenticationDefaults.HeaderName,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "Service-to-service API key"
            });

            swagger.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    []
                },
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
                    },
                    []
                }
            });

            swagger.CustomSchemaIds(static type => type.FullName?.Replace('+', '.') ?? type.Name);
        });
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

    /// <summary>
    /// Configures the ABAC Controller middleware pipeline including auth, health checks, and routing.
    /// </summary>
    public static void UseAbacControllerHost(this WebApplication app)
    {
        app.UseMiddleware<Middleware.CorrelationIdMiddleware>();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseMiddleware<TenantContextMiddleware>();
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
        var hasApiKeys = options.Auth.ApiKeys.Count > 0;
        var allowDevelopmentAuth = environment.IsDevelopment() && options.Auth.EnableDevelopmentAuth;

        if (!hasAuthority && !hasApiKeys && !allowDevelopmentAuth)
        {
            throw new InvalidOperationException(
                "Authentication is not configured. Set Auth:Authority for JWT bearer, configure Auth:ApiKeys for API key auth, or explicitly enable Auth:EnableDevelopmentAuth in Development only.");
        }

        if (hasAuthority)
        {
            var authBuilder = services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(jwtOptions =>
                {
                    jwtOptions.Authority = options.Auth.Authority;
                    jwtOptions.Audience = options.Auth.Audience;
                    jwtOptions.RequireHttpsMetadata = options.Auth.RequireHttpsMetadata;
                });

            if (hasApiKeys)
            {
                authBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                    ApiKeyAuthenticationDefaults.SchemeName, _ => { });
            }

            return;
        }

        if (hasApiKeys && !allowDevelopmentAuth)
        {
            services.AddAuthentication(ApiKeyAuthenticationDefaults.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                    ApiKeyAuthenticationDefaults.SchemeName, _ => { });
            return;
        }

        var devBuilder = services.AddAuthentication(DevelopmentAuthenticationDefaults.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DevelopmentAuthHandler>(
                DevelopmentAuthenticationDefaults.SchemeName,
                _ => { });

        if (hasApiKeys)
        {
            devBuilder.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                ApiKeyAuthenticationDefaults.SchemeName, _ => { });
        }
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

            // Global rate limit for all PDP evaluation traffic
            rateLimiterOptions.AddFixedWindowLimiter("pdp", limiterOptions =>
            {
                limiterOptions.PermitLimit = options.RateLimiting.PdpPermitLimit;
                limiterOptions.Window = TimeSpan.FromSeconds(options.RateLimiting.WindowSeconds);
                limiterOptions.QueueLimit = 0;
            });

            // Per-client rate limit (partitioned by client identity)
            if (options.RateLimiting.PerClientPermitLimit > 0)
            {
                rateLimiterOptions.AddPolicy("pdp-per-client", httpContext =>
                {
                    var clientId = httpContext.User.FindFirst("client_id")?.Value
                                   ?? httpContext.User.FindFirst("sub")?.Value
                                   ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                   ?? "anonymous";

                    return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RateLimiting.PerClientPermitLimit,
                        Window = TimeSpan.FromSeconds(options.RateLimiting.PerClientWindowSeconds),
                        QueueLimit = 0
                    });
                });
            }

            // Per-resource rate limit (partitioned by resource type:id from request body)
            if (options.RateLimiting.PerResourcePermitLimit > 0)
            {
                rateLimiterOptions.AddPolicy("pdp-per-resource", httpContext =>
                {
                    // Resource partitioning uses a header set by the evaluation endpoints
                    var resourceKey = httpContext.Request.Headers["X-ABAC-Resource-Key"].FirstOrDefault() ?? "default";

                    return RateLimitPartition.GetFixedWindowLimiter(resourceKey, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RateLimiting.PerResourcePermitLimit,
                        Window = TimeSpan.FromSeconds(options.RateLimiting.PerResourceWindowSeconds),
                        QueueLimit = 0
                    });
                });
            }
        });
    }

    private static void AddScopePolicy(AuthorizationOptions options, string policyName, string scope)
    {
        options.AddPolicy(policyName, policy => policy.RequireScope(scope));
    }
}
