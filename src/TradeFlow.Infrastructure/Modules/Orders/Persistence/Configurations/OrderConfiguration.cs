using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", "orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, val => new OrderId(val))
            .ValueGeneratedNever();

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, val => new CustomerId(val))
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(x => x.SubmittedAt)
            .HasColumnName("submitted_at");

        builder.Property(x => x.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.Property(x => x.CancellationReason)
            .HasColumnName("cancellation_reason")
            .HasMaxLength(500);

        // Concurrency token mapped to PostgreSQL xmin system column
        builder.Property(x => x.Version)
            .IsRowVersion();

        // Optional MinimumOrderValue mapped as complex type
        builder.ComplexProperty(x => x.MinimumOrderValue, moneyBuilder =>
        {
            moneyBuilder.Property(m => m.Amount)
                .HasColumnName("minimum_order_value_amount")
                .HasPrecision(18, 2);

            moneyBuilder.Property(m => m.Currency)
                .HasColumnName("minimum_order_value_currency")
                .HasMaxLength(3);
        });

        // Computed domain properties ignored by EF Core
        builder.Ignore(x => x.TotalAmount);
        builder.Ignore(x => x.DomainEvents);

        // Encapsulated 1-to-many relationship using private backing field (ADR-0004)
        builder.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey("order_id")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
