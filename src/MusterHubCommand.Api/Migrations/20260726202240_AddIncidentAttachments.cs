using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncidentAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    UploadedByDeviceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentAttachments_Devices_UploadedByDeviceId",
                        column: x => x.UploadedByDeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncidentAttachments_Employees_UploadedByEmployeeId",
                        column: x => x.UploadedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IncidentAttachments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_IncidentId",
                table: "IncidentAttachments",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_UploadedByDeviceId",
                table: "IncidentAttachments",
                column: "UploadedByDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAttachments_UploadedByEmployeeId",
                table: "IncidentAttachments",
                column: "UploadedByEmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncidentAttachments");
        }
    }
}
