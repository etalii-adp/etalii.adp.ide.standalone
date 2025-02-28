using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtAlii.Adp.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddedUserTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Theme",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Theme",
                table: "Users");
        }
    }
}
