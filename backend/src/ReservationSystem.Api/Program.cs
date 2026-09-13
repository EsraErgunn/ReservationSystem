using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ReservationSystem.Api.Handlers;
using ReservationSystem.Api.Options;
using ReservationSystem.Api.Services;
using ReservationSystem.Application;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Infrastructure;
using ReservationSystem.Infrastructure.Identity;
using ReservationSystem.Infrastructure.RealTime;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------- Loglama ----------
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---------- Katmanlar ----------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// ---------- HTTP ----------
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(ConfigureSwagger);
// SignalR'ı Infrastructure kaydediyor (IHubContext'i SignalRSeatNotifier kullanıyor).

builder.Services.Configure<FrontendOptions>(
    builder.Configuration.GetSection(FrontendOptions.SectionName));

// ---------- Kimlik doğrulama ----------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt bölümü yapılandırılmamış.");

if (jwt.Key.Length < JwtOptions.MinimumKeyLength)
    throw new InvalidOperationException(
        $"Jwt:Key en az {JwtOptions.MinimumKeyLength} karakter olmalı (HmacSha256 için 256 bit). " +
        "user-secrets veya ortam değişkeni ile verin.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR, WebSocket üzerinden Authorization header gönderemez; token
        // query string'den okunur. Yalnızca /hubs altı için — başka yerde
        // token'ın URL'e (ve log'lara) düşmesini istemiyoruz.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// ---------- CORS ----------
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? throw new InvalidOperationException("Cors:Origins yapılandırılmamış.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());   // SignalR için şart
});

// ---------- Rate limiting (NFR-06) ----------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("reservation", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PartitionKeyFor(ctx),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));

    // auth.md §7: giriş ucu kaba kuvvetin birincil hedefi. Partition IP bazlı —
    // kullanıcı henüz kimlik doğrulamamış olduğu için claim yok.
    options.AddPolicy("auth", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("payment", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: PartitionKeyFor(ctx),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// ---------- Sağlık kontrolü (NFR-11) ----------
var postgres = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres yapılandırılmamış.");
var redis = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("ConnectionStrings:Redis yapılandırılmamış.");

builder.Services.AddHealthChecks()
    .AddNpgSql(postgres, name: "postgres")
    .AddRedis(redis, name: "redis");

var app = builder.Build();

// ---------- Pipeline — SIRA ÖNEMLİ ----------
// UseExceptionHandler en üstte: altındaki her şeyin hatasını yakalayabilmesi için.
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// UseCors, UseAuthentication'dan ÖNCE: aksi halde tarayıcının preflight (OPTIONS)
// isteği kimlik doğrulamaya takılır ve yanıltıcı bir CORS hatası alınır.
app.UseCors("Frontend");

// Sıra sabit: önce "sen kimsin", sonra "yetkin var mı".
app.UseAuthentication();

// UseRateLimiter ikisinin ARASINDA (api-katmani.md §5). UseAuthentication'dan önce
// olsaydı ctx.User henüz boş olur, partitionKey daima IP'ye düşer ve NAT arkasındaki
// tüm kullanıcılar tek kotayı paylaşırdı. UseAuthorization'dan önce olması ise
// kimliksiz isteğin 401'e takılıp limiti atlamasını engelliyor.
app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();
app.MapHub<SeatHub>("/hubs/seats");
app.MapHealthChecks("/health");

app.Run();

static string PartitionKeyFor(HttpContext ctx) =>
    ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    ?? ctx.Connection.RemoteIpAddress?.ToString()
    ?? "anonymous";

static void ConfigureSwagger(Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions options)
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ReservationSystem API",
        Version = "v1"
    });

    // Swagger UI'daki "Authorize" düğmesi — korumalı uçları elle test edebilmek için.
    const string scheme = "Bearer";

    options.AddSecurityDefinition(scheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT erişim belirteci."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = scheme
            }
        }] = []
    });
}

/// <summary>Entegrasyon testlerinin (<c>WebApplicationFactory</c>) görebilmesi için.</summary>
public partial class Program;
