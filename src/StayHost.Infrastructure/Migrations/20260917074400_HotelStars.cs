using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HotelStars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HotelStars",
                table: "listings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Demo hotels seeded before stars existed get the class HotelSeeder now gives them.
            migrationBuilder.Sql(
                "UPDATE listings SET \"HotelStars\" = CASE WHEN \"PricePerNight\" >= 2000000 THEN 5 " +
                "WHEN \"PricePerNight\" >= 1000000 THEN 4 ELSE 3 END " +
                "WHERE \"Type\" = " + (int)StayHost.Domain.PlaceType.Hotel + " AND \"HotelStars\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HotelStars",
                table: "listings");
        }
    }
}
