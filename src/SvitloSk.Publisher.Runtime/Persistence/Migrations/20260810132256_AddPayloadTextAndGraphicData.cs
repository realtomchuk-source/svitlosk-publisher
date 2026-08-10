using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SvitloSk.Publisher.Runtime.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadTextAndGraphicData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GraphicData",
                table: "Publications",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayloadText",
                table: "Publications",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GraphicData",
                table: "Publications");

            migrationBuilder.DropColumn(
                name: "PayloadText",
                table: "Publications");
        }
    }
}
