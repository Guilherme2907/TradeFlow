using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Orders;

namespace TradeFlow.Infrastructure.Modules.Orders.Persistence.Configurations;

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    private readonly bool _isInMemory;

    public OrderLineConfiguration(bool isInMemory = false)
    {
        _isInMemory = isInMemory;
    }

    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", "orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, val => new OrderLineId(val))
            .ValueGeneratedNever();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .HasConversion(id => id.Value, val => new ProductId(val))
            .IsRequired();

        builder.Property(x => x.Sku)
            .HasColumnName("sku")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ProductName)
            .HasColumnName("product_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        // Map Money Value Object using EF Core ComplexProperty on relational databases (ADR-0004).
        // EF Core 10's InMemory provider does not support ComplexProperty in Include() shapers.
        if (_isInMemory)
        {
            builder.Property(x => x.UnitPrice)
                .HasConversion(
                    m => $"{m.Amount.ToString(CultureInfo.InvariantCulture)}|{m.Currency}",
                    raw => ParseMoney(raw))
                .IsRequired();
        }
        else
        {
            builder.ComplexProperty(x => x.UnitPrice, moneyBuilder =>
            {
                moneyBuilder.Property(m => m.Amount)
                    .HasColumnName("unit_price_amount")
                    .HasPrecision(18, 2)
                    .IsRequired();

                moneyBuilder.Property(m => m.Currency)
                    .HasColumnName("unit_price_currency")
                    .HasMaxLength(3)
                    .IsRequired();
            });
        }

        // LineTotal is a computed domain property (UnitPrice * Quantity), not persisted
        builder.Ignore(x => x.LineTotal);
    }

    private static Money ParseMoney(string raw)
    {
        var parts = raw.Split('|');
        return new Money(decimal.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }
}
