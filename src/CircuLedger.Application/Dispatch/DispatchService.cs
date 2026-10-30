using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using CircuLedger.Domain.Geo;
using CircuLedger.Domain.Services;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CircuLedger.Application.Dispatch;

public sealed record GenerateBatchesRequest(
    double? RadiusKm = null,
    int? MaxPerBatch = null,
    IReadOnlyList<string>? FieldAgentIds = null,
    TransportMode? TransportMode = null,
    DateOnly? ScheduledFor = null);

public sealed record BatchPickupDto(
    Guid IntakeRequestId,
    string ReferenceCode,
    string Brand,
    DeviceCondition Condition,
    string? PickupArea,
    double? Latitude,
    double? Longitude,
    IntakeStatus Status,
    string CitizenPhoneMasked,
    DateTime? CollectedAt);

public sealed record DispatchBatchDto(
    Guid Id,
    string BatchCode,
    string FieldAgentUserId,
    string? FieldAgentName,
    TransportMode TransportMode,
    DispatchBatchStatus Status,
    double CentroidLatitude,
    double CentroidLongitude,
    double RadiusKm,
    double SpreadKm,
    DateOnly ScheduledFor,
    IReadOnlyList<BatchPickupDto> Pickups);

public sealed record GenerateBatchesResult(IReadOnlyList<DispatchBatchDto> Batches, int RequestsBatched, int UnlocatedRequests);

/// <summary>Full phone number, revealed only to the assigned agent so they can call the seller.</summary>
public sealed record PickupContactDto(Guid IntakeRequestId, string ReferenceCode, string Phone, string? PickupArea);

public sealed record DispatchBatchQuery : PageQuery
{
    public DispatchBatchStatus? Status { get; init; }
    public string? FieldAgentUserId { get; init; }
}

public sealed class GenerateBatchesRequestValidator : AbstractValidator<GenerateBatchesRequest>
{
    public GenerateBatchesRequestValidator()
    {
        RuleFor(x => x.RadiusKm).InclusiveBetween(0.1, 50).When(x => x.RadiusKm.HasValue);
        RuleFor(x => x.MaxPerBatch).InclusiveBetween(1, 50).When(x => x.MaxPerBatch.HasValue);
        RuleFor(x => x.TransportMode).IsInEnum().When(x => x.TransportMode.HasValue);
    }
}

public interface IDispatchService
{
    Task<GenerateBatchesResult> GenerateAsync(GenerateBatchesRequest request, CancellationToken ct = default);
    Task<PagedResult<DispatchBatchDto>> ListAsync(DispatchBatchQuery query, CancellationToken ct = default);
    Task<DispatchBatchDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DispatchBatchDto>> GetAgentBatchesAsync(string agentUserId, CancellationToken ct = default);
    Task<DispatchBatchDto> MarkCollectedAsync(Guid batchId, Guid intakeRequestId, CancellationToken ct = default);
    Task<PickupContactDto> GetPickupContactAsync(Guid batchId, Guid intakeRequestId, CancellationToken ct = default);
}

