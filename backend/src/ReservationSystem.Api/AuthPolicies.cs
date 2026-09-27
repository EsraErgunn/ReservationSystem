namespace ReservationSystem.Api;

public static class AuthPolicies
{
    /// <summary>BR-16: etkinlik ve mekân tanımlama yalnızca admin rolüne açık.</summary>
    public const string Admin = "Admin";
}
