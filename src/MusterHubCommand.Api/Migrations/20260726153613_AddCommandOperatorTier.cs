using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandOperatorTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing grants to IncidentCommander (2), not the
            // entity's own ControlRoom default -- every operator granted
            // before this migration could already close incidents and
            // manage sectors, so this preserves exactly the access they
            // have today. New grants from here on explicitly set a tier at
            // insert time, regardless of this column's own SQL default.
            migrationBuilder.AddColumn<int>(
                name: "Tier",
                table: "CommandOperators",
                type: "integer",
                nullable: false,
                defaultValue: 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tier",
                table: "CommandOperators");
        }
    }
}
