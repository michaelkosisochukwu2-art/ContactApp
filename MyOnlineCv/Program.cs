using MyOnlineCv.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────────────────────
// CORS — allow all origins (Swagger UI + any frontend)
// ─────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// ─────────────────────────────────────────────────────────────
// Forward headers — required for Render's / Docker's reverse proxy
// (safe to include locally; has no effect when no proxy is present)
// ─────────────────────────────────────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─────────────────────────────────────────────────────────────
// DbContext — supports both environments:
//   LOCAL:      appsettings.Development.json → SQL Server
//   RENDER/DOCKER: DATABASE_URL env var      → PostgreSQL
//
// Render automatically sets DATABASE_URL as a postgres:// URI.
// Locally, appsettings.Development.json provides the SQL Server
// connection string via ConnectionStrings:DefaultConnection.
// ─────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Render / Docker sets DATABASE_URL automatically for linked PostgreSQL databases
    var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

    // Fall back to appsettings (used locally with SQL Server)
    var rawConnection = !string.IsNullOrEmpty(databaseUrl)
        ? databaseUrl
        : builder.Configuration.GetConnectionString("DefaultConnection");

    var isPostgres = !string.IsNullOrEmpty(rawConnection) &&
                     (rawConnection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
                      rawConnection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase));

    if (isPostgres)
    {
        // Npgsql 8.x does NOT support postgres:// URI format — must convert to key=value string
        var connectionString = ConvertPostgresUriToConnectionString(rawConnection!);

        // PostgreSQL — Render / Docker production
        options.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null
            );
        });
    }
    else
    {
        // SQL Server — local development
        options.UseSqlServer(rawConnection, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null
            );
        });
    }
});

// Add Razor Pages and API Controllers
builder.Services.AddRazorPages();
builder.Services.AddControllers();

// Add Swagger Documentation Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MyOnlineCv API", Version = "v1" });
});

var app = builder.Build();

// ─────────────────────────────────────────────────────────────
// Auto-Apply Migrations on Startup
//
// Strategy:
//   • PostgreSQL (Render/Docker): MigrateAsync() — applies EF migrations
//   • SQL Server  (local):        EnsureCreated() — creates schema without
//     migrations, preserving existing local data on restart
// ─────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var dbContext = services.GetRequiredService<ApplicationDbContext>();
        var providerName = dbContext.Database.ProviderName ?? string.Empty;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            // Production (Render / Docker) — run EF Core migrations
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            // Local development (SQL Server) — create schema if it doesn't exist
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or initializing the database.");
    }
}

// ─────────────────────────────────────────────────────────────
// Middleware Pipeline
// ─────────────────────────────────────────────────────────────

// Must come first — resolves X-Forwarded-Proto from load balancer
app.UseForwardedHeaders();

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Enable Swagger UI (Accessible at /swagger)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MyOnlineCv API v1");
    c.RoutePrefix = "swagger";
});

// ⚠ UseHttpsRedirection intentionally omitted when running behind a reverse proxy
// (Render / Docker terminates TLS at the edge — the container only receives HTTP).
// The IsDevelopment() guard below keeps it active for local HTTPS profiles.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowAll");
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();

// ─────────────────────────────────────────────────────────────
// Helper: Convert Render's postgres:// URI → Npgsql connection string
//
// Npgsql 8.x cannot parse postgres:// or postgresql:// URI format.
// Render's DATABASE_URL is always in URI format, so we convert it.
//
// Input:  postgres://user:password@host:5432/database
// Output: Host=host;Port=5432;Database=database;Username=user;Password=password;SSL Mode=Prefer;Trust Server Certificate=true
// ─────────────────────────────────────────────────────────────
static string ConvertPostgresUriToConnectionString(string uri)
{
    try
    {
        // Uri class requires a valid scheme — normalise postgres:// → postgresql://
        var normalized = uri.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            ? "postgresql://" + uri["postgres://".Length..]
            : uri;

        var parsed = new Uri(normalized);

        var host     = parsed.Host;
        var port     = parsed.Port > 0 ? parsed.Port : 5432;
        var database = parsed.AbsolutePath.TrimStart('/');

        // UserInfo is "username:password" — split on first colon only
        var userParts = parsed.UserInfo.Split(':', 2);
        var username  = Uri.UnescapeDataString(userParts[0]);
        var password  = userParts.Length > 1 ? Uri.UnescapeDataString(userParts[1]) : string.Empty;

        // SSL Mode=Prefer works for both Render internal (no SSL) and external (SSL) connections
        return $"Host={host};Port={port};Database={database};Username={username};Password={password};" +
               $"SSL Mode=Prefer;Trust Server Certificate=true";
    }
    catch
    {
        // If parsing fails for any reason, return the original and let Npgsql try
        return uri;
    }
}