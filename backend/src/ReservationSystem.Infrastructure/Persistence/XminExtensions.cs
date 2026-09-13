using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ReservationSystem.Infrastructure.Persistence;

public static class XminExtensions
{
    /// <summary>
    /// PostgreSQL'in <c>xmin</c> sistem sütununu optimistic concurrency token'ı
    /// yapar. Shadow property olarak eklenir — Domain entity'sinde karşılığı yok,
    /// yani Domain'in PostgreSQL'den haberi olmuyor (domain-katmani.md §4).
    ///
    /// Npgsql 7 öncesindeki <c>UseXminAsConcurrencyToken()</c> kısayolunun yerine
    /// gelen resmi yazım budur.
    /// </summary>
    public static EntityTypeBuilder<T> UseXminConcurrencyToken<T>(this EntityTypeBuilder<T> builder)
        where T : class
    {
        builder.Property<uint>("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return builder;
    }
}
