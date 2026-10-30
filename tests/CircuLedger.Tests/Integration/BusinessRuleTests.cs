using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace CircuLedger.Tests.Integration;

[Collection(ApiCollection.Name)]
public class BusinessRuleTests(CircuLedgerApiFactory factory)
{
    private static async Task<JsonElement> WalkInAsync(HttpClient ops, string brand = "Samsung", string condition = "Dead")
    {
        var points = await (await ops.GetAsync("/api/v1/drop-off-points")).Json();
        var hub = points.EnumerateArray().First().Str("id");
        return await (await ops.PostAsJsonAsync("/api/v1/intake-requests/walk-in", new
        {
            phone = TestData.Phone(), brand, model = "Test", imei = TestData.Imei(), condition, dropOffPointId = hub
        })).Json();
    }

    [Fact]
    public async Task Purchase_is_blocked_with_409_when_seed_fund_is_insufficient()
    {
        var ops = await factory.OpsAsync();
        var device = await WalkInAsync(ops);

        var response = await ops.PostAsJsonAsync($"/api/v1/devices/{device.Str("id")}/purchase",
            new { payoutMethod = "Cash", agreedPriceSle = 99_999m });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("insufficient_funds", problem.Str("code"));

        var reloaded = await (await ops.GetAsync($"/api/v1/devices/{device.Str("id")}")).Json();
        Assert.Equal("Received", reloaded.Str("status")); // rolled back
    }

