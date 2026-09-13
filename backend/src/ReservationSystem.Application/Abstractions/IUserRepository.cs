using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// Dokümanın port listesinde yoktu; ödeme başlatırken sağlayıcıya gönderilecek
/// alıcı adı/e-postası için gerekli (application-katmani.md §6'daki
/// "/* kullanıcıdan */" boşluğu).
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
}
