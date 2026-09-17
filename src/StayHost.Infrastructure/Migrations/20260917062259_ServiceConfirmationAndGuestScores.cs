using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ServiceConfirmationAndGuestScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresConfirmation",
                table: "service_offerings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RespondBy",
                table: "service_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Cleanliness",
                table: "guest_reviews",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Communication",
                table: "guest_reviews",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HouseRules",
                table: "guest_reviews",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequiresConfirmation",
                table: "service_offerings");

            migrationBuilder.DropColumn(
                name: "RespondBy",
                table: "service_bookings");

            migrationBuilder.DropColumn(
                name: "Cleanliness",
                table: "guest_reviews");

            migrationBuilder.DropColumn(
                name: "Communication",
                table: "guest_reviews");

            migrationBuilder.DropColumn(
                name: "HouseRules",
                table: "guest_reviews");
        }
    }
}
