namespace ReservationSystem.Infrastructure.BackgroundJobs;

public class PendingPaymentReconciliationOptions
{
    public const string SectionName = "PendingPaymentReconciliation";

    /// <summary>Temizlik görevinden seyrek: her tur sağlayıcıya ağ isteği demek.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Bu yaştan genç Pending ödemeye dokunulmaz — kullanıcı hâlâ sağlayıcının
    /// formunu dolduruyor olabilir, normal akışı bozmayalım.
    /// </summary>
    public TimeSpan MinimumAge { get; set; } = TimeSpan.FromMinutes(2);

    public int BatchSize { get; set; } = 50;
}
