using CircuLedger.Application.Common;
using CircuLedger.Application.Common.Interfaces;
using CircuLedger.Domain.Entities;
using CircuLedger.Domain.Enums;
using FluentValidation;
using Mapster;
using Microsoft.EntityFrameworkCore;

namespace CircuLedger.Application.People;

public sealed record TrainingCycleDto(
    Guid Id, string Name, string? Focus, string? Location, DateOnly StartDate, DateOnly? EndDate,
    int EnrolledCount, int CompletedCount, int SafetyCertifiedCount);

public sealed record CreateTrainingCycleRequest(string Name, string? Focus, string? Location, DateOnly StartDate, DateOnly? EndDate);

public sealed record TraineeEnrollmentDto(
    Guid Id, Guid TrainingCycleId, string? UserId, string FullName, Gender Gender, EnrollmentStatus Status,
    bool SafetyTrainingCompleted, DateTime? SafetyTrainingCompletedAt, DateTime? CompletedAt);

public sealed record EnrollTraineeRequest(string FullName, Gender Gender, string? UserId);

public sealed record UpdateEnrollmentRequest(EnrollmentStatus Status, bool SafetyTrainingCompleted);

public sealed record EmploymentRecordDto(
    Guid Id, string? UserId, string FullName, Gender Gender, string JobTitle, EmploymentType EmploymentType,
    DateOnly StartDate, DateOnly? EndDate);

public sealed record CreateEmploymentRecordRequest(
    string FullName, Gender Gender, string JobTitle, EmploymentType EmploymentType, DateOnly StartDate, DateOnly? EndDate, string? UserId);

public sealed record AwarenessCampaignDto(
    Guid Id, string InstitutionName, InstitutionType InstitutionType, CampaignType CampaignType, DateOnly Date,
    int ReachCount, string? Location, string? Notes);

public sealed record CreateAwarenessCampaignRequest(
    string InstitutionName, InstitutionType InstitutionType, CampaignType CampaignType, DateOnly Date, int ReachCount,
    string? Location, string? Notes);

public sealed class CreateTrainingCycleRequestValidator : AbstractValidator<CreateTrainingCycleRequest>
{
    public CreateTrainingCycleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Focus).MaximumLength(200);
        RuleFor(x => x.Location).MaximumLength(120);
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate.HasValue);
    }
}

public sealed class EnrollTraineeRequestValidator : AbstractValidator<EnrollTraineeRequest>
{
    public EnrollTraineeRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Gender).IsInEnum();
    }
}

public sealed class UpdateEnrollmentRequestValidator : AbstractValidator<UpdateEnrollmentRequest>
{
    public UpdateEnrollmentRequestValidator() => RuleFor(x => x.Status).IsInEnum();
}

public sealed class CreateEmploymentRecordRequestValidator : AbstractValidator<CreateEmploymentRecordRequest>
{
    public CreateEmploymentRecordRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.JobTitle).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Gender).IsInEnum();
        RuleFor(x => x.EmploymentType).IsInEnum();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate).When(x => x.EndDate.HasValue);
    }
}

public sealed class CreateAwarenessCampaignRequestValidator : AbstractValidator<CreateAwarenessCampaignRequest>
{
    public CreateAwarenessCampaignRequestValidator()
    {
        RuleFor(x => x.InstitutionName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.InstitutionType).IsInEnum();
        RuleFor(x => x.CampaignType).IsInEnum();
        RuleFor(x => x.ReachCount).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.Location).MaximumLength(120);
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}

public interface ITrainingService
{
    Task<IReadOnlyList<TrainingCycleDto>> ListCyclesAsync(CancellationToken ct = default);
    Task<TrainingCycleDto> CreateCycleAsync(CreateTrainingCycleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TraineeEnrollmentDto>> ListEnrollmentsAsync(Guid cycleId, CancellationToken ct = default);
    Task<TraineeEnrollmentDto> EnrollAsync(Guid cycleId, EnrollTraineeRequest request, CancellationToken ct = default);
    Task<TraineeEnrollmentDto> UpdateEnrollmentAsync(Guid enrollmentId, UpdateEnrollmentRequest request, CancellationToken ct = default);
}

public sealed class TrainingService(IApplicationDbContext db, IUserDirectory users, TimeProvider time) : ITrainingService
{
    public async Task<IReadOnlyList<TrainingCycleDto>> ListCyclesAsync(CancellationToken ct = default)
    {
        var cycles = await db.TrainingCycles.AsNoTracking().Include(c => c.Enrollments)
            .OrderByDescending(c => c.StartDate).ToListAsync(ct);
        return cycles.Select(ToDto).ToList();
    }

