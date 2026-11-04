using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AutoInventory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LicensePlate = table.Column<string>(type: "varchar(6)", maxLength: 6, nullable: false),
                    Brand = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: true),
                    Mileage = table.Column<int>(type: "int", nullable: false),
                    Fuel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Branch = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TestDrives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestDrives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestDrives_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "Vehicles",
                columns: new[] { "Id", "Branch", "Brand", "Fuel", "LicensePlate", "Mileage", "Model", "Price", "Status", "Version", "Year" },
                values: new object[,]
                {
                    { 1, "Dordrecht", "Volkswagen", "Petrol", "GV123X", 68500, "GOLF", 19950m, "Available", 0, 2020 },
                    { 2, "Ridderkerk", "Audi", "Petrol", "KZ842P", 42100, "A3 SPORTBACK", 27450m, "Available", 0, 2021 },
                    { 3, "Zwijndrecht", "Škoda", "Diesel", "TN502R", 98000, "OCTAVIA COMBI", 16900m, "Available", 0, 2019 },
                    { 4, "Sliedrecht", "SEAT", "Petrol", "54SDK7", 87300, "IBIZA", 11750m, "Sold", 0, 2018 },
                    { 5, "Dordrecht", "CUPRA", "Electric", "PL671H", 24800, "BORN", 29950m, "Available", 0, 2023 },
                    { 6, "Oud-Beijerland", "Volkswagen", "Electric", "RB309N", 36200, "ID.4", 32500m, "Available", 0, 2022 },
                    { 7, "'s-Gravendeel", "Toyota", "Hybrid", "HF884K", 51000, "YARIS", 15450m, "Available", 0, 2020 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestDrives_VehicleId_ScheduledAt",
                table: "TestDrives",
                columns: new[] { "VehicleId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Branch",
                table: "Vehicles",
                column: "Branch");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Brand",
                table: "Vehicles",
                column: "Brand");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_LicensePlate",
                table: "Vehicles",
                column: "LicensePlate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestDrives");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
