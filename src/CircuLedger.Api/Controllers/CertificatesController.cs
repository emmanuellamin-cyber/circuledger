using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/certificates")]
[Authorize(Policy = Policies.Staff)]
public sealed class CertificatesController(ICertificateService certificates) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<CertificateDto>>> List([FromQuery] PageQuery query, CancellationToken ct) =>
        Ok(await certificates.ListAsync(query, ct));

    [HttpGet("{number}")]
    public async Task<ActionResult<CertificateDto>> Get(string number, CancellationToken ct) => Ok(await certificates.GetAsync(number, ct));

    /// <summary>Printable PDF with a QR code that links to public verification.</summary>
    [HttpGet("{number}/pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> Pdf(string number, CancellationToken ct)
    {
        var file = await certificates.GetPdfAsync(number, ct);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{number}/qr")]
    [Produces("image/png")]
    public async Task<IActionResult> Qr(string number, CancellationToken ct)
    {
        var file = await certificates.GetQrCodeAsync(number, ct);
        return File(file.Content, file.ContentType);
    }

    [HttpPost("{number}/revoke")]
    [Authorize(Policy = Policies.AdminOnly)]
    public async Task<ActionResult<CertificateDto>> Revoke(string number, RevokeCertificateRequest request, CancellationToken ct) =>
        Ok(await certificates.RevokeAsync(number, request, ct));
}
