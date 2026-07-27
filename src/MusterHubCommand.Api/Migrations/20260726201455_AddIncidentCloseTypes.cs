using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentCloseTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseCause",
                table: "Incidents");

            migrationBuilder.AddColumn<Guid>(
                name: "CloseTypeId",
                table: "Incidents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IncidentCloseTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentCloseTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Incidents_CloseTypeId",
                table: "Incidents",
                column: "CloseTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Incidents_IncidentCloseTypes_CloseTypeId",
                table: "Incidents",
                column: "CloseTypeId",
                principalTable: "IncidentCloseTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Incidents_IncidentCloseTypes_CloseTypeId",
                table: "Incidents");

            migrationBuilder.DropTable(
                name: "IncidentCloseTypes");

            migrationBuilder.DropIndex(
                name: "IX_Incidents_CloseTypeId",
                table: "Incidents");

            migrationBuilder.DropColumn(
                name: "CloseTypeId",
                table: "Incidents");

            migrationBuilder.AddColumn<string>(
                name: "CloseCause",
                table: "Incidents",
                type: "text",
                nullable: true);
        }
    }
}
