using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoInventory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueTimeSlotPerVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            SwapIndex(migrationBuilder, unique: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            SwapIndex(migrationBuilder, unique: false);

        // Hand-edited: MySQL won't drop an index a foreign key needs, so a temporary VehicleId index covers the FK during the swap.
        private static void SwapIndex(MigrationBuilder mb, bool unique)
        {
            mb.CreateIndex(name: "IX_TestDrives_VehicleId_Tmp", table: "TestDrives", column: "VehicleId");
            mb.DropIndex(name: "IX_TestDrives_VehicleId_ScheduledAt", table: "TestDrives");
            mb.CreateIndex(
                name: "IX_TestDrives_VehicleId_ScheduledAt",
                table: "TestDrives",
                columns: new[] { "VehicleId", "ScheduledAt" },
                unique: unique);
            mb.DropIndex(name: "IX_TestDrives_VehicleId_Tmp", table: "TestDrives");
        }
    }
}
