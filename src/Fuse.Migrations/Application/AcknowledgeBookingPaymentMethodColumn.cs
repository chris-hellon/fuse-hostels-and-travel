#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace Fuse.Migrations.Application
{
    public partial class AcknowledgeBookingPaymentMethodColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 🔇 NO-OP
            // The PaymentMethod column already exists in the database.
            // This migration exists only so EF Core knows about it.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 🔥 Only dropped if migration is rolled back
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "BookingPayments");
        }
    }
}