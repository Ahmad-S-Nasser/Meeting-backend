using System.Text.Json.Serialization;
using Coon.Meeting.Api.Auth;
using Coon.Meeting.Api.Config;
using Coon.Meeting.Api.Models;
using Coon.Meeting.Api.Repositories;
using Coon.Meeting.Api.Services;

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

builder.Services.AddAuthentication(ApiKeyAuthenticationSchemeOptions.SchemeName)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationSchemeOptions.SchemeName, _ => { });

builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

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
