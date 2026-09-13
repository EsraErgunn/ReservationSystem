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

    /// <summary>
    /// auth.md §3. Kullanıcıya anlamlı mesaj vermek için; benzersizlik *garantisi*
    /// <c>ux_users_email</c> index'inden geliyor.
    /// </summary>
    Task<bool> ExistsByEmailAsync(string email, CancellationToken ct);

    /// <summary>E-posta normalize edilmiş (küçük harf) olarak beklenir.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);

    void Add(User user);
}
