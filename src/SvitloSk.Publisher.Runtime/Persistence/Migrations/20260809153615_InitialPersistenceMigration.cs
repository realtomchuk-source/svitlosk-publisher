using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvitloSk.Publisher.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistenceMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Editions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Editions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentities",
                columns: table => new
                {
                    PublicationId = table.Column<string>(type: "text", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentities", x => x.PublicationId);
                });

            migrationBuilder.CreateTable(
                name: "PublicationPackages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    EditionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationPackages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationPackages_Editions_EditionId",
                        column: x => x.EditionId,
                        principalTable: "Editions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Publications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TerritoryId = table.Column<string>(type: "text", nullable: false),
                    Classification = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    Addresses = table.Column<string>(type: "jsonb", nullable: true),
                    HasTomorrowForecast = table.Column<bool>(type: "boolean", nullable: true),
                    ScheduleHash = table.Column<string>(type: "text", nullable: true),
                    PublicationPackageId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publications_PublicationPackages_PublicationPackageId",
                        column: x => x.PublicationPackageId,
                        principalTable: "PublicationPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationPackages_EditionId",
                table: "PublicationPackages",
                column: "EditionId");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_PublicationPackageId",
                table: "Publications",
                column: "PublicationPackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdentities");

            migrationBuilder.DropTable(
                name: "Publications");

            migrationBuilder.DropTable(
                name: "PublicationPackages");

            migrationBuilder.DropTable(
                name: "Editions");
        }
    }
}
