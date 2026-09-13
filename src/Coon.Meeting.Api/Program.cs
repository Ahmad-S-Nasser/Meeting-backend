using System.Text;
using System.Text.Json.Serialization;
using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Config;
using Coon.Meeting.Api.Hubs;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    // Integrators are external consumers of this API - "status": "cancelled" over the wire
    // beats "status": 1, and it stops the enum's underlying values from becoming an
    // accidental part of the contract.
    opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var databaseSettings = builder.Configuration.GetSection("Database").Get<DatabaseSettings>()
    ?? new DatabaseSettings();
builder.Services.AddSingleton(databaseSettings);
builder.Services.AddSingleton<LiteDbContext>();

builder.Services.AddSingleton<ITenantRepository, TenantRepository>();
builder.Services.AddSingleton<IMeetingRepository, MeetingRepository>();
builder.Services.AddSingleton<IApiKeyService, ApiKeyService>();
builder.Services.AddSingleton<IParticipantTokenService, ParticipantTokenService>();

// Capture startup errors so a missing secret returns a clean 500 instead of leaking a stack
// trace, matching SquadSpace's own Program.cs convention for the same class of failure.
string? startupError = null;
JwtSettings jwtSettings;
TurnSettings turnSettings;

try
{
    jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
        ?? throw new InvalidOperationException("Jwt settings not configured.");
    turnSettings = builder.Configuration.GetSection("Turn").Get<TurnSettings>()
        ?? throw new InvalidOperationException("Turn settings not configured.");

    // A hardcoded default participant-token signing key would let anyone mint a valid call
    // token for any meeting; failing to start is strictly better. The TURN shared secret is
    // the same story one layer down - without it call-credentials can't be minted at all, and
    // calling is core to this product, not an optional add-on the way it was in SquadSpace.
    RequireSecret(jwtSettings.ParticipantKey, "Jwt:ParticipantKey", "Jwt__ParticipantKey");
    RequireSecret(turnSettings.SharedSecret, "Turn:SharedSecret", "Turn__SharedSecret");

    static void RequireSecret(string value, string configKey, string envVar)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{configKey} is not configured. Set the {envVar} environment variable " +
                "(or use dotnet user-secrets in development). See docs\\DEPLOY.md.");
        }
    }
}
catch (Exception ex)
{
    startupError = "The server is not correctly configured and cannot handle requests.";
    Console.WriteLine($"Startup Configuration Error: {ex.Message}\n{ex.StackTrace}");
    jwtSettings = new JwtSettings();
    turnSettings = new TurnSettings();
}

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddSingleton(turnSettings);

builder.Services.AddAuthentication(ApiKeyAuthenticationSchemeOptions.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationSchemeOptions.SchemeName, _ => { })
    .AddJwtBearer(ParticipantTokenScheme.SchemeName, options =>
    {
        options.MapInboundClaims = false; // keep claim types exactly as minted: "sub", "tenantId", "meetingId", "name"
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrEmpty(jwtSettings.ParticipantKey) ? "startup-error-placeholder-key-not-used" : jwtSettings.ParticipantKey)),
        };
        options.Events = new JwtBearerEvents
        {
            // Browsers can't set headers on a WebSocket upgrade, so the SignalR JS client sends
            // the token as a query string parameter instead - read it back out here.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSignalR(options =>
{
    options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Coon.Meeting API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "sk_live_... / sk_test_...",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter 'Bearer' [space] and then your tenant API key.",
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer",
                },
            },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Coon.Meeting API v1"));

    // Dev-only convenience: seed two tenants with known raw keys so Phase (a) - CRUD +
    // cross-tenant isolation - can be exercised immediately via curl/Postman/Swagger without
    // a provisioning endpoint (that's Phase (e), deliberately not built yet).
    await DevSeed.SeedAsync(app.Services);
}

// Startup Error Middleware - returns 500 for every request if a required secret was missing.
// The underlying exception (connection strings, signing keys) must never reach a response body.
app.Use(async (context, next) =>
{
    if (!string.IsNullOrEmpty(startupError))
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsync(startupError);
        return;
    }

    await next();
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<MeetingCallHub>("/hubs/meetingCall");

app.Run();

static class DevSeed
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var tenants = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
        var apiKeys = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var db = scope.ServiceProvider.GetRequiredService<LiteDbContext>();

        if (db.Tenants.Count() > 0) return;

        foreach (var name in new[] { "Dev Tenant A", "Dev Tenant B" })
        {
            var rawKey = apiKeys.GenerateKey(live: false);
            var tenant = new Tenant
            {
                Name = name,
                ApiKeyHash = apiKeys.Hash(rawKey),
                ApiKeyPrefix = apiKeys.Prefix(rawKey),
                AllowedOrigins = new List<string> { "http://localhost:5173" },
            };
            await tenants.CreateAsync(tenant);
            Console.WriteLine($"[dev-seed] {name} (id={tenant.Id}) API key: {rawKey}");
        }
    }
}
