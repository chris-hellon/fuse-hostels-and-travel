using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddEnquiryMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnquiryMessages",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnquiryType = table.Column<int>(type: "int", nullable: false),
                    EnquiryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    FromName = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ToAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    HtmlBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SentOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    GmailMessageId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GmailThreadId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SentBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnquiryMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnquiryMessages_EnquiryType_EnquiryId",
                schema: "Catalog",
                table: "EnquiryMessages",
                columns: new[] { "EnquiryType", "EnquiryId" });

            migrationBuilder.CreateIndex(
                name: "IX_EnquiryMessages_GmailMessageId",
                schema: "Catalog",
                table: "EnquiryMessages",
                column: "GmailMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EnquiryMessages_MessageId",
                schema: "Catalog",
                table: "EnquiryMessages",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_EnquiryMessages_SentOn",
                schema: "Catalog",
                table: "EnquiryMessages",
                column: "SentOn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnquiryMessages",
                schema: "Catalog");
        }
    }
}
