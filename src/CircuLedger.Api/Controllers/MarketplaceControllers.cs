using CircuLedger.Application.Common;
using CircuLedger.Application.Marketplace;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CircuLedger.Api.Controllers;

[Route("api/v{version:apiVersion}/listings")]
[Authorize]
public sealed class ListingsController(IListingService listings) : ApiControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<ListingDto>> Create(CreateListingRequest request, CancellationToken ct)
    {
        var created = await listings.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id, version = "1" }, created);
    }

    [HttpGet]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<PagedResult<ListingDto>>> Search([FromQuery] ListingSearchQuery query, CancellationToken ct) =>
        Ok(await listings.SearchAsync(query, publicOnly: false, ct));

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<ActionResult<ListingDto>> Get(Guid id, CancellationToken ct) => Ok(await listings.GetAsync(id, publicOnly: false, ct));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<ListingDto>> Update(Guid id, UpdateListingRequest request, CancellationToken ct) =>
        Ok(await listings.UpdateAsync(id, request, ct));

    /// <summary>Uploads picture evidence (JPEG/PNG/WebP, max 5 MB). The uploader and timestamp are recorded.</summary>
    [HttpPost("{id:guid}/photos")]
    [Authorize(Policy = Policies.Staff)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ListingPhotoDto>> UploadPhoto(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return Ok(await listings.UploadPhotoAsync(id, new PhotoUpload(stream, file.FileName, file.ContentType, file.Length), ct));
    }

    /// <summary>Marks a photo as verified. It must be someone other than the uploader (Admin excepted).</summary>
    [HttpPost("{id:guid}/photos/{photoId:guid}/verify")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<ListingPhotoDto>> VerifyPhoto(Guid id, Guid photoId, CancellationToken ct) =>
        Ok(await listings.VerifyPhotoAsync(id, photoId, ct));

    [HttpGet("{id:guid}/photos/{photoId:guid}")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Photo(Guid id, Guid photoId, CancellationToken ct)
    {
        var photo = await listings.GetPhotoAsync(id, photoId, publicOnly: false, ct);
        return File(photo.Content, photo.ContentType);
    }

    /// <summary>Publishes the listing. It needs at least one verified photo.</summary>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<ListingDto>> Publish(Guid id, CancellationToken ct) => Ok(await listings.PublishAsync(id, ct));

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<ListingDto>> Withdraw(Guid id, CancellationToken ct) => Ok(await listings.WithdrawAsync(id, ct));
}

[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController(IOrderService orders) : ApiControllerBase
{
    [HttpPost]
    [Authorize(Policy = Policies.Buyer)]
    public async Task<ActionResult<OrderDto>> Place(PlaceOrderRequest request, CancellationToken ct)
    {
        var order = await orders.PlaceAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id, version = "1" }, order);
    }

    [HttpGet("mine")]
    [Authorize(Policy = Policies.Buyer)]
    public async Task<ActionResult<PagedResult<OrderDto>>> Mine([FromQuery] OrderQuery query, CancellationToken ct) =>
        Ok(await orders.ListAsync(query, mineOnly: true, ct));

    [HttpGet]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<PagedResult<OrderDto>>> List([FromQuery] OrderQuery query, CancellationToken ct) =>
        Ok(await orders.ListAsync(query, mineOnly: false, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderDto>> Get(Guid id, CancellationToken ct) => Ok(await orders.GetAsync(id, ct));

    /// <summary>Confirms payment. In one transaction: marks items sold, credits the funds per the reinvestment rule, and writes ledger entries.</summary>
    [HttpPost("{id:guid}/pay")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<OrderDto>> Pay(Guid id, MarkOrderPaidRequest request, CancellationToken ct) =>
        Ok(await orders.MarkPaidAsync(id, request, ct));

    [HttpPost("{id:guid}/fulfil")]
    [Authorize(Policy = Policies.Operations)]
    public async Task<ActionResult<OrderDto>> Fulfil(Guid id, CancellationToken ct) => Ok(await orders.FulfilAsync(id, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderDto>> Cancel(Guid id, CancelOrderRequest request, CancellationToken ct) =>
        Ok(await orders.CancelAsync(id, request, ct));
}
