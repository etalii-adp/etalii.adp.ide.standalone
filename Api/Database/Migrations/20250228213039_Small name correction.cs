using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtAlii.Adp.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Smallnamecorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TagGroups_Links_LinkId1",
                table: "TagGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_TagGroups_Nodes_NodeId1",
                table: "TagGroups");

            migrationBuilder.DropIndex(
                name: "IX_TagGroups_LinkId1",
                table: "TagGroups");

            migrationBuilder.DropIndex(
                name: "IX_TagGroups_NodeId1",
                table: "TagGroups");

            migrationBuilder.DropColumn(
                name: "LinkId1",
                table: "TagGroups");

            migrationBuilder.DropColumn(
                name: "NodeId1",
                table: "TagGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LinkId1",
                table: "TagGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NodeId1",
                table: "TagGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_LinkId1",
                table: "TagGroups",
                column: "LinkId1");

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_NodeId1",
                table: "TagGroups",
                column: "NodeId1");

            migrationBuilder.AddForeignKey(
                name: "FK_TagGroups_Links_LinkId1",
                table: "TagGroups",
                column: "LinkId1",
                principalTable: "Links",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TagGroups_Nodes_NodeId1",
                table: "TagGroups",
                column: "NodeId1",
                principalTable: "Nodes",
                principalColumn: "Id");
        }
    }
}
