using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Infrastructure.Configurations;

internal sealed class CustomerImportErrorConfiguration : IEntityTypeConfiguration<CustomerImportError>
{
    public void Configure(EntityTypeBuilder<CustomerImportError> builder)
    {
        builder.ToTable("CustomerImportErrors");
        builder.HasKey(error => error.Id);
        builder.Property(error => error.RawData).HasMaxLength(4000).IsRequired();
        builder.Property(error => error.ErrorReason).HasMaxLength(2000).IsRequired();
        builder.HasIndex(error => new { error.CustomerImportJobId, error.RowNumber });
        builder.HasOne(error => error.CustomerImportJob)
            .WithMany(job => job.Errors)
            .HasForeignKey(error => error.CustomerImportJobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
