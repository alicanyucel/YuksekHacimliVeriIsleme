using YuksekHacimliVeriIsleme.Domain.Abstractions;

namespace YuksekHacimliVeriIsleme.Domain.Entities;

public enum CustomerImportStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    PartiallyCompleted
}

public sealed class CustomerImportJob : Entity
{
    public string TrackingNumber { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public CustomerImportStatus Status { get; set; } = CustomerImportStatus.Pending;
    public long TotalRows { get; set; }
    public long ProcessedRows { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public long DuplicateCount { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public ICollection<CustomerImportError> Errors { get; set; } = new List<CustomerImportError>();
}
