using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplianceOfficerInCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OfficerInChargeEmployeeId",
                table: "IncidentAppliances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficerInChargeName",
                table: "IncidentAppliances",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncidentAppliances_OfficerInChargeEmployeeId",
                table: "IncidentAppliances",
                column: "OfficerInChargeEmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_IncidentAppliances_Employees_OfficerInChargeEmployeeId",
                table: "IncidentAppliances",
                column: "OfficerInChargeEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IncidentAppliances_Employees_OfficerInChargeEmployeeId",
                table: "IncidentAppliances");

            migrationBuilder.DropIndex(
                name: "IX_IncidentAppliances_OfficerInChargeEmployeeId",
                table: "IncidentAppliances");

            migrationBuilder.DropColumn(
                name: "OfficerInChargeEmployeeId",
                table: "IncidentAppliances");

            migrationBuilder.DropColumn(
                name: "OfficerInChargeName",
                table: "IncidentAppliances");
        }
    }
}
