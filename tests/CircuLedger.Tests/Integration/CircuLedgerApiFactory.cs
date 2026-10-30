using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuLedger.Infrastructure.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace CircuLedger.Tests.Integration;

/// <summary>Boots the real API against a throwaway PostgreSQL 16 container. Migrations and seed data run on startup.</summary>
public sealed class CircuLedgerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DemoPassword = "Test!Demo-Password1";
    public const string AdminPassword = "Test!Admin-Password1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("circuledger")
        .WithUsername("circuledger")
        .WithPassword("integration-tests")
        .Build();

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "circuledger-tests", Guid.NewGuid().ToString("N"));

    public string ConnectionString => _postgres.GetConnectionString();

    public FakeSmsSender Sms => Services.GetRequiredService<FakeSmsSender>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:CircuLedgerDb", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", "integration-tests-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("DataProtection:PhoneHashKey", "HJ6ZOhyaDvcxlfkn97den3Vdd65EaK9fBYKc/m641dE=");
        builder.UseSetting("DataProtection:ImeiHashKey", "H8gltGTiaMpskaHMa3+HEDciPGGcaARDn/FdoaKgPtM=");
        builder.UseSetting("DataProtection:PhoneEncryptionKey", "4ZaMjvPxe1as29EPARCLbB9G5DcMy8uuoHr6ZEckn1M=");
        builder.UseSetting("Certificates:Signing:PrivateKeyPath", Path.Combine(_dataRoot, "keys", "signing.pem"));
        builder.UseSetting("Certificates:Signing:AutoGenerateIfMissing", "true");
        builder.UseSetting("Storage:LocalRoot", Path.Combine(_dataRoot, "uploads"));
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:DemoData", "true");
        builder.UseSetting("Seed:DemoPassword", DemoPassword);
        builder.UseSetting("Seed:InitialSeedFundSle", "25000");
        builder.UseSetting("RateLimiting:PublicPerMinute", "10000");
        builder.UseSetting("RateLimiting:UssdPerMinute", "10000");
        builder.UseSetting("RateLimiting:AuthPerMinute", "10000");
        builder.UseSetting("Hub:PublicBaseUrl", "https://circuledger.test");
    }

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        try { Directory.Delete(_dataRoot, recursive: true); } catch (IOException) { }
    }

    public async Task<HttpClient> ClientForAsync(string email, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        return client;
    }

    public Task<HttpClient> AdminAsync() => ClientForAsync("admin@circuledger.local", AdminPassword);
    public Task<HttpClient> OpsAsync() => ClientForAsync("ops@circuledger.local", DemoPassword);
    public Task<HttpClient> TechAsync() => ClientForAsync("tech@circuledger.local", DemoPassword);
    public Task<HttpClient> AgentAsync() => ClientForAsync("agent@circuledger.local", DemoPassword);
    public Task<HttpClient> BuyerAsync() => ClientForAsync("buyer@circuledger.local", DemoPassword);
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<CircuLedgerApiFactory>
{
    public const string Name = "CircuLedger API";
}

internal static class TestData
{
    private static int _counter = Random.Shared.Next(1000, 9000);

    /// <summary>A unique, Luhn-valid 15-digit IMEI.</summary>
    public static string Imei()
    {
        var body = $"35{Interlocked.Increment(ref _counter):D6}{Random.Shared.Next(0, 999999):D6}";
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var d = body[13 - i] - '0';
            if (i % 2 == 0) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return body + (10 - sum % 10) % 10;
    }

    /// <summary>A unique Sierra Leone mobile number in the fictional 99 range.</summary>
    public static string Phone() => $"+23299{Random.Shared.Next(100000, 999999)}";

    /// <summary>Smallest valid PNG (1×1 transparent pixel).</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static async Task<JsonElement> Json(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static string Str(this JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
