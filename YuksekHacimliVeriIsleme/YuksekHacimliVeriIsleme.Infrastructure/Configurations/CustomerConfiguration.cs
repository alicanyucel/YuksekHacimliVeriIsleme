using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using YuksekHacimliVeriIsleme.Domain.Entities;

namespace YuksekHacimliVeriIsleme.Infrastructure.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.CustomerId).HasMaxLength(100).IsRequired();
        builder.Property(customer => customer.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(customer => customer.LastName).HasMaxLength(100).IsRequired();
        builder.Property(customer => customer.NationalId).HasMaxLength(11).IsRequired();
        builder.Property(customer => customer.Email).HasMaxLength(254).IsRequired();
        builder.Property(customer => customer.Phone).HasMaxLength(30).IsRequired();
        builder.HasIndex(customer => customer.CustomerId).IsUnique();
        builder.HasIndex(customer => customer.NationalId).IsUnique();
    }
}
