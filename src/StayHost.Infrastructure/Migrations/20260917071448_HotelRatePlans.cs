using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HotelRatePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BreakfastPricePerGuest",
                table: "room_types",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "NonRefundableDiscountPercent",
                table: "room_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "BreakfastFee",
                table: "bookings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BreakfastPerGuest",
                table: "bookings",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RatePlanDiscountPercent",
                table: "bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The demo hotels were seeded before rate plans existed; give them the
            // same offer HotelSeeder now seeds, so an existing database sells them too.
            migrationBuilder.Sql(
                "UPDATE room_types SET \"NonRefundableDiscountPercent\" = 10, " +
                "\"BreakfastPricePerGuest\" = GREATEST(80000, ROUND(\"PricePerNight\" / 8 / 10000) * 10000) " +
                "WHERE \"NonRefundableDiscountPercent\" = 0 AND \"BreakfastPricePerGuest\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreakfastPricePerGuest",
                table: "room_types");

            migrationBuilder.DropColumn(
                name: "NonRefundableDiscountPercent",
                table: "room_types");

            migrationBuilder.DropColumn(
                name: "BreakfastFee",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "BreakfastPerGuest",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "RatePlanDiscountPercent",
                table: "bookings");
        }
    }
}
