using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CircuLedger.Tests.Integration;

/// <summary>
/// The full pilot pipeline over HTTP against real PostgreSQL:
/// USSD sell → pickup batch → collect → receive → purchase → sanitize → certificate → public verify
/// → refurbish → listing + verified photo → order → payment + reinvestment → KPIs → ledger verification.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class EndToEndPipelineTests(CircuLedgerApiFactory factory)
{
    private async Task<string> UssdAsync(HttpClient client, string session, string phone, string text)
    {
        var response = await client.PostAsync("/api/v1/ussd/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["sessionId"] = session, ["serviceCode"] = "*384*1234#", ["phoneNumber"] = phone, ["text"] = text
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    [GeneratedRegex(@"Ref: (SY[A-Z0-9]{6})")]
    private static partial Regex RefPattern();

    [Fact]
    public async Task Full_pipeline_from_ussd_to_reinvestment()
    {
        var anonymous = factory.CreateClient();
        var phone = TestData.Phone();
        var session = $"ATUid_{Guid.NewGuid():N}";

        // 1. USSD: sell a Samsung with a broken screen, pickup in Towama.
        Assert.StartsWith("CON Smart Youth", await UssdAsync(anonymous, session, phone, ""));
        Assert.Contains("Samsung", await UssdAsync(anonymous, session, phone, "1"));
        await UssdAsync(anonymous, session, phone, "1*1");
        await UssdAsync(anonymous, session, phone, "1*1*2");
        await UssdAsync(anonymous, session, phone, "1*1*2*2");
        var confirm = await UssdAsync(anonymous, session, phone, "1*1*2*2*Towama layout");
        Assert.Contains("Est. offer: SLE 200", confirm);
        var end = await UssdAsync(anonymous, session, phone, "1*1*2*2*Towama layout*1");
        Assert.StartsWith("END Thank you!", end);
        var reference = RefPattern().Match(end).Groups[1].Value;

        // Gateway retry of the final hop is idempotent: same answer, still one request.
        Assert.Equal(end, await UssdAsync(anonymous, session, phone, "1*1*2*2*Towama layout*1"));
        Assert.Contains(factory.Sms.Outbox, m => m.To == phone && m.Message.Contains(reference));

        var ops = await factory.OpsAsync();
        var requests = await (await ops.GetAsync($"/api/v1/intake-requests?referenceCode={reference}")).Json();
        Assert.Equal(1, requests.GetProperty("totalCount").GetInt32());
        var intake = requests.GetProperty("items")[0];
        var intakeId = intake.Str("id");
        Assert.Equal("Pending", intake.Str("status"));
        Assert.DoesNotContain(phone[^6..], intake.Str("citizenPhoneMasked")); // masked
        Assert.True(intake.GetProperty("pickupLatitude").ValueKind == JsonValueKind.Number, "Towama should be geocoded");

        // 2. Dispatch: batch nearby pickups, then the agent collects.
        var generated = await (await ops.PostAsJsonAsync("/api/v1/dispatch/batches/generate", new { radiusKm = 5 })).Json();
        var batch = generated.GetProperty("batches").EnumerateArray()
            .Single(b => b.GetProperty("pickups").EnumerateArray().Any(p => p.Str("referenceCode") == reference));
        var batchId = batch.Str("id");

        var agent = await factory.AgentAsync();
        var mine = await (await agent.GetAsync("/api/v1/dispatch/my-batches")).Json();
        Assert.Contains(mine.EnumerateArray(), b => b.Str("id") == batchId);
        var contact = await (await agent.GetAsync($"/api/v1/dispatch/batches/{batchId}/pickups/{intakeId}/contact")).Json();
        Assert.Equal(phone, contact.Str("phone"));
        await (await agent.PostAsync($"/api/v1/dispatch/batches/{batchId}/pickups/{intakeId}/collect", null)).Json();

        // 3. Receive at Njala Hub, creating the device and its tracking ID.
        var points = await (await anonymous.GetAsync("/api/v1/public/drop-off-points")).Json();
        var njalaHub = points.EnumerateArray().First(p => p.Str("name") == "Njala Hub").Str("id");
        var imei = TestData.Imei();
        var device = await (await ops.PostAsJsonAsync($"/api/v1/intake-requests/{intakeId}/receive",
            new { dropOffPointId = njalaHub, imei, model = "Galaxy A14" })).Json();
        var deviceId = device.Str("id");
        var trackingId = device.Str("trackingId");
        Assert.Matches(@"^CL-\d{4}-\d{6}$", trackingId);
        Assert.True(device.GetProperty("isSamsungFlagship").GetBoolean());
        Assert.Equal($"***********{imei[^4..]}", device.Str("maskedImei"));

        // 4. Purchase: debits the seed fund.
        var fundsBefore = await (await ops.GetAsync("/api/v1/funds")).Json();
        var seedBefore = SeedBalance(fundsBefore);
        var purchase = await (await ops.PostAsJsonAsync($"/api/v1/devices/{deviceId}/purchase", new { payoutMethod = "OrangeMoney" })).Json();
        Assert.Equal(200m, purchase.GetProperty("paidPriceSle").GetDecimal());
        Assert.Equal(seedBefore - 200m, SeedBalance(await (await ops.GetAsync("/api/v1/funds")).Json()));

        // 5. Sanitization → certificate.
        var tech = await factory.TechAsync();
        var now = DateTime.UtcNow;
        var sanitization = await (await tech.PostAsJsonAsync($"/api/v1/devices/{deviceId}/sanitizations", new
        {
            method = "SamsungKnoxGradeErase", startedAt = now.AddMinutes(-20), completedAt = now.AddMinutes(-5),
            passes = 1, outcome = "Passed", toolName = "Samsung Knox Wipe"
        })).Json();
        var certificateNumber = sanitization.GetProperty("certificate").Str("certificateNumber");
        Assert.Matches(@"^CDS-\d{4}-\d{6}$", certificateNumber);
        Assert.Contains(factory.Sms.Outbox, m => m.To == phone && m.Message.Contains(certificateNumber));

        var pdf = await tech.GetAsync($"/api/v1/certificates/{certificateNumber}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        // 6. Public verification: anonymous, valid, no personal data.
        var verify = await anonymous.GetAsync($"/api/v1/public/certificates/{certificateNumber}/verify");
        var verifyBody = await verify.Content.ReadAsStringAsync();
        var verification = await verify.Json();
        Assert.True(verification.GetProperty("isValid").GetBoolean());
        Assert.Equal("Valid", verification.Str("status"));
        Assert.DoesNotContain(phone, verifyBody);
        Assert.DoesNotContain(imei, verifyBody);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/public/certificates/CDS-1999-999999/verify")).StatusCode);

        // 7. Refurbish and list with a verified photo.
        await (await tech.PutAsJsonAsync($"/api/v1/devices/{deviceId}/recovery-outcome", new { outcome = "Refurbish" })).Json();
        var listing = await (await ops.PostAsJsonAsync("/api/v1/listings", new
        {
            itemType = "RefurbishedDevice", deviceId, title = "Samsung Galaxy A14 (refurbished)", priceSle = 1200m,
            condition = "B", warrantyDays = 90
        })).Json();
        var listingId = listing.Str("id");
        Assert.Equal(certificateNumber, listing.Str("certificateNumber"));

        var publishTooEarly = await ops.PostAsync($"/api/v1/listings/{listingId}/publish", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, publishTooEarly.StatusCode); // no verified photo yet

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestData.Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "front.png");
        var photo = await (await tech.PostAsync($"/api/v1/listings/{listingId}/photos", form)).Json();
        await (await ops.PostAsync($"/api/v1/listings/{listingId}/photos/{photo.Str("id")}/verify", null)).Json();
        await (await ops.PostAsync($"/api/v1/listings/{listingId}/publish", null)).Json();

        var browse = await (await anonymous.GetAsync("/api/v1/public/listings?samsungOnly=true&minPrice=1000&maxPrice=1500&pageSize=100")).Json();
        var publicListing = browse.GetProperty("items").EnumerateArray().Single(l => l.Str("id") == listingId);
        Assert.Equal(certificateNumber, publicListing.Str("certificateNumber"));
        var photoResponse = await anonymous.GetAsync(publicListing.GetProperty("photos")[0].Str("url"));
        Assert.Equal("image/png", photoResponse.Content.Headers.ContentType?.MediaType);

        // 8. Buyer orders; ops confirms payment, which triggers reinvestment in the same transaction.
        var buyer = await factory.BuyerAsync();
        var order = await (await buyer.PostAsJsonAsync("/api/v1/orders", new { listingIds = new[] { listingId } })).Json();
        Assert.Equal(1200m, order.GetProperty("totalSle").GetDecimal());

        var secondBuyerAttempt = await buyer.PostAsJsonAsync("/api/v1/orders", new { listingIds = new[] { listingId } });
        Assert.Equal(HttpStatusCode.Conflict, secondBuyerAttempt.StatusCode); // reserved

        var paid = await (await ops.PostAsJsonAsync($"/api/v1/orders/{order.Str("id")}/pay",
            new { paymentMethod = "OrangeMoney", paymentReference = "OM-778812" })).Json();
        Assert.Equal("Paid", paid.Str("status"));
        var reinvestment = paid.GetProperty("reinvestment");
        Assert.Equal(240m, reinvestment.GetProperty("seedFundSle").GetDecimal());     // 20% of 1200
        Assert.Equal(120m, reinvestment.GetProperty("trainingFundSle").GetDecimal()); // 10% of 1200
        Assert.Equal(seedBefore - 200m + 240m, SeedBalance(await (await ops.GetAsync("/api/v1/funds")).Json()));

        // 9. Dashboard KPIs.
        var kpis = await (await ops.GetAsync("/api/v1/dashboard/kpis")).Json();
        Assert.True(kpis.GetProperty("certificatesIssued").GetInt32() >= 1);
        Assert.Equal(500m, kpis.GetProperty("devicesCollected").GetProperty("target").GetDecimal());
        Assert.True(kpis.GetProperty("totalReinvestedSle").GetDecimal() >= 360m);
        Assert.True(kpis.GetProperty("marketplaceRevenueSle").GetDecimal() >= 1200m);
        var csv = await ops.GetStringAsync("/api/v1/dashboard/export/kpis.csv");
        Assert.StartsWith("indicator,actual,target,percent_of_target", csv);

        // 10. Ledger: complete device history and an intact chain.
        var history = await (await ops.GetAsync($"/api/v1/ledger/devices/{trackingId}")).Json();
        var events = history.GetProperty("events").EnumerateArray().Select(e => e.Str("eventType")).ToList();
        Assert.Equal(["IntakeCreated", "DeviceReceived", "Purchased", "Sanitized", "CertificateIssued", "Listed", "Sold"], events);
        Assert.DoesNotContain(phone, history.GetRawText());

        var ledger = await (await ops.GetAsync("/api/v1/ledger/verify")).Json();
        Assert.True(ledger.GetProperty("isValid").GetBoolean(), ledger.GetRawText());
    }

    private static decimal SeedBalance(JsonElement funds) =>
        funds.GetProperty("funds").EnumerateArray().Single(f => f.Str("type") == "SeedFund").GetProperty("balanceSle").GetDecimal();
}
