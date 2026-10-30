using System.Security.Cryptography;
using System.Text;
using CircuLedger.Application.Sms;
using CircuLedger.Application.Ussd;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CircuLedger.Api.Controllers;

/// <summary>Africa's Talking-style webhooks. Optionally protected by a shared token in the callback URL (?token=...).</summary>
public abstract class GatewayControllerBase(IConfiguration configuration) : ApiControllerBase
{
    protected bool IsAuthorizedGateway(string? token)
    {
        var expected = configuration["Gateway:WebhookToken"];
        if (string.IsNullOrEmpty(expected)) return true;
        return token is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected));
    }
}

[Route("api/v{version:apiVersion}/ussd")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Ussd)]
public sealed class UssdController(IUssdService ussd, IConfiguration configuration) : GatewayControllerBase(configuration)
{
    /// <summary>USSD callback. Responds in plain text starting with "CON " (continue) or "END " (final screen).</summary>
    [HttpPost("callback")]
    [Consumes("application/x-www-form-urlencoded")]
    [Produces("text/plain")]
    public async Task<IActionResult> Callback([FromForm] UssdCallbackRequest request, [FromQuery] string? token, CancellationToken ct)
    {
        if (!IsAuthorizedGateway(token)) return Unauthorized();
        var response = await ussd.HandleAsync(request, ct);
        return Content(response, "text/plain", Encoding.UTF8);
    }
}

[Route("api/v{version:apiVersion}/sms")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Ussd)]
public sealed class SmsController(ISmsInboundService sms, IConfiguration configuration) : GatewayControllerBase(configuration)
{
    /// <summary>Inbound SMS: SELL &lt;brand&gt; &lt;DEAD|SCREEN|FAULTY&gt; &lt;area&gt; · STATUS &lt;ref&gt; · VERIFY &lt;certificate&gt;. The reply goes out by SMS.</summary>
    [HttpPost("inbound")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<ActionResult<InboundSmsResult>> Inbound([FromForm] InboundSmsRequest request, [FromQuery] string? token, CancellationToken ct)
    {
        if (!IsAuthorizedGateway(token)) return Unauthorized();
        return Ok(await sms.HandleAsync(request, ct));
    }
}
