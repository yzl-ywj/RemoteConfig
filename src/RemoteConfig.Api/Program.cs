using System.Text.Json;
using RemoteConfig.Api.Controllers;
using RemoteConfig.Core.Interfaces;
using RemoteConfig.Infrastructure.Configuration;
using RemoteConfig.Infrastructure.Messaging;
using RemoteConfig.Infrastructure.Persistence;
using RemoteConfig.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ── Azure AD Authentication ─────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Config.Read", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("Config.Read"))));

    options.AddPolicy("Config.Write", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("Config.Write"))));

    options.AddPolicy("Command.Read", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("Command.Read"))));

    options.AddPolicy("Command.Execute", policy =>
        policy.RequireAssertion(context =>
            context.User.HasClaim(c => c.Type == "scope" && c.Value.Contains("Command.Execute"))));
});

// ── Application Insights ───────────────────────────────
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
});

// ── Configuration Options ───────────────────────────────
builder.Services.Configure<DatabaseOptions>(
    configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<RemoteConfigOptions>(
    configuration.GetSection(RemoteConfigOptions.SectionName));
builder.Services.Configure<RedisOptions>(
    configuration.GetSection(RedisOptions.SectionName));
builder.Services.Configure<ServiceBusOptions>(
    configuration.GetSection(ServiceBusOptions.SectionName));
builder.Services.Configure<IoTHubOptions>(
    configuration.GetSection(IoTHubOptions.SectionName));

// ── Database ────────────────────────────────────────────
builder.Services.AddSingleton<System.Data.IDbConnection>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
    var conn = new Microsoft.Data.SqlClient.SqlConnection(opts.ConnectionString);
    return conn;
});

// ── Core Services (DI) ──────────────────────────────────
builder.Services.AddScoped<IConfigRepository, SqlConfigRepository>();
builder.Services.AddScoped<ICommandStore, SqlCommandStore>();
builder.Services.AddScoped<IAuditLogger, SqlAuditLogger>();

// Choose dispatcher based on config
var dispatchMode = configuration["RemoteConfig:DispatchMode"] ?? "ServiceBus";
if (dispatchMode.Equals("IoTHub", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ICommandDispatcher, IoTHubCommandDispatcher>();
}
else
{
    builder.Services.AddSingleton<ICommandDispatcher, ServiceBusCommandDispatcher>();
}

builder.Services.AddScoped<IRemoteConfigService, RemoteConfigService>();
builder.Services.AddScoped<ICommandService, CommandService>();
builder.Services.AddScoped<IConfigRollback, ConfigRollbackEngine>();

// ── Redis (lazy singleton) ──────────────────────────────
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var redisOpts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RedisOptions>>().Value;
    return StackExchange.Redis.ConnectionMultiplexer.Connect(redisOpts.ConnectionString);
});

// ── Background Services ─────────────────────────────────
builder.Services.AddHostedService<CommandResultProcessor>();
builder.Services.AddHostedService<CommandTimeoutWatcher>();

// ── Controllers & JSON ──────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// ── Swagger / OpenAPI ──────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "IoT Remote Config API",
        Version = "v1",
        Description = "Remote configuration and command execution for Tietoevry IoT Platform"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.OAuth2,
        Flows = new Microsoft.OpenApi.Models.OpenApiOAuthFlows
        {
            AuthorizationCode = new Microsoft.OpenApi.Models.OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{configuration["AzureAd:Instance"]}{configuration["AzureAd:TenantId"]}/oauth2/v2.0/authorize"),
                TokenUrl = new Uri($"{configuration["AzureAd:Instance"]}{configuration["AzureAd:TenantId"]}/oauth2/v2.0/token"),
                Scopes = new Dictionary<string, string>
                {
                    { "api://{{AZURE_AD_APP_ID}}/Config.Read", "Read configs" },
                    { "api://{{AZURE_AD_APP_ID}}/Config.Write", "Write configs" },
                    { "api://{{AZURE_AD_APP_ID}}/Command.Read", "Read commands" },
                    { "api://{{AZURE_AD_APP_ID}}/Command.Execute", "Execute commands" }
                }
            }
        }
    });
});

// ── Health Checks ───────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

// ── CORS ───────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "https://portal.iot.{{DOMAIN}}",
                "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── Rate Limiting ───────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<AspNetCoreRateLimit.IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<AspNetCoreRateLimit.IRateLimitCounterStore, AspNetCoreRateLimit.MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<AspNetCoreRateLimit.IClientPolicyStore, AspNetCoreRateLimit.MemoryCacheClientPolicyStore>();
builder.Services.AddSingleton<AspNetCoreRateLimit.IProcessingStrategy, AspNetCoreRateLimit.AsyncKeyLockProcessingStrategy>();

// ── Build & Middleware ──────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "IoT Remote Config API v1");
        options.OAuthClientId(configuration["AzureAd:ClientId"]);
        options.OAuthAppName("IoT Remote Config API");
    });
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
