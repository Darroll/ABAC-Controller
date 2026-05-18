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
using AbacController.Pap.Webhooks;
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
        services.AddScoped<KeycloakClaimsContext>();
        services.AddScoped<IKeycloakClaimsContext>(sp => sp.GetRequiredService<KeycloakClaimsContext>());
        services.AddScoped<IKeycloakGroupClaimsProvider>(sp => sp.GetRequiredService<KeycloakClaimsContext>());
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
        services.AddScoped<IApplicationRepository, ApplicationRepository>();
        services.AddScoped<IEntitlementRepository, EntitlementRepository>();
        services.AddScoped<IEntitlementResolver, EntitlementResolver>();
        services.AddScoped<IAbacGroupRepository, AbacGroupRepository>();
        services.AddSingleton<IGroupMembershipCache, GroupMembershipCache>();
        services.AddScoped<IGroupMembershipResolver, GroupMembershipResolver>();
        services.AddScoped<IClassificationQueryEngine, ClassificationQueryEngine>();
        services.AddScoped<BundledSpifImporter>();

        // Webhook publisher + dispatcher
        services.AddScoped<IWebhookSubscriptionRepository, WebhookRepository>();
        services.AddSingleton<WebhookDispatcherSignal>();
        services.AddSingleton<IWebhookPublisher, WebhookPublisher>();
        services.AddHostedService<WebhookDispatcherHostedService>();
        services.AddHttpClient("AbacWebhookDispatcher", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });

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

        // The embedded Blazor admin pages call relative URIs like
        // "pap/api/spifs". Razor pages don't get a configured HttpClient
        // without an explicit registration. Three requirements:
        //
        //   1. BaseAddress must be the in-process Kestrel listener so the
        //      client is independent of any host-side port mapping.
        //   2. Authorization must forward the bearer / API key from the
        //      caller's request so the PAP call runs with the admin's
        //      scopes, not as an anonymous loopback.
        //   3. X-Tenant-Id must come from the current request, not a
        //      hard-coded literal — otherwise admins viewing tenant B
        //      through the shell get data from tenant A.
        services.AddHttpContextAccessor();
        services.AddTransient<BlazorAdminAuthForwardingHandler>();
        services.AddHttpClient("BlazorAdmin", client =>
            {
                client.BaseAddress = new Uri("http://localhost:8080/");
            })
            .AddHttpMessageHandler<BlazorAdminAuthForwardingHandler>();
        services.AddScoped(sp =>
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("BlazorAdmin"));

        services.AddHealthChecks()
            .AddCheck<StartupHealthCheck>("startup", tags: ["startup"])
            .AddCheck<ReadinessHealthCheck>("readiness", tags: ["ready"]);

        services.AddAuthorizationCore();
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        ConfigureAuthentication(services, environment, options);
        ConfigureAuthorization(services, environment, options);
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
        app.UseMiddleware<Middleware.KeycloakClaimsMiddleware>();
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
        var hasKeycloakConfig = options.Auth.Keycloak.IsEnabled;
        var allowDevelopmentAuth = environment.IsDevelopment() && options.Auth.EnableDevelopmentAuth;

        if (!hasAuthority && !hasApiKeys && !allowDevelopmentAuth && !hasKeycloakConfig)
        {
            throw new InvalidOperationException(
                "Authentication is not configured. Set Auth:Authority for JWT bearer, Auth:Keycloak:Authority for the Keycloak issuer, configure Auth:ApiKeys for API key auth, or explicitly enable Auth:EnableDevelopmentAuth in Development only.");
        }

        var hasKeycloak = options.Auth.Keycloak.IsEnabled;

        if (hasAuthority || hasKeycloak)
        {
            // Default to whichever issuer is configured first; ASP.NET Core
            // will fall through to the second scheme automatically when an
            // [Authorize] policy lists both via AddAuthenticationSchemes.
            var defaultScheme = hasAuthority
                ? JwtBearerDefaults.AuthenticationScheme
                : "Keycloak";

            var authBuilder = services.AddAuthentication(defaultScheme);

            if (hasAuthority)
            {
                authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwtOptions =>
                {
                    jwtOptions.Authority = options.Auth.Authority;
                    jwtOptions.Audience = options.Auth.Audience;
                    jwtOptions.RequireHttpsMetadata = options.Auth.RequireHttpsMetadata;
                });
            }

            if (hasKeycloak)
            {
                authBuilder.AddJwtBearer("Keycloak", jwtOptions =>
                {
                    jwtOptions.Authority = options.Auth.Keycloak.Authority;
                    jwtOptions.Audience = options.Auth.Keycloak.Audience;
                    jwtOptions.RequireHttpsMetadata = options.Auth.Keycloak.RequireHttpsMetadata;
                });
            }

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

    private static void ConfigureAuthorization(
        IServiceCollection services,
        IWebHostEnvironment environment,
        AbacControllerOptions options)
    {
        // Compute the actual list of authentication scheme names that will be
        // registered, mirroring the logic in ConfigureAuthentication. We
        // attach exactly these schemes to every authorization policy so that
        // a request authenticated by ANY of them satisfies the policy, and so
        // we never list a scheme that doesn't exist (which would cause a 500
        // at evaluation time with "No authentication handler is registered").
        var schemes = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Auth.Authority))
            schemes.Add(JwtBearerDefaults.AuthenticationScheme);
        if (options.Auth.Keycloak.IsEnabled)
            schemes.Add("Keycloak");
        if (options.Auth.ApiKeys.Count > 0)
            schemes.Add(ApiKeyAuthenticationDefaults.SchemeName);
        if (environment.IsDevelopment() && options.Auth.EnableDevelopmentAuth)
            schemes.Add(DevelopmentAuthenticationDefaults.SchemeName);

        var schemesArray = schemes.ToArray();

        services.AddAuthorization(authOptions =>
        {
            AddScopePolicy(authOptions, "Evaluate", Scopes.Evaluate, schemesArray);
            AddScopePolicy(authOptions, "EvaluateExplain", Scopes.EvaluateExplain, schemesArray);
            AddScopePolicy(authOptions, "PolicyRead", Scopes.PolicyRead, schemesArray);
            AddScopePolicy(authOptions, "PolicyWrite", Scopes.PolicyWrite, schemesArray);
            AddScopePolicy(authOptions, "PolicyAdmin", Scopes.PolicyAdmin, schemesArray);
            AddScopePolicy(authOptions, "PipRead", Scopes.PipRead, schemesArray);
            AddScopePolicy(authOptions, "PipAdmin", Scopes.PipAdmin, schemesArray);
            AddScopePolicy(authOptions, "PepRead", Scopes.PepRead, schemesArray);
            AddScopePolicy(authOptions, "PepAdmin", Scopes.PepAdmin, schemesArray);
            AddScopePolicy(authOptions, "PepLabel", Scopes.PepLabel, schemesArray);
            AddScopePolicy(authOptions, "AuditRead", Scopes.AuditRead, schemesArray);
            AddScopePolicy(authOptions, "SysRead", Scopes.SysRead, schemesArray);
            AddScopePolicy(authOptions, "SysAdmin", Scopes.SysAdmin, schemesArray);
            AddScopePolicy(authOptions, "ClassificationQuery", Scopes.ClassificationQuery, schemesArray);
            AddScopePolicy(authOptions, "ApplicationAdmin", Scopes.ApplicationAdmin, schemesArray);
            AddScopePolicy(authOptions, "EntitlementAdmin", Scopes.EntitlementAdmin, schemesArray);
            AddScopePolicy(authOptions, "RecipientCheck", Scopes.RecipientCheck, schemesArray);
            AddScopePolicy(authOptions, "WebhookAdmin", Scopes.WebhookAdmin, schemesArray);
            AddScopePolicy(authOptions, "AuditMirrorWrite", Scopes.AuditMirrorWrite, schemesArray);
            AddScopePolicy(authOptions, "GroupAdmin", Scopes.GroupAdmin, schemesArray);
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

    private static void AddScopePolicy(AuthorizationOptions options, string policyName, string scope, string[] schemes)
    {
        options.AddPolicy(policyName, policy =>
        {
            // Only attach schemes that ConfigureAuthentication actually
            // registered. Listing a scheme that doesn't exist would cause
            // AuthorizationMiddleware to throw "No authentication handler is
            // registered for the scheme '<name>'" at request time.
            if (schemes.Length > 0)
            {
                policy.AddAuthenticationSchemes(schemes);
            }
            policy.RequireScope(scope);
        });
    }
}
