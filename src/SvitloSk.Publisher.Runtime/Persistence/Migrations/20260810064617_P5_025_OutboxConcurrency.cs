using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvitloSk.Publisher.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class P5_025_OutboxConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClaimedBy",
                table: "OutboxMessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeasedUntil",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedBy",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeasedUntil",
                table: "OutboxMessages");
        }
    }
}
