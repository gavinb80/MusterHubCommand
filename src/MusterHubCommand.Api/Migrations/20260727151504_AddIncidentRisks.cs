using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentRisks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncidentRisks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    RiskLevel = table.Column<int>(type: "integer", nullable: false),
                    ControlMeasure = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    RaisedByName = table.Column<string>(type: "text", nullable: false),
                    RaisedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewedByName = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentRisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentRisks_Employees_RaisedByEmployeeId",
                        column: x => x.RaisedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncidentRisks_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentRisks_IncidentId",
                table: "IncidentRisks",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentRisks_RaisedByEmployeeId",
                table: "IncidentRisks",
                column: "RaisedByEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentRisks");
        }
    }
}
