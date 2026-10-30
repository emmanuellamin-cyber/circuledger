using CircuLedger.Application.Common;
using CircuLedger.Application.Intake;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CircuLedger.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public bool MigrateOnStartup { get; set; } = true;
    public string AdminEmail { get; set; } = "admin@circuledger.local";

    /// <summary>Required to create the admin user. Supply it via env var Seed__AdminPassword.</summary>
    public string? AdminPassword { get; set; }

    public decimal InitialSeedFundSle { get; set; } = 25_000m;

    /// <summary>Demo staff accounts and sample devices. Development only.</summary>
    public bool DemoData { get; set; }
    public string? DemoPassword { get; set; }
}

/// <summary>Idempotent: every block checks whether its data already exists.</summary>
public sealed class DatabaseSeeder(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IIntakeService intake,
    IOptions<SeedOptions> options,
    TimeProvider time,
    ILogger<DatabaseSeeder> logger)
{
    private readonly SeedOptions _o = options.Value;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (_o.MigrateOnStartup) await db.Database.MigrateAsync(ct);

        await SeedRolesAsync();
        await SeedAdminAsync();
        await SeedDropOffPointsAsync(ct);
        await SeedPricingAsync(ct);
        await SeedReinvestmentRuleAsync(ct);
        await SeedFundsAsync(ct);

        if (_o.DemoData)
        {
            await SeedDemoUsersAsync(ct);
            await SeedSampleDevicesAsync(ct);
        }
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    private async Task SeedAdminAsync()
    {
        if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count > 0) return;
        if (string.IsNullOrWhiteSpace(_o.AdminPassword))
        {
            logger.LogWarning("No admin user exists and Seed:AdminPassword is not set; skipping admin seeding");
            return;
        }
        await CreateUserAsync(_o.AdminEmail, _o.AdminPassword, "Hub Administrator", Gender.Unspecified, [Roles.Admin]);
        logger.LogInformation("Seeded admin user {Email}", _o.AdminEmail);
    }

    private async Task SeedDropOffPointsAsync(CancellationToken ct)
    {
        if (await db.DropOffPoints.IgnoreQueryFilters().AnyAsync(ct)) return;
        // Coordinates are approximate and should be re-surveyed on site with GPS.
        db.DropOffPoints.AddRange(
            new DropOffPoint { Name = "Njala Hub", Type = DropOffPointType.Hub, Latitude = 8.1137, Longitude = -12.0798, Landmark = "Njala University campus, ICT block", OpeningHours = "Mon-Sat 09:00-17:00", DisplayOrder = 1 },
            new DropOffPoint { Name = "Solar Makit Njala", Type = DropOffPointType.SolarMakit, Latitude = 8.1102, Longitude = -12.0751, Landmark = "Njala junction market", OpeningHours = "Mon-Sat 08:00-18:00", DisplayOrder = 2 },
            new DropOffPoint { Name = "Solar Makit Bo/Towama", Type = DropOffPointType.SolarMakit, Latitude = 7.9786, Longitude = -11.7519, Landmark = "Towama, Bo", OpeningHours = "Mon-Sat 08:00-18:00", DisplayOrder = 3 },
            new DropOffPoint { Name = "Kiosk Bo/Kowama", Type = DropOffPointType.Kiosk, Latitude = 7.9523, Longitude = -11.7296, Landmark = "Kowama, Bo", OpeningHours = "Mon-Sun 09:00-19:00", DisplayOrder = 4 });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPricingAsync(CancellationToken ct)
    {
        if (await db.PricingRules.IgnoreQueryFilters().AnyAsync(ct)) return;
        // SLE (redenominated Leone). Columns: Dead, BrokenScreen, WorkingFaulty.
        var matrix = new (string Brand, decimal Dead, decimal Screen, decimal Faulty)[]
        {
            ("Samsung", 60, 200, 300),
            ("Apple", 80, 300, 450),
            ("Tecno", 30, 120, 180),
            ("Infinix", 30, 120, 180),
            ("Itel", 20, 60, 90),
            ("Nokia", 20, 50, 80),
            (PricingRule.AnyBrand, 15, 50, 80)
        };
        foreach (var (brand, dead, screen, faulty) in matrix)
        {
            db.PricingRules.AddRange(
                new PricingRule { Brand = brand, Condition = DeviceCondition.Dead, PriceSle = dead },
                new PricingRule { Brand = brand, Condition = DeviceCondition.BrokenScreen, PriceSle = screen },
                new PricingRule { Brand = brand, Condition = DeviceCondition.WorkingFaulty, PriceSle = faulty });
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedReinvestmentRuleAsync(CancellationToken ct)
    {
        if (await db.ReinvestmentRules.IgnoreQueryFilters().AnyAsync(ct)) return;
        db.ReinvestmentRules.Add(new ReinvestmentRule
        {
            Name = "Pilot rule: 30% of each sale (20% Seed Fund, 10% Training Fund)",
            SeedFundPercent = 20,
            TrainingFundPercent = 10,
            EffectiveFrom = time.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedFundsAsync(CancellationToken ct)
    {
        if (await db.Funds.IgnoreQueryFilters().AnyAsync(ct)) return;
        var seed = new Fund { Type = FundType.SeedFund, Name = "Device Purchasing Seed Fund" };
        var training = new Fund { Type = FundType.TrainingFund, Name = "Youth Training Fund" };
        db.Funds.AddRange(seed, training);
        if (_o.InitialSeedFundSle > 0)
        {
            db.FundTransactions.Add(seed.Credit(_o.InitialSeedFundSle, FundTransactionReason.Grant, "Grant", "initial-capital",
                time.GetUtcNow().UtcDateTime, "Initial pilot capital"));
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedDemoUsersAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_o.DemoPassword)) return;
        var ops = await CreateUserAsync("ops@circuledger.local", _o.DemoPassword, "Aminata Kamara", Gender.Female, [Roles.OperationsLead]);
        var tech = await CreateUserAsync("tech@circuledger.local", _o.DemoPassword, "Fatmata Sesay", Gender.Female, [Roles.Technician]);
        await CreateUserAsync("agent@circuledger.local", _o.DemoPassword, "Mohamed Bangura", Gender.Male, [Roles.FieldAgent]);
        await CreateUserAsync("buyer@circuledger.local", _o.DemoPassword, "Demo Buyer", Gender.Unspecified, [Roles.Buyer]);

        if (!await db.TrainingCycles.AnyAsync(ct))
        {
            var cycle = new TrainingCycle
            {
                Name = "Cohort 1: Safe repair & dismantling",
                Focus = "Phone repair, data sanitization, battery safety",
                Location = "Njala Hub",
                StartDate = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-30)
            };
            db.TrainingCycles.Add(cycle);
            db.TraineeEnrollments.Add(new TraineeEnrollment
            {
                TrainingCycleId = cycle.Id,
                UserId = tech?.Id,
                FullName = "Fatmata Sesay",
                Gender = Gender.Female,
                Status = EnrollmentStatus.Completed,
                SafetyTrainingCompleted = true,
                SafetyTrainingCompletedAt = time.GetUtcNow().UtcDateTime,
                CompletedAt = time.GetUtcNow().UtcDateTime
            });
            if (ops is not null)
            {
                db.TraineeEnrollments.Add(new TraineeEnrollment
                {
                    TrainingCycleId = cycle.Id, UserId = ops.Id, FullName = "Aminata Kamara", Gender = Gender.Female,
                    Status = EnrollmentStatus.Completed, SafetyTrainingCompleted = true,
                    SafetyTrainingCompletedAt = time.GetUtcNow().UtcDateTime, CompletedAt = time.GetUtcNow().UtcDateTime
                });
            }
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task SeedSampleDevicesAsync(CancellationToken ct)
    {
        if (await db.IntakeRequests.IgnoreQueryFilters().AnyAsync(ct)) return;
        var njala = await db.DropOffPoints.FirstAsync(p => p.Name == "Njala Hub", ct);
        var kiosk = await db.DropOffPoints.FirstAsync(p => p.Name == "Kiosk Bo/Kowama", ct);

        // Fictional numbers in the reserved test range. Sample devices go through the normal pipeline, so the ledger is consistent.
        await intake.RegisterWalkInAsync(new WalkInIntakeRequest("+23299000001", "Samsung", "Galaxy A12", "356938035643809", DeviceCondition.BrokenScreen, njala.Id), ct);
        await intake.RegisterWalkInAsync(new WalkInIntakeRequest("+23299000002", "Tecno", "Spark 7", null, DeviceCondition.Dead, njala.Id), ct);
        await intake.RegisterWalkInAsync(new WalkInIntakeRequest("+23299000003", "Samsung", "Galaxy J7", "490154203237518", DeviceCondition.WorkingFaulty, kiosk.Id), ct);
        await intake.CreateAsync(new CreateIntakeRequest("+23299000004", "Itel", null, DeviceCondition.Dead, FulfilmentMethod.Pickup, null, "Towama near the mosque", null, null, IntakeChannel.Ussd), null, ct);
        await intake.CreateAsync(new CreateIntakeRequest("+23299000005", "Infinix", null, DeviceCondition.BrokenScreen, FulfilmentMethod.Pickup, null, "Kowama market", null, null, IntakeChannel.Ussd), null, ct);
        logger.LogInformation("Seeded sample devices and pickup requests");
    }

    private async Task<ApplicationUser?> CreateUserAsync(string email, string password, string name, Gender gender, string[] roles)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return existing;
        var user = new ApplicationUser
        {
            UserName = email, Email = email, EmailConfirmed = true, FullName = name, Gender = gender,
            CreatedAt = time.GetUtcNow().UtcDateTime
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            logger.LogError("Could not seed user {Email}: {Errors}", email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }
        await userManager.AddToRolesAsync(user, roles);
        return user;
    }
}
