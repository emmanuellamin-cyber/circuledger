using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Asp.Versioning;
using CircuLedger.Api.Controllers;
using CircuLedger.Api.Infrastructure;
using CircuLedger.Application;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Infrastructure;
using CircuLedger.Infrastructure.Identity;
using CircuLedger.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Logging: structured, and no personal data. Services log hashes, masked numbers or IDs only.
    builder.Services.AddSerilog((services, cfg) => cfg
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "CircuLedger.Api")
        .WriteTo.Console());

    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

    builder.Services
        .AddControllers(o => o.Filters.Add<FluentValidationFilter>())
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

    builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Instance = $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    });
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services
        .AddApiVersioning(o =>
        {
            o.DefaultApiVersion = new ApiVersion(1, 0);
            o.AssumeDefaultVersionWhenUnspecified = true;
            o.ReportApiVersions = true;
            o.ApiVersionReader = new UrlSegmentApiVersionReader();
        })
        .AddMvc()
        .AddApiExplorer(o =>
        {
            o.GroupNameFormat = "'v'VVV";
            o.SubstituteApiVersionInUrl = true;
        });

    builder.Services.AddOpenApi("v1", o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

    // Authentication: JWT bearer access tokens + rotating refresh tokens (see AuthService).
    var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey.PadRight(32))),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin))
        .AddPolicy(Policies.Operations, p => p.RequireRole(Roles.Admin, Roles.OperationsLead))
        .AddPolicy(Policies.Workshop, p => p.RequireRole(Roles.Admin, Roles.OperationsLead, Roles.Technician))
        .AddPolicy(Policies.Field, p => p.RequireRole(Roles.Admin, Roles.OperationsLead, Roles.FieldAgent))
        .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Admin, Roles.OperationsLead, Roles.Technician, Roles.FieldAgent))
        .AddPolicy(Policies.Buyer, p => p.RequireRole(Roles.Buyer, Roles.Admin, Roles.OperationsLead));

    // Rate limiting: per client IP on the public, USSD/SMS and auth surfaces.
    var limits = builder.Configuration.GetSection("RateLimiting");
    builder.Services.AddRateLimiter(o =>
    {
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        void Policy(string name, int perMinute) => o.AddPolicy(name, ctx => RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        Policy(RateLimitPolicies.Public, limits.GetValue("PublicPerMinute", 60));
        Policy(RateLimitPolicies.Ussd, limits.GetValue("UssdPerMinute", 300));
        Policy(RateLimitPolicies.Auth, limits.GetValue("AuthPerMinute", 10));
    });

    // CORS for the admin dashboard and marketplace front-ends. "*" in Cors:AllowedOrigins allows any origin.
    var origins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .Select(origin => origin.Trim().TrimEnd('/'))
        .Where(origin => origin.Length > 0)
        .ToArray();
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    {
        if (origins.Contains("*")) p.SetIsOriginAllowed(_ => true);
        else p.WithOrigins(origins);
        p.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders("api-supported-versions", "Content-Disposition");
    }));

    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });

    var app = builder.Build();

    app.UseForwardedHeaders();
    // Request logging wraps the exception handler, so it records the final ProblemDetails status (e.g. 409), not 500.
    app.UseSerilogRequestLogging(o =>
        o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms");
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Configuration.GetValue("Swagger:Enabled", app.Environment.IsDevelopment()))
    {
        app.MapOpenApi();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/openapi/v1.json", "CircuLedger API v1");
            o.RoutePrefix = "swagger";
        });
    }

    if (app.Configuration.GetValue("UseHttpsRedirection", false)) app.UseHttpsRedirection();

    app.UseCors();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status = report.Status.ToString(),
                durationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), e.Value.Description })
            }));
        }
    }).AllowAnonymous();

    if (app.Configuration.GetValue("Seed:RunOnStartup", true))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "CircuLedger API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
