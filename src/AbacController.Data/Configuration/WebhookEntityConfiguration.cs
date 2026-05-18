using AbacController.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AbacController.Data.Configuration;

/// <summary>Configures webhook subscription persistence.</summary>
public class WebhookSubscriptionEntityConfiguration : IEntityTypeConfiguration<WebhookSubscriptionEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<WebhookSubscriptionEntity> builder)
    {
        builder.ToTable("WebhookSubscriptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CallbackUrl).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.Secret).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PreviousSecret).HasMaxLength(256);
        builder.Property(e => e.EventTypes).HasMaxLength(1024).IsRequired();
        builder.HasIndex(e => new { e.TenantId, e.CallbackUrl });
        builder.HasIndex(e => e.Active);
    }
}

/// <summary>Configures webhook event outbox persistence.</summary>
public class WebhookEventEntityConfiguration : IEntityTypeConfiguration<WebhookEventEntity>
{
    /// <summary>Configures table, keys, and indexes.</summary>
    public void Configure(EntityTypeBuilder<WebhookEventEntity> builder)
    {
        builder.ToTable("WebhookEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.Property(e => e.LastError).HasMaxLength(2048);
        builder.HasIndex(e => new { e.SubscriptionId, e.Status, e.NextAttemptAt });
        builder.HasIndex(e => new { e.SubscriptionId, e.CreatedAt });
    }
}
