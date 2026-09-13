using FluentValidation;

namespace ReservationSystem.Application.Auth;

/// <summary>
/// auth.md §8. Parola politikası Domain'e değil Application'a ait: "en az 8 karakter"
/// bir iş kuralı değil, giriş kısıtı. Domain <c>User</c> zaten hash alıyor, ham
/// parolayı hiç görmüyor.
/// </summary>
/// <remarks>
/// Karmaşıklık kuralı (büyük harf + rakam + sembol) bilinçli olarak yok: NIST
/// SP 800-63B bu kuralların kullanıcıları tahmin edilebilir kalıplara ittiğini
/// (<c>Parola123!</c>) ve uzunluğun daha etkili olduğunu belirtiyor. Üst sınır 128,
/// hash fonksiyonuna aşırı uzun girdiyle DoS yapılmasını engellemek için.
/// </remarks>
public class RegisterValidator : AbstractValidator<RegisterCommand>
{
    public RegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Parola en az 8 karakter olmalı.")
            .MaximumLength(128);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
