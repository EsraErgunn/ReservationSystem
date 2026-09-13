using ReservationSystem.Domain.Common;

namespace ReservationSystem.Domain.Entities;

public class Venue : Entity
{
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public string City { get; private set; } = null!;

    private Venue() { }   // EF Core için

    public Venue(string name, string address, string city)
    {
        Name = name;
        Address = address;
        City = city;
    }
}
