using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBaEntryControlPointOwningDevice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwningDeviceId",
                table: "BaEntryControlPoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaEntryControlPoints_OwningDeviceId",
                table: "BaEntryControlPoints",
                column: "OwningDeviceId");

            migrationBuilder.AddForeignKey(
                name: "FK_BaEntryControlPoints_Devices_OwningDeviceId",
                table: "BaEntryControlPoints",
                column: "OwningDeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BaEntryControlPoints_Devices_OwningDeviceId",
                table: "BaEntryControlPoints");

            migrationBuilder.DropIndex(
                name: "IX_BaEntryControlPoints_OwningDeviceId",
                table: "BaEntryControlPoints");

            migrationBuilder.DropColumn(
                name: "OwningDeviceId",
                table: "BaEntryControlPoints");
        }
    }
}