    public async Task<TrainingCycleDto> CreateCycleAsync(CreateTrainingCycleRequest request, CancellationToken ct = default)
    {
        var cycle = request.Adapt<TrainingCycle>();
        db.TrainingCycles.Add(cycle);
        await db.SaveChangesAsync(ct);
        return ToDto(cycle);
    }

    public async Task<IReadOnlyList<TraineeEnrollmentDto>> ListEnrollmentsAsync(Guid cycleId, CancellationToken ct = default)
    {
        var list = await db.TraineeEnrollments.AsNoTracking().Where(e => e.TrainingCycleId == cycleId)
            .OrderBy(e => e.FullName).ToListAsync(ct);
        return list.Adapt<List<TraineeEnrollmentDto>>();
    }

    public async Task<TraineeEnrollmentDto> EnrollAsync(Guid cycleId, EnrollTraineeRequest request, CancellationToken ct = default)
    {
        if (!await db.TrainingCycles.AnyAsync(c => c.Id == cycleId, ct)) throw new NotFoundException("TrainingCycle", cycleId);
        if (request.UserId is not null && await users.FindAsync(request.UserId, ct) is null)
            throw new NotFoundException("User", request.UserId);

        var enrollment = request.Adapt<TraineeEnrollment>();
        enrollment.TrainingCycleId = cycleId;
        db.TraineeEnrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        return enrollment.Adapt<TraineeEnrollmentDto>();
    }

    public async Task<TraineeEnrollmentDto> UpdateEnrollmentAsync(Guid enrollmentId, UpdateEnrollmentRequest request, CancellationToken ct = default)
    {
        var e = await db.TraineeEnrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct)
                ?? throw new NotFoundException("TraineeEnrollment", enrollmentId);
        var now = time.GetUtcNow().UtcDateTime;
        if (request.SafetyTrainingCompleted && !e.SafetyTrainingCompleted) e.SafetyTrainingCompletedAt = now;
        if (!request.SafetyTrainingCompleted) e.SafetyTrainingCompletedAt = null;
        e.SafetyTrainingCompleted = request.SafetyTrainingCompleted;
        if (request.Status == EnrollmentStatus.Completed && e.Status != EnrollmentStatus.Completed) e.CompletedAt = now;
        e.Status = request.Status;
        await db.SaveChangesAsync(ct);
        return e.Adapt<TraineeEnrollmentDto>();
    }

    private static TrainingCycleDto ToDto(TrainingCycle c) => new(
        c.Id, c.Name, c.Focus, c.Location, c.StartDate, c.EndDate,
        c.Enrollments.Count(e => !e.IsDeleted),
        c.Enrollments.Count(e => !e.IsDeleted && e.Status == EnrollmentStatus.Completed),
        c.Enrollments.Count(e => !e.IsDeleted && e.SafetyTrainingCompleted));
}

public interface IEmploymentService
{
    Task<IReadOnlyList<EmploymentRecordDto>> ListAsync(CancellationToken ct = default);
    Task<EmploymentRecordDto> CreateAsync(CreateEmploymentRecordRequest request, CancellationToken ct = default);
}

public sealed class EmploymentService(IApplicationDbContext db) : IEmploymentService
{
    public async Task<IReadOnlyList<EmploymentRecordDto>> ListAsync(CancellationToken ct = default) =>
        (await db.EmploymentRecords.AsNoTracking().OrderByDescending(e => e.StartDate).ToListAsync(ct)).Adapt<List<EmploymentRecordDto>>();

    public async Task<EmploymentRecordDto> CreateAsync(CreateEmploymentRecordRequest request, CancellationToken ct = default)
    {
        var record = request.Adapt<EmploymentRecord>();
        db.EmploymentRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return record.Adapt<EmploymentRecordDto>();
    }
}

public interface IAwarenessService
{
    Task<IReadOnlyList<AwarenessCampaignDto>> ListAsync(CancellationToken ct = default);
    Task<AwarenessCampaignDto> CreateAsync(CreateAwarenessCampaignRequest request, CancellationToken ct = default);
}

public sealed class AwarenessService(IApplicationDbContext db) : IAwarenessService
{
    public async Task<IReadOnlyList<AwarenessCampaignDto>> ListAsync(CancellationToken ct = default) =>
        (await db.AwarenessCampaigns.AsNoTracking().OrderByDescending(a => a.Date).ToListAsync(ct)).Adapt<List<AwarenessCampaignDto>>();

    public async Task<AwarenessCampaignDto> CreateAsync(CreateAwarenessCampaignRequest request, CancellationToken ct = default)
    {
        var campaign = request.Adapt<AwarenessCampaign>();
        db.AwarenessCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return campaign.Adapt<AwarenessCampaignDto>();
    }
}
