using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradeFlow.Domain.Common;
using TradeFlow.Domain.Inventory;

namespace TradeFlow.Infrastructure.Modules.Inventory.Persistence.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", "inventory");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, val => new InventoryItemId(val))
            .ValueGeneratedNever();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .HasConversion(id => id.Value, val => new ProductId(val))
            .IsRequired();

        builder.Property(x => x.Sku)
            .HasColumnName("sku")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.QuantityOnHand)
            .HasColumnName("quantity_on_hand")
            .IsRequired();

        builder.Property(x => x.AllocatedQuantity)
            .HasColumnName("allocated_quantity")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at");

        // Concurrency token for Optimistic Concurrency Control
        builder.Property(x => x.Version)
            .IsRowVersion();

        // Ignored computed/in-memory domain properties
        builder.Ignore(x => x.AvailableQuantity);
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.ProductId).IsUnique();
        builder.HasIndex(x => x.Sku).IsUnique();
    }
}
