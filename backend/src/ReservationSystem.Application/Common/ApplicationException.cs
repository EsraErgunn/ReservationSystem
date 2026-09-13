namespace ReservationSystem.Application.Common;

/// <summary>
/// Uygulama seviyesi hata. <see cref="Code"/> Api katmanında HTTP durum koduna
/// eşlenir (bkz. application-katmani.md §10).
/// </summary>
public abstract class AppException(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>404</summary>
public class NotFoundAppException(string resource, object key)
    : AppException("not_found", $"{resource} bulunamadı: {key}");

/// <summary>409</summary>
public class ConflictAppException(string code, string message)
    : AppException(code, message);

/// <summary>403</summary>
public class ForbiddenAppException()
    : AppException("forbidden", "Bu işlem için yetkiniz yok.");

/// <summary>401</summary>
public class UnauthorizedAppException()
    : AppException("unauthorized", "Giriş yapmanız gerekiyor.");

/// <summary>422</summary>
public class PaymentAppException(string message)
    : AppException("payment_error", message);

/// <summary>
/// 409. Infrastructure, <c>DbUpdateConcurrencyException</c>'ı buna çevirir — böylece
/// EF Core tipi Application'a sızmaz (§5/A'da seçilen 1. seçenek).
/// </summary>
public class ConcurrencyConflictException(string message, Exception? inner = null)
    : AppException("concurrency_conflict", message, inner);

/// <summary>
/// 409. Infrastructure, PostgreSQL <c>23505 unique_violation</c>'ı buna çevirir.
/// <see cref="ConstraintName"/> hangi savunma hattının devreye girdiğini söyler
/// (ör. <c>ux_reservation_items_active_seat</c>).
/// </summary>
public class UniqueConstraintException(string? constraintName, Exception? inner = null)
    : AppException("unique_violation", $"Benzersizlik kısıtı ihlal edildi: {constraintName}", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
