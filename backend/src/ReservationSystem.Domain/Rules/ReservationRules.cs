namespace ReservationSystem.Domain.Rules;

public static class ReservationRules
{
    /// <summary>BR-01: Hold süresi.</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(10);

    /// <summary>BR-03: Tek rezervasyonda maksimum koltuk.</summary>
    public const int MaxSeatsPerReservation = 6;
}
