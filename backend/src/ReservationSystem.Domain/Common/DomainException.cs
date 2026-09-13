namespace ReservationSystem.Domain.Common;

/// <summary>
/// Domain kuralı ihlali. <see cref="Code"/> API katmanında HTTP durum koduna ve
/// kullanıcıya gösterilecek mesaja çevrilir; exception mesajı doğrudan kullanıcıya
/// gösterilmez.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}
