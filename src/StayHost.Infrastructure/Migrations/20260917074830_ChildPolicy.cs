using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChildPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChildrenAllowed",
                table: "listings",
                type: "boolean",
                nullable: false,
                // Every place already on sale took children; false here would
                // have turned the whole catalogue adults-only overnight.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CribAvailable",
                table: "listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ExtraBedAvailable",
                table: "listings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChildrenAllowed",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "CribAvailable",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "ExtraBedAvailable",
                table: "listings");
        }
    }
}
