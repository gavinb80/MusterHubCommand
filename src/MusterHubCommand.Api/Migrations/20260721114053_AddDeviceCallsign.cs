using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceCallsign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Callsign",
                table: "Devices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Callsign",
                table: "Devices");
        }
    }
}
