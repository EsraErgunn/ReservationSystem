using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReservationSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Şema değişikliği yok: yalnızca model snapshot'ında Guid anahtarların
    /// "eklenirken üretilir" işareti kaldırılıyor (bkz. AppDbContext.OnModelCreating).
    /// Kimlikler zaten Domain'de üretiliyordu; veritabanında varsayılan değer yoktu.
    /// </summary>
    public partial class ClientGeneratedKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
