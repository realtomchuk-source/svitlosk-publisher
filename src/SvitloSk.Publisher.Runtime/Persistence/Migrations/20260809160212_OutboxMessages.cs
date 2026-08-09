using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvitloSk.Publisher.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<string>(type: "text", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    ArtifactType = table.Column<int>(type: "integer", nullable: false),
                    ExternalIdentity = table.Column<string>(type: "text", nullable: true),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.OperationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_NextRetryAt_CreatedAt",
                table: "OutboxMessages",
                columns: new[] { "Status", "NextRetryAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");
        }
    }
}
