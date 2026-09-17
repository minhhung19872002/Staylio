using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BookingRooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rooms",
                table: "bookings",
                type: "integer",
                nullable: false,
                // Every booking already made holds exactly one room.
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rooms",
                table: "bookings");
        }
    }
}
