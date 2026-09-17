using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayHost.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentSessionSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillShareId",
                table: "payment_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExperienceBookingId",
                table: "payment_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBalance",
                table: "payment_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ServiceBookingId",
                table: "payment_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payment_sessions_BillShareId",
                table: "payment_sessions",
                column: "BillShareId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_sessions_ExperienceBookingId",
                table: "payment_sessions",
                column: "ExperienceBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_sessions_ServiceBookingId",
                table: "payment_sessions",
                column: "ServiceBookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_payment_sessions_bill_shares_BillShareId",
                table: "payment_sessions",
                column: "BillShareId",
                principalTable: "bill_shares",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_sessions_experience_bookings_ExperienceBookingId",
                table: "payment_sessions",
                column: "ExperienceBookingId",
                principalTable: "experience_bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_payment_sessions_service_bookings_ServiceBookingId",
                table: "payment_sessions",
                column: "ServiceBookingId",
                principalTable: "service_bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_payment_sessions_bill_shares_BillShareId",
                table: "payment_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_sessions_experience_bookings_ExperienceBookingId",
                table: "payment_sessions");

            migrationBuilder.DropForeignKey(
                name: "FK_payment_sessions_service_bookings_ServiceBookingId",
                table: "payment_sessions");

            migrationBuilder.DropIndex(
                name: "IX_payment_sessions_BillShareId",
                table: "payment_sessions");

            migrationBuilder.DropIndex(
                name: "IX_payment_sessions_ExperienceBookingId",
                table: "payment_sessions");

            migrationBuilder.DropIndex(
                name: "IX_payment_sessions_ServiceBookingId",
                table: "payment_sessions");

            migrationBuilder.DropColumn(
                name: "BillShareId",
                table: "payment_sessions");

            migrationBuilder.DropColumn(
                name: "ExperienceBookingId",
                table: "payment_sessions");

            migrationBuilder.DropColumn(
                name: "IsBalance",
                table: "payment_sessions");

            migrationBuilder.DropColumn(
                name: "ServiceBookingId",
                table: "payment_sessions");
        }
    }
}
