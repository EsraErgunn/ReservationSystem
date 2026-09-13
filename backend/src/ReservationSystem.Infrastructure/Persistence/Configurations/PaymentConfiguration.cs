using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", t =>
        {
            t.HasCheckConstraint("ck_payments_status",
                "status IN ('Pending', 'Succeeded', 'Failed', 'Abandoned')");
            t.HasCheckConstraint("ck_payments_amount", "amount > 0");
            // Pending ise completed_at boş, değilse dolu olmalı
            t.HasCheckConstraint("ck_payments_completed",
                "(status = 'Pending') = (completed_at IS NULL)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProviderToken).HasMaxLength(256);
        builder.Property(x => x.ProviderPaymentId).HasMaxLength(128);
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.Property(x => x.CreatedAt).IsRequired();

        // BR-12: aynı sağlayıcı token'ı iki kez işlenemez
        builder.HasIndex(x => x.ProviderToken)
            .IsUnique()
            .HasFilter("provider_token IS NOT NULL")
            .HasDatabaseName("ux_payments_provider_token");

        // C maddesi: bir rezervasyonun aynı anda tek Pending ödemesi olabilir.
        // Yeni ödeme başlatmadan önce mevcut Pending kaydın Abandoned yapılmasını
        // veritabanı seviyesinde zorunlu kılar (Reservation.StartPayment bunu yapıyor).
        builder.HasIndex(x => x.ReservationId)
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_payments_single_pending");

        // Şemada yok — BR-12'yi sertleştirmek için eklendi: aynı sağlayıcı işlem
        // kimliği iki ödeme kaydına yazılamaz.
        builder.HasIndex(x => x.ProviderPaymentId)
            .IsUnique()
            .HasFilter("provider_payment_id IS NOT NULL")
            .HasDatabaseName("ux_payments_provider_payment_id");

        builder.HasIndex(x => new { x.ReservationId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_payments_reservation");
    }
}
