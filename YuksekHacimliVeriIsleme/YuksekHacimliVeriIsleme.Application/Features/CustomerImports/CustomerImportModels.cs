using System.Text.Json.Serialization;
using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Application.Features.CustomerImports;

public sealed record CustomerImportJobDto(
    Guid Id,
    string TrackingNumber,
    string FileName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] CustomerImportStatus Status,
    long TotalRows,
    long ProcessedRows,
    long SuccessCount,
    long ErrorCount,
    long DuplicateCount,
    DateTime UploadedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason)
{
    public double ProgressPercentage => TotalRows == 0
        ? Status is CustomerImportStatus.Completed or CustomerImportStatus.Failed or CustomerImportStatus.PartiallyCompleted ? 100 : 0
        : Math.Round((double)ProcessedRows / TotalRows * 100, 2);
}

public sealed record CustomerImportErrorDto(long RowNumber, string RawData, string ErrorReason);

public sealed record CustomerImportPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);
