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
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("venue.invalid_name", "Mekân adı boş olamaz.");

        Name = name.Trim();
        Address = address.Trim();
        City = city.Trim();
    }

    /// <summary>
    /// FR-12: sıra etiketi ve koltuk sayısından fiziksel koltuk düzenini üretir.
    /// Koltuklar aggregate'in parçası değil (ayrı tablo, etkinlikten bağımsız);
    /// bu yüzden koleksiyon tutulmaz, üretilen liste çağırana döner.
    /// </summary>
    public IReadOnlyList<Seat> CreateLayout(IReadOnlyList<(string RowLabel, int SeatCount)> rows)
    {
        if (rows.Count == 0)
            throw new DomainException("venue.empty_layout", "En az bir sıra tanımlanmalı.");

        var labels = rows.Select(r => r.RowLabel.Trim().ToUpperInvariant()).ToList();
        if (labels.Any(string.IsNullOrWhiteSpace))
            throw new DomainException("venue.invalid_row", "Sıra etiketi boş olamaz.");

        if (labels.Distinct().Count() != labels.Count)
            throw new DomainException("venue.duplicate_row", "Aynı sıra etiketi birden fazla kez tanımlanamaz.");

        if (rows.Any(r => r.SeatCount <= 0))
            throw new DomainException("venue.invalid_row", "Her sırada en az bir koltuk olmalı.");

        return rows
            .SelectMany((r, i) => Enumerable.Range(1, r.SeatCount)
                .Select(n => new Seat(Id, labels[i], n)))
            .ToList();
    }
}
