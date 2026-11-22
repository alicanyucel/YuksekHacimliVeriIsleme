using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Infrastructure.Configurations;

internal sealed class CustomerImportJobConfiguration : IEntityTypeConfiguration<CustomerImportJob>
{
    public void Configure(EntityTypeBuilder<CustomerImportJob> builder)
    {
        builder.ToTable("CustomerImportJobs");
        builder.HasKey(job => job.Id);
        builder.Property(job => job.TrackingNumber).HasMaxLength(64).IsRequired();
        builder.Property(job => job.FileName).HasMaxLength(255).IsRequired();
        builder.Property(job => job.FilePath).HasMaxLength(1024).IsRequired();
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.FailureReason).HasMaxLength(2000);
        builder.HasIndex(job => job.TrackingNumber).IsUnique();
        builder.HasIndex(job => job.UploadedAt);
    }
}
