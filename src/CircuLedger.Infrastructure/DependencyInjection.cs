using CircuLedger.Application.Auth;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.Ledger;
using CircuLedger.Infrastructure.Certificates;
using CircuLedger.Infrastructure.Geo;
using CircuLedger.Infrastructure.Identity;
using CircuLedger.Infrastructure.Ledger;
using CircuLedger.Infrastructure.Messaging;
using CircuLedger.Infrastructure.Persistence;
using CircuLedger.Infrastructure.Persistence.Seed;
using CircuLedger.Infrastructure.Security;
using CircuLedger.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CircuLedger.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "CircuLedgerDb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = PostgresConnectionString.Normalize(
            configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured."));

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContext<ApplicationDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => System.Text.Encoding.UTF8.GetByteCount(o.SigningKey) >= 32, "Jwt:SigningKey must be at least 32 bytes.")
            .ValidateOnStart();
        services.AddOptions<DataProtectionKeysOptions>().Bind(configuration.GetSection(DataProtectionKeysOptions.Section));
        services.AddOptions<CertificateSigningOptions>().Bind(configuration.GetSection(CertificateSigningOptions.Section));
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.Section));
        services.AddOptions<GazetteerOptions>().Bind(configuration.GetSection(GazetteerOptions.Section));
        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.Section));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<IUserService>(sp => sp.GetRequiredService<UserService>());
        services.AddScoped<IUserDirectory>(sp => sp.GetRequiredService<UserService>());
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<DatabaseSeeder>();

        services.AddSingleton<Application.Common.Interfaces.IPersonalDataProtector, PersonalDataProtector>();
        services.AddSingleton<FakeSmsSender>();
        services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<FakeSmsSender>());
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        services.AddSingleton<ICertificateSigner, RsaCertificateSigner>();
        services.AddSingleton<ICertificateDocumentRenderer, CertificateDocumentRenderer>();
        services.AddSingleton<IGeocoder, GazetteerGeocoder>();

        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        services.AddHealthChecks().AddNpgSql(connectionString, name: "postgresql", tags: ["db", "ready"]);
        return services;
    }
}
