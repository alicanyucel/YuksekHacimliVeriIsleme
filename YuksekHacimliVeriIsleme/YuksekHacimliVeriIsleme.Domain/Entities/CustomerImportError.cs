using YuksekHacimliVeriIsleme.Domain.Abstractions;

namespace YuksekHacimliVeriIsleme.Domain.Entities;

public sealed class CustomerImportError : Entity
{
    public Guid CustomerImportJobId { get; set; }
    public CustomerImportJob CustomerImportJob { get; set; } = null!;
    public long RowNumber { get; set; }
    public string RawData { get; set; } = string.Empty;
    public string ErrorReason { get; set; } = string.Empty;
}
