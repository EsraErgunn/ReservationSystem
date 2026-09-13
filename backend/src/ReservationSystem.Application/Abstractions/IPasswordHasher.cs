namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// auth.md §2. Algoritma seçimi (PBKDF2 / Argon2) Infrastructure'ın işi;
/// Application yalnızca "hash'le" ve "doğrula" bilir.
/// </summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}