public sealed class DispatchService(
    IApplicationDbContext db,
    IUserDirectory users,
    IPersonalDataProtector protector,
    ICurrentUser currentUser,
    CitizenNotifier notifier,
    IOptions<DispatchOptions> options,
    TimeProvider time,
    ILogger<DispatchService> logger) : IDispatchService
{
    public async Task<GenerateBatchesResult> GenerateAsync(GenerateBatchesRequest request, CancellationToken ct = default)
    {
        var radius = request.RadiusKm ?? options.Value.DefaultRadiusKm;
        var maxPerBatch = request.MaxPerBatch ?? options.Value.MaxRequestsPerBatch;
        var now = time.GetUtcNow().UtcDateTime;
        var scheduledFor = request.ScheduledFor ?? DateOnly.FromDateTime(now).AddDays(1);

        var agents = await ResolveAgentsAsync(request.FieldAgentIds, ct);

        var (batches, unlocated) = await db.ExecuteInTransactionAsync(async t =>
        {
            var pending = await db.IntakeRequests
                .Include(r => r.Citizen)
                .Where(r => r.Status == IntakeStatus.Pending
                            && r.FulfilmentMethod == FulfilmentMethod.Pickup
                            && r.DispatchBatchId == null)
                .ToListAsync(t);

            var located = pending.Where(r => r.HasPickupCoordinates).ToList();
            var proposals = DispatchBatcher.Group(
                located.Select(r => new BatchCandidate(r.Id, new GeoPoint(r.PickupLatitude!.Value, r.PickupLongitude!.Value), r.CreatedAt)),
                radius, maxPerBatch);

            // Least-loaded agents first, then round-robin.
            var openCounts = await db.DispatchBatches
                .Where(b => b.Status == DispatchBatchStatus.Planned || b.Status == DispatchBatchStatus.InProgress)
                .GroupBy(b => b.FieldAgentUserId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, t);
            var agentQueue = agents.OrderBy(a => openCounts.GetValueOrDefault(a.Id)).ThenBy(a => a.FullName).ToList();

            var byId = located.ToDictionary(r => r.Id);
            var created = new List<DispatchBatch>();
            for (var i = 0; i < proposals.Count; i++)
            {
                var proposal = proposals[i];
                var agent = agentQueue[i % agentQueue.Count];
                var batch = new DispatchBatch
                {
                    BatchCode = CodeGenerator.BatchCode(scheduledFor, await db.NextSequenceValueAsync(Sequences.DispatchBatch, t)),
                    FieldAgentUserId = agent.Id,
                    TransportMode = request.TransportMode ?? proposal.SuggestedTransport,
                    CentroidLatitude = proposal.Centroid.Latitude,
                    CentroidLongitude = proposal.Centroid.Longitude,
                    RadiusKm = radius,
                    SpreadKm = proposal.SpreadKm,
                    ScheduledFor = scheduledFor
                };
                db.DispatchBatches.Add(batch);
                foreach (var requestId in proposal.RequestIds)
                {
                    var intake = byId[requestId];
                    intake.Schedule(batch.Id, now);
                    batch.Requests.Add(intake);
                }
                created.Add(batch);
            }

            await db.SaveChangesAsync(t);
            return (created, pending.Count - located.Count);
        }, ct);

        var names = agents.ToDictionary(a => a.Id, a => a.FullName);
        foreach (var batch in batches)
        foreach (var intake in batch.Requests)
        {
            await notifier.NotifyAsync(intake.Citizen!,
                $"CircuHub: pickup for ref {intake.ReferenceCode} is scheduled for {batch.ScheduledFor:ddd d MMM}. Agent {names[batch.FieldAgentUserId]} will call you.", ct);
        }

        logger.LogInformation("Generated {BatchCount} dispatch batches ({Unlocated} pickups without coordinates)", batches.Count, unlocated);
        return new GenerateBatchesResult(
            batches.Select(b => ToDto(b, names.GetValueOrDefault(b.FieldAgentUserId))).ToList(),
            batches.Sum(b => b.Requests.Count),
            unlocated);
    }

    public async Task<PagedResult<DispatchBatchDto>> ListAsync(DispatchBatchQuery query, CancellationToken ct = default)
    {
        var q = Query().AsNoTracking();
        if (query.Status is { } status) q = q.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(query.FieldAgentUserId)) q = q.Where(b => b.FieldAgentUserId == query.FieldAgentUserId);
        var page = await q.OrderByDescending(b => b.ScheduledFor).ThenBy(b => b.BatchCode).ToPagedAsync(query, b => b, ct);
        var names = await users.GetDisplayNamesAsync(page.Items.Select(b => b.FieldAgentUserId), ct);
        return new PagedResult<DispatchBatchDto>(
            page.Items.Select(b => ToDto(b, names.GetValueOrDefault(b.FieldAgentUserId))).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<DispatchBatchDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await LoadAsync(id, ct);
        EnsureCanAccess(batch);
        var names = await users.GetDisplayNamesAsync([batch.FieldAgentUserId], ct);
        return ToDto(batch, names.GetValueOrDefault(batch.FieldAgentUserId));
    }

    public async Task<IReadOnlyList<DispatchBatchDto>> GetAgentBatchesAsync(string agentUserId, CancellationToken ct = default)
    {
        var batches = await Query().AsNoTracking()
            .Where(b => b.FieldAgentUserId == agentUserId
                        && (b.Status == DispatchBatchStatus.Planned || b.Status == DispatchBatchStatus.InProgress))
            .OrderBy(b => b.ScheduledFor)
            .ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync([agentUserId], ct);
        return batches.Select(b => ToDto(b, names.GetValueOrDefault(agentUserId))).ToList();
    }

    public async Task<DispatchBatchDto> MarkCollectedAsync(Guid batchId, Guid intakeRequestId, CancellationToken ct = default)
    {
        var batch = await LoadAsync(batchId, ct);
        EnsureCanAccess(batch);
        var intake = batch.Requests.FirstOrDefault(r => r.Id == intakeRequestId)
                     ?? throw new NotFoundException("Pickup", intakeRequestId);

        var now = time.GetUtcNow().UtcDateTime;
        intake.MarkCollected(currentUser.Actor, now);
        batch.Status = batch.Requests.All(r => r.Status is IntakeStatus.Collected or IntakeStatus.Cancelled or IntakeStatus.Rejected)
            ? DispatchBatchStatus.Completed
            : DispatchBatchStatus.InProgress;
        if (batch.Status == DispatchBatchStatus.Completed) batch.CompletedAt = now;
        await db.SaveChangesAsync(ct);

        await notifier.NotifyAsync(intake.Citizen!, $"CircuHub: your phone (ref {intake.ReferenceCode}) has been collected. We'll SMS when it reaches the hub.", ct);
        var names = await users.GetDisplayNamesAsync([batch.FieldAgentUserId], ct);
        return ToDto(batch, names.GetValueOrDefault(batch.FieldAgentUserId));
    }

    public async Task<PickupContactDto> GetPickupContactAsync(Guid batchId, Guid intakeRequestId, CancellationToken ct = default)
    {
        var batch = await LoadAsync(batchId, ct);
        EnsureCanAccess(batch);
        var intake = batch.Requests.FirstOrDefault(r => r.Id == intakeRequestId)
                     ?? throw new NotFoundException("Pickup", intakeRequestId);
        if (intake.Status != IntakeStatus.Scheduled)
            throw new ForbiddenAccessException("Contact details are only available while a pickup is scheduled.");

        logger.LogInformation("Pickup contact for {IntakeRequestId} revealed to {UserId}", intake.Id, currentUser.UserId);
        return new PickupContactDto(intake.Id, intake.ReferenceCode, protector.DecryptPhone(intake.Citizen!.PhoneEncrypted), intake.PickupArea);
    }

    private async Task<IReadOnlyList<UserSummary>> ResolveAgentsAsync(IReadOnlyList<string>? requestedIds, CancellationToken ct)
    {
        var all = await users.GetUsersInRoleAsync(Roles.FieldAgent, ct);
        var agents = requestedIds is { Count: > 0 } ? all.Where(a => requestedIds.Contains(a.Id)).ToList() : all.ToList();
        if (requestedIds is { Count: > 0 } && agents.Count != requestedIds.Distinct().Count())
            throw new FluentValidation.ValidationException("One or more FieldAgentIds are not active field agents.");
        if (agents.Count == 0)
            throw new ConflictException("No active field agents are available for dispatch.", "no_field_agents");
        return agents;
    }

    private void EnsureCanAccess(DispatchBatch batch)
    {
        var isOps = currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.OperationsLead);
        if (!isOps && batch.FieldAgentUserId != currentUser.UserId)
            throw new ForbiddenAccessException("This batch is assigned to another agent.");
    }

    private IQueryable<DispatchBatch> Query() => db.DispatchBatches.Include(b => b.Requests).ThenInclude(r => r.Citizen);

    private async Task<DispatchBatch> LoadAsync(Guid id, CancellationToken ct) =>
        await Query().FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("DispatchBatch", id);

    private static DispatchBatchDto ToDto(DispatchBatch b, string? agentName) => new(
        b.Id, b.BatchCode, b.FieldAgentUserId, agentName, b.TransportMode, b.Status, b.CentroidLatitude, b.CentroidLongitude,
        b.RadiusKm, b.SpreadKm, b.ScheduledFor,
        b.Requests.OrderBy(r => r.ReferenceCode).Select(r => new BatchPickupDto(
            r.Id, r.ReferenceCode, r.Brand, r.ReportedCondition, r.PickupArea, r.PickupLatitude, r.PickupLongitude,
            r.Status, r.Citizen?.MaskedPhone ?? "hidden", r.CollectedAt)).ToList());
}
