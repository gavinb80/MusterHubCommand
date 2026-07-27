using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentActionsAndUpdateReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToUpdateId",
                table: "IncidentUpdates",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    RaisedByName = table.Column<string>(type: "text", nullable: true),
                    RaisedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedToName = table.Column<string>(type: "text", nullable: true),
                    SectorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedByName = table.Column<string>(type: "text", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByName = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentActions_Employees_AssignedToEmployeeId",
                        column: x => x.AssignedToEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncidentActions_IncidentSectors_SectorId",
                        column: x => x.SectorId,
                        principalTable: "IncidentSectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncidentActions_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentUpdates_ReplyToUpdateId",
                table: "IncidentUpdates",
                column: "ReplyToUpdateId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentActions_AssignedToEmployeeId",
                table: "IncidentActions",
                column: "AssignedToEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentActions_IncidentId",
                table: "IncidentActions",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentActions_SectorId",
                table: "IncidentActions",
                column: "SectorId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentUpdates_IncidentUpdates_ReplyToUpdateId",
                table: "IncidentUpdates",
                column: "ReplyToUpdateId",
                principalTable: "IncidentUpdates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentUpdates_IncidentUpdates_ReplyToUpdateId",
                table: "IncidentUpdates");

            migrationBuilder.DropTable(
                name: "IncidentActions");

            migrationBuilder.DropIndex(
                name: "IX_IncidentUpdates_ReplyToUpdateId",
                table: "IncidentUpdates");

            migrationBuilder.DropColumn(
                name: "ReplyToUpdateId",
                table: "IncidentUpdates");
        }
    }
}
