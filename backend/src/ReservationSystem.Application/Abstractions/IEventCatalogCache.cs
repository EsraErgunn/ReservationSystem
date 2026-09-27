namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// NFR-02: etkinlik listesi sık okunan, nadiren değişen veri — Infrastructure'da
/// Redis'te cache'leniyor. Application yalnızca "katalog değişti" demeyi bilir;
/// cache'in nerede ve nasıl tutulduğu onun işi değil.
/// </summary>
public interface IEventCatalogCache
{
    Task InvalidateAsync(CancellationToken ct);
}
