using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReservationSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    full_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('User', 'Admin')");
                });

            migrationBuilder.CreateTable(
                name: "venues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    city = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_venues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    event_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sales_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sales_end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.CheckConstraint("ck_events_sales_before_event", "sales_end_at <= event_date");
                    table.CheckConstraint("ck_events_sales_window", "sales_start_at < sales_end_at");
                    table.ForeignKey(
                        name: "fk_events_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "seats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    venue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_label = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    seat_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seats", x => x.id);
                    table.CheckConstraint("ck_seats_number", "seat_number > 0");
                    table.ForeignKey(
                        name: "fk_seats_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    held_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.CheckConstraint("ck_reservations_status", "status IN ('Held', 'Confirmed', 'Expired', 'Failed', 'Cancelled')");
                    table.CheckConstraint("ck_reservations_total", "total_amount >= 0");
                    table.ForeignKey(
                        name: "fk_reservations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_seats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_seats", x => x.id);
                    table.CheckConstraint("ck_event_seats_price", "price >= 0");
                    table.CheckConstraint("ck_event_seats_status", "status IN ('Available', 'Held', 'Sold')");
                    table.ForeignKey(
                        name: "fk_event_seats_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_seats_seats_seat_id",
                        column: x => x.seat_id,
                        principalTable: "seats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_token = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    provider_payment_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_completed", "(status = 'Pending') = (completed_at IS NULL)");
                    table.CheckConstraint("ck_payments_status", "status IN ('Pending', 'Succeeded', 'Failed', 'Abandoned')");
                    table.ForeignKey(
                        name: "fk_payments_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_seat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price_at_reservation = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation_items", x => x.id);
                    table.CheckConstraint("ck_reservation_items_price", "price_at_reservation >= 0");
                    table.ForeignKey(
                        name: "fk_reservation_items_event_seats_event_seat_id",
                        column: x => x.event_seat_id,
                        principalTable: "event_seats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservation_items_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_event_seats_availability",
                table: "event_seats",
                columns: new[] { "event_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_event_seats_seat_id",
                table: "event_seats",
                column: "seat_id");

            migrationBuilder.CreateIndex(
                name: "ux_event_seats_event_seat",
                table: "event_seats",
                columns: new[] { "event_id", "seat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_events_date",
                table: "events",
                column: "event_date");

            migrationBuilder.CreateIndex(
                name: "ix_events_venue_id",
                table: "events",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_reservation",
                table: "payments",
                columns: new[] { "reservation_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_payment_id",
                table: "payments",
                column: "provider_payment_id",
                unique: true,
                filter: "provider_payment_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_provider_token",
                table: "payments",
                column: "provider_token",
                unique: true,
                filter: "provider_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_single_pending",
                table: "payments",
                column: "reservation_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_reservation_items_reservation",
                table: "reservation_items",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ux_reservation_items_active_seat",
                table: "reservation_items",
                column: "event_seat_id",
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_event_id",
                table: "reservations",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_expiry",
                table: "reservations",
                column: "held_until",
                filter: "status = 'Held'");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_user",
                table: "reservations",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_seats_venue_position",
                table: "seats",
                columns: new[] { "venue_id", "row_label", "seat_number" },
                unique: true);

            // İfade tabanlı (functional) index EF Core modelinde ifade edilemiyor,
            // bu yüzden ham SQL ile oluşturuluyor. lower(email) üzerinde olması şart:
            // aksi hâlde Esra@x.com ve esra@x.com iki ayrı hesap olarak kaydedilir.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_users_email ON users (lower(email));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_users_email;");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "reservation_items");

            migrationBuilder.DropTable(
                name: "event_seats");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "seats");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "venues");
        }
    }
}
