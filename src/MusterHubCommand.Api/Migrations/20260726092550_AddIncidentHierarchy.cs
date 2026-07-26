using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "IncidentSectors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PersonInChargeEmployeeId",
                table: "IncidentSectors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonInChargeName",
                table: "IncidentSectors",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentSectors_ParentId",
                table: "IncidentSectors",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_IncidentSectors_PersonInChargeEmployeeId",
                table: "IncidentSectors",
                column: "PersonInChargeEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentSectors_Employees_PersonInChargeEmployeeId",
                table: "IncidentSectors",
                column: "PersonInChargeEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentSectors_IncidentSectors_ParentId",
                table: "IncidentSectors",
                column: "ParentId",
                principalTable: "IncidentSectors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentSectors_Employees_PersonInChargeEmployeeId",
                table: "IncidentSectors");

            migrationBuilder.DropForeignKey(
                name: "FK_IncidentSectors_IncidentSectors_ParentId",
                table: "IncidentSectors");

            migrationBuilder.DropIndex(
                name: "IX_IncidentSectors_ParentId",
                table: "IncidentSectors");

            migrationBuilder.DropIndex(
                name: "IX_IncidentSectors_PersonInChargeEmployeeId",
                table: "IncidentSectors");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "IncidentSectors");

            migrationBuilder.DropColumn(
                name: "PersonInChargeEmployeeId",
                table: "IncidentSectors");

            migrationBuilder.DropColumn(
                name: "PersonInChargeName",
                table: "IncidentSectors");
        }
    }
}
