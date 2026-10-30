using CircuLedger.Domain.Common;
using CircuLedger.Domain.Enums;

namespace CircuLedger.Domain.Entities;

public class TrainingCycle : AuditableEntity
{
    public required string Name { get; set; }
    public string? Focus { get; set; }
    public string? Location { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public ICollection<TraineeEnrollment> Enrollments { get; set; } = [];
}

public class TraineeEnrollment : AuditableEntity
{
    public Guid TrainingCycleId { get; set; }
    public TrainingCycle? TrainingCycle { get; set; }

    /// <summary>Set when the trainee has (or later gets) a system account, for example as a Technician.</summary>
    public string? UserId { get; set; }
    public required string FullName { get; set; }
    public Gender Gender { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Enrolled;
    public bool SafetyTrainingCompleted { get; set; }
    public DateTime? SafetyTrainingCompletedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class EmploymentRecord : AuditableEntity
{
    public string? UserId { get; set; }
    public required string FullName { get; set; }
    public Gender Gender { get; set; }
    public required string JobTitle { get; set; }
    public EmploymentType EmploymentType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
}

public class AwarenessCampaign : AuditableEntity
{
    public required string InstitutionName { get; set; }
    public InstitutionType InstitutionType { get; set; }
    public CampaignType CampaignType { get; set; }
    public DateOnly Date { get; set; }
    public int ReachCount { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}