    [Fact]
    public async Task Dismantling_requires_safety_training_and_logs_materials()
    {
        var ops = await factory.OpsAsync();
        var trainedTech = await factory.TechAsync();
        var device = await WalkInAsync(ops, "Tecno", "Dead");
        var id = device.Str("id");

        await (await ops.PostAsJsonAsync($"/api/v1/devices/{id}/purchase", new { payoutMethod = "Cash" })).Json();
        var now = DateTime.UtcNow;
        await (await trainedTech.PostAsJsonAsync($"/api/v1/devices/{id}/sanitizations", new
        {
            method = "StoragePhysicalDestruction", startedAt = now.AddMinutes(-10), completedAt = now.AddMinutes(-9),
            passes = 1, outcome = "NotEligible", notes = "Board dead; eMMC chip physically destroyed."
        })).Json();
        await (await trainedTech.PutAsJsonAsync($"/api/v1/devices/{id}/recovery-outcome", new { outcome = "DismantleForParts" })).Json();

        // A new technician without a safety-training record is refused.
        var admin = await factory.AdminAsync();
        var email = $"newtech-{Guid.NewGuid():N}@circuledger.local";
        await (await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email, password = "Untrained!Tech-2026", fullName = "Untrained Tech", gender = "Female", roles = new[] { "Technician" }
        })).Json();
        var untrained = await factory.ClientForAsync(email, "Untrained!Tech-2026");
        var refused = await untrained.PostAsJsonAsync($"/api/v1/devices/{id}/dismantle", new { notes = "trying" });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The trained technician can dismantle, recover a battery and log the lithium.
        var dismantled = await (await trainedTech.PostAsJsonAsync($"/api/v1/devices/{id}/dismantle", new { notes = "ok" })).Json();
        Assert.Equal("Dismantled", dismantled.Str("status"));
        var component = await (await trainedTech.PostAsJsonAsync($"/api/v1/devices/{id}/components", new { type = "Battery", grade = "C" })).Json();
        Assert.EndsWith("-C01", component.Str("componentCode"));
        var log = await (await trainedTech.PostAsJsonAsync("/api/v1/recovery/hazardous-materials", new
        {
            deviceId = id, material = "LithiumBattery", weightKg = 0.045m, storageLocation = "Njala Hub fireproof bin 2", isSwollenBattery = true
        })).Json();
        Assert.Equal(0.045m, log.GetProperty("weightKg").GetDecimal());

        var summary = await (await ops.GetAsync("/api/v1/recovery/hazardous-materials/summary")).Json();
        Assert.True(summary.GetProperty("lithiumKg").GetDecimal() >= 0.045m);
    }

    [Fact]
    public async Task Ledger_rows_cannot_be_updated_or_deleted_even_with_raw_sql()
    {
        var ops = await factory.OpsAsync();
        await WalkInAsync(ops); // ensure at least one entry

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();

        await using var update = new NpgsqlCommand("UPDATE ledger_entries SET actor = 'attacker'", connection);
        var updateError = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, updateError.SqlState);

        await using var delete = new NpgsqlCommand("DELETE FROM ledger_entries", connection);
        await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        var verify = await (await ops.GetAsync("/api/v1/ledger/verify")).Json();
        Assert.True(verify.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task Concurrent_ledger_writes_keep_the_chain_intact()
    {
        var ops = await factory.OpsAsync();
        var tasks = Enumerable.Range(0, 8).Select(async _ =>
        {
            var client = await factory.OpsAsync();
            return await WalkInAsync(client, "Itel", "Dead");
        });
        await Task.WhenAll(tasks);

        var verify = await (await ops.GetAsync("/api/v1/ledger/verify")).Json();
        Assert.True(verify.GetProperty("isValid").GetBoolean(), verify.GetRawText());
    }

    [Fact]
    public async Task Ussd_sessions_create_at_most_one_request_and_status_is_only_visible_to_owner()
    {
        var client = factory.CreateClient();
        var phone = TestData.Phone();
        var session = $"ATUid_{Guid.NewGuid():N}";
        async Task<string> Hop(string text, string? from = null, string? sessionId = null)
        {
            var response = await client.PostAsync("/api/v1/ussd/callback", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["sessionId"] = sessionId ?? session, ["serviceCode"] = "*384*1234#", ["phoneNumber"] = from ?? phone, ["text"] = text
            }));
            return await response.Content.ReadAsStringAsync();
        }

        var final = await Hop("1*2*1*1*1*1"); // Tecno, dead, drop-off, first point, confirm
        Assert.StartsWith("END Thank you!", final);
        var reference = final.Split("Ref: ")[1][..8];

        // Retried as a "new" hop with a different text in the same session: no second request.
        await Hop("1*2*1*1*1");
        Assert.Equal(final, await Hop("1*2*1*1*1*1"));
        var ops = await factory.OpsAsync();
        var found = await (await ops.GetAsync($"/api/v1/intake-requests?referenceCode={reference}")).Json();
        Assert.Equal(1, found.GetProperty("totalCount").GetInt32());

        Assert.Contains("Pending", await Hop($"2*{reference}", sessionId: $"s-{Guid.NewGuid():N}"));
        Assert.Contains("No request", await Hop($"2*{reference}", from: TestData.Phone(), sessionId: $"s-{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task Inbound_sms_sell_creates_a_pickup_request()
    {
        var client = factory.CreateClient();
        var phone = TestData.Phone();
        var response = await client.PostAsync("/api/v1/sms/inbound", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["from"] = phone, ["to"] = "21212", ["text"] = "SELL Samsung SCREEN Kowama market", ["id"] = Guid.NewGuid().ToString()
        }));
        var result = await response.Json();
        Assert.Equal("SELL", result.Str("keyword"));
        Assert.Contains(factory.Sms.Outbox, m => m.To == phone && m.Message.Contains("received"));
    }

    [Fact]
    public async Task Auth_refresh_tokens_rotate_and_reuse_is_rejected()
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = "buyer@circuledger.local", password = CircuLedgerApiFactory.DemoPassword })).Json();
        var refresh1 = login.Str("refreshToken");

        var rotated = await (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 })).Json();
        Assert.NotEqual(refresh1, rotated.Str("refreshToken"));

        var reuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        // Reuse detection revoked the whole family, including the rotated token.
        var afterReuse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated.Str("refreshToken") });
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.StatusCode);
    }

    [Fact]
    public async Task Role_policies_are_enforced()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/devices")).StatusCode);

        var buyer = await factory.BuyerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync("/api/v1/dashboard/kpis")).StatusCode);

        var tech = await factory.TechAsync();
        Assert.Equal(HttpStatusCode.OK, (await tech.GetAsync("/api/v1/drop-off-points")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/dispatch/batches/generate", new { })).StatusCode);
    }

    [Fact]
    public async Task Validation_errors_return_problem_details()
    {
        var ops = await factory.OpsAsync();
        var response = await ops.PostAsJsonAsync("/api/v1/intake-requests/walk-in", new
        {
            phone = "123", brand = "", condition = "Dead", dropOffPointId = Guid.Empty, imei = "123456789012345"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Phone", out _));
        Assert.True(errors.TryGetProperty("Imei", out _));
    }

    [Fact]
    public async Task Health_endpoint_reports_postgres()
    {
        var health = await (await factory.CreateClient().GetAsync("/health")).Json();
        Assert.Equal("Healthy", health.Str("status"));
        Assert.Contains(health.GetProperty("checks").EnumerateArray(), c => c.Str("name") == "postgresql");
    }
}
