using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusterHubCommand.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleProfilesAndDeviceLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CurrentLatitude",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentLongitude",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocationUpdatedAtUtc",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleProfileId",
                table: "Devices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VehicleProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    MaxWeightTonnes = table.Column<double>(type: "double precision", nullable: true),
                    MaxHeightMetres = table.Column<double>(type: "double precision", nullable: true),
                    MaxWidthMetres = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_VehicleProfileId",
                table: "Devices",
                column: "VehicleProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_VehicleProfiles_VehicleProfileId",
                table: "Devices",
                column: "VehicleProfileId",
                principalTable: "VehicleProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_VehicleProfiles_VehicleProfileId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "VehicleProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Devices_VehicleProfileId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CurrentLatitude",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CurrentLongitude",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LocationUpdatedAtUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "VehicleProfileId",
                table: "Devices");
        }
    }
}
