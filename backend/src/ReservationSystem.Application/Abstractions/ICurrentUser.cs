namespace ReservationSystem.Application.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAdmin { get; }
}
