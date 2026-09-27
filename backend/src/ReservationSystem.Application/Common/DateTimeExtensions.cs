namespace ReservationSystem.Application.Common;

public static class DateTimeExtensions
{
    /// <summary>
    /// İstemciden gelen tarih <c>Unspecified</c> olabilir (saat dilimi eki yoksa).
    /// PostgreSQL <c>timestamptz</c> yalnızca UTC kabul ettiği için tüm giriş
    /// tarihleri burada UTC'ye normalize edilir; ekisiz değer UTC sayılır.
    /// </summary>
    public static DateTime AsUtc(this DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
