using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Domain.Customers;

namespace TradeFlow.Infrastructure.Modules.Customers.Persistence.Configurations;

public sealed class CustomerAccountConfiguration : IEntityTypeConfiguration<CustomerAccount>
{
    public void Configure(EntityTypeBuilder<CustomerAccount> builder)
    {
        builder.ToTable("customer_accounts", "customers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, val => new CustomerAccountId(val))
            .ValueGeneratedNever();

        builder.Property(x => x.CompanyName)
            .HasColumnName("company_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TaxId)
            .HasColumnName("tax_id")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Tier)
            .HasColumnName("tier")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.CreditLimit)
            .HasColumnName("credit_limit")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.PaymentTermsDays)
            .HasColumnName("payment_terms_days")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Ignore(x => x.DomainEvents);

        builder.HasMany(x => x.Contracts)
            .WithOne()
            .HasForeignKey(c => c.CustomerAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Contracts)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(x => x.TaxId).IsUnique();
    }
}

public sealed class CustomerContractConfiguration : IEntityTypeConfiguration<CustomerContract>
{
    public void Configure(EntityTypeBuilder<CustomerContract> builder)
    {
        builder.ToTable("customer_contracts", "customers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, val => new CustomerContractId(val))
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerAccountId)
            .HasColumnName("customer_account_id")
            .HasConversion(id => id.Value, val => new CustomerAccountId(val))
            .IsRequired();

        builder.Property(x => x.ContractNumber)
            .HasColumnName("contract_number")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TermsText)
            .HasColumnName("terms_text")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.DiscountPercent)
            .HasColumnName("discount_percent")
            .HasPrecision(5, 2)
            .IsRequired();

        builder.Property(x => x.EffectiveFrom)
            .HasColumnName("effective_from")
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.HasIndex(x => new { x.CustomerAccountId, x.ContractNumber }).IsUnique();
    }
}
