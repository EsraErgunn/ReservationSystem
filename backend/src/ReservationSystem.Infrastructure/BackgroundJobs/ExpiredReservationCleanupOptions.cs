namespace ReservationSystem.Infrastructure.BackgroundJobs;

public class ExpiredReservationCleanupOptions
{
    public const string SectionName = "ExpiredReservationCleanup";

    /// <summary>Küçük batch + sık tur: doğruluğu hıza tercih eden takas.</summary>
    public int BatchSize { get; set; } = 100;

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(5);
}
