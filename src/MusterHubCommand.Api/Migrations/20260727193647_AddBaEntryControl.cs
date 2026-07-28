using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBaEntryControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BaEntryControlEnabled",
                table: "OrganisationSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BaEntryControlPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaEntryControlPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaEntryControlPoints_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BaTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryControlPointId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TeamLeader = table.Column<string>(type: "text", nullable: false),
                    CommsChannel = table.Column<string>(type: "text", nullable: true),
                    Briefing = table.Column<string>(type: "text", nullable: true),
                    Equipment = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaTeams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaTeams_BaEntryControlPoints_EntryControlPointId",
                        column: x => x.EntryControlPointId,
                        principalTable: "BaEntryControlPoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BaWearers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CylinderPressureBar = table.Column<double>(type: "double precision", nullable: false),
                    EnteredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WhistleAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExitedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaWearers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaWearers_BaTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "BaTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BaEntryControlPoints_IncidentId",
                table: "BaEntryControlPoints",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_BaTeams_EntryControlPointId",
                table: "BaTeams",
                column: "EntryControlPointId");

            migrationBuilder.CreateIndex(
                name: "IX_BaWearers_TeamId",
                table: "BaWearers",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BaWearers");

            migrationBuilder.DropTable(
                name: "BaTeams");

            migrationBuilder.DropTable(
                name: "BaEntryControlPoints");

            migrationBuilder.DropColumn(
                name: "BaEntryControlEnabled",
                table: "OrganisationSettings");
        }
    }
}
