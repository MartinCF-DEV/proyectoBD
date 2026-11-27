using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Community360.Api.Configuration;
using Community360.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var loadedEnvPath = DotEnvLoader.LoadFromHierarchy(Directory.GetCurrentDirectory())
    ?? DotEnvLoader.LoadFromHierarchy(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

if (loadedEnvPath is not null)
{
    Console.WriteLine($"[INFO] Variables de entorno cargadas desde: {loadedEnvPath}");
}

var jwtSecret = DotEnvLoader.GetRequired("JWT_SECRET");
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "Community360.Api";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "Community360.Web";

builder.Services.AddSingleton<TokenRevocationService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<CloudDatabaseService>();
builder.Services.AddSingleton<WebSocketHubService>();

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        var allowedOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? ["http://localhost:4200", "http://127.0.0.1:4200"];

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "nombre",
            RoleClaimType = "rol"
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var revocationService = context.HttpContext.RequestServices.GetRequiredService<TokenRevocationService>();
                var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (revocationService.IsRevoked(jti))
                {
                    context.Fail("El token de sesión fue revocado mediante cierre de sesión.");
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Inicializar conexión a Couchbase Capella y DataStax Astra DB al arrancar
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var db = app.Services.GetRequiredService<CloudDatabaseService>();
            await db.InitializeAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Aviso durante la conexión inicial con la nube: {ex.Message}");
        }
    });
});

app.UseCors("FrontendPolicy");
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Map("/ws", async context =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var wsHub = context.RequestServices.GetRequiredService<WebSocketHubService>();
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        await wsHub.HandleConnectionAsync(ws, context.RequestAborted);
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapGet("/api/health", () => Results.Ok(new
{
    estado = "ok",
    servicio = "Community 360 API (.NET 10)",
    timestamp_utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
}));

app.Run();
