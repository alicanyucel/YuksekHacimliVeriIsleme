namespace YuksekHacimliVeriIsleme.Infrastructure.Options;

public sealed class CustomerImportOptions
{
    public const string SectionName = "CustomerImport";
    public long MaxFileSizeBytes { get; set; } = 1_073_741_824;
    public int BatchSize { get; set; } = 1000;
    public int MaxConcurrentJobs { get; set; } = 2;
    public string UploadDirectory { get; set; } = "App_Data/CustomerImports";
}
