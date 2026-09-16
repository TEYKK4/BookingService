using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoomBooking.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesAndRoomStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Rooms",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 3,
                column: "IsActive",
                value: true);

            migrationBuilder.UpdateData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 4,
                column: "IsActive",
                value: true);

            // Seed rows were inserted with explicit ids, which leaves the identity
            // sequence untouched - the first room created through the API would
            // collide with id 1. Move the sequence past the highest existing id.
            migrationBuilder.Sql(
                """SELECT setval(pg_get_serial_sequence('"Rooms"', 'Id'), COALESCE((SELECT MAX("Id") FROM "Rooms"), 0) + 1, false);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Rooms");
        }
    }
}
