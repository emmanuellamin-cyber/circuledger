using CircuLedger.Application.Certificates;
using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Application.DropOffPoints;
using CircuLedger.Application.Marketplace;
using CircuLedger.Application.Purchasing;
using CircuLedger.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CircuLedger.Api.Controllers;

/// <summary>Anonymous endpoints. They never return personal data.</summary>
[Route("api/v{version:apiVersion}/public")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class PublicController(
    ICertificateService certificates,
    ICertificateSigner signer,
    IDropOffPointService points,
    IListingService listings,
    IPricingService pricing) : ApiControllerBase
{
    /// <summary>Verifies a Certificate of Data Sanitization: hash, RSA signature and revocation status.</summary>
    [HttpGet("certificates/{number}/verify")]
    public async Task<ActionResult<PublicCertificateVerificationDto>> VerifyCertificate(string number, CancellationToken ct)
    {
        var result = await certificates.VerifyPublicAsync(number, ct);
        return result.Status == "NotFound" ? NotFound(result) : Ok(result);
    }

    /// <summary>The PEM public key used to sign certificates, for offline verification.</summary>
    [HttpGet("certificates/signing-key")]
    public ActionResult<object> SigningKey() =>
        Ok(new { keyId = signer.KeyId, algorithm = "RS256", publicKeyPem = signer.PublicKeyPem });

    [HttpGet("drop-off-points")]
    public async Task<ActionResult<IReadOnlyList<DropOffPointDto>>> DropOffPoints(CancellationToken ct) =>
        Ok(await points.ListAsync(includeInactive: false, ct));

    [HttpGet("pricing/quote")]
    public async Task<ActionResult<PriceQuoteDto>> Quote([FromQuery] string brand, [FromQuery] DeviceCondition condition, CancellationToken ct) =>
        Ok(await pricing.QuoteAsync(brand, condition, ct));

    /// <summary>Browse, search and filter published listings (brand, type, price range, Samsung only), with pagination.</summary>
    [HttpGet("listings")]
    public async Task<ActionResult<PagedResult<ListingDto>>> Listings([FromQuery] ListingSearchQuery query, CancellationToken ct) =>
        Ok(await listings.SearchAsync(query, publicOnly: true, ct));

    [HttpGet("listings/{id:guid}")]
    public async Task<ActionResult<ListingDto>> Listing(Guid id, CancellationToken ct) =>
        Ok(await listings.GetAsync(id, publicOnly: true, ct));

    [HttpGet("listings/{id:guid}/photos/{photoId:guid}")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await listings.GetPhotoAsync(id, photoId, publicOnly: true, ct);
        return File(photo.Content, photo.ContentType);
    }
}
