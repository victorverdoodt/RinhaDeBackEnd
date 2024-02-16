using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RinhaDeBackEnd.Migrations
{
    /// <inheritdoc />
    public partial class changerowversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Customers");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Customers",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Customers");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Customers",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
