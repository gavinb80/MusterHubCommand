using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentSectorsAndResourceKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AcknowledgedAtUtc",
                table: "IncidentUpdates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedByName",
                table: "IncidentUpdates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResourceKind",
                table: "IncidentAppliances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SectorId",
                table: "IncidentAppliances",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentSectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentSectors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentSectors_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAppliances_SectorId",
                table: "IncidentAppliances",
                column: "SectorId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentSectors_IncidentId",
                table: "IncidentSectors",
                column: "IncidentId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentAppliances_IncidentSectors_SectorId",
                table: "IncidentAppliances",
                column: "SectorId",
                principalTable: "IncidentSectors",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentAppliances_IncidentSectors_SectorId",
                table: "IncidentAppliances");

            migrationBuilder.DropTable(
                name: "IncidentSectors");

            migrationBuilder.DropIndex(
                name: "IX_IncidentAppliances_SectorId",
                table: "IncidentAppliances");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAtUtc",
                table: "IncidentUpdates");

            migrationBuilder.DropColumn(
                name: "AcknowledgedByName",
                table: "IncidentUpdates");

            migrationBuilder.DropColumn(
                name: "ResourceKind",
                table: "IncidentAppliances");

            migrationBuilder.DropColumn(
                name: "SectorId",
                table: "IncidentAppliances");
        }
    }
}
