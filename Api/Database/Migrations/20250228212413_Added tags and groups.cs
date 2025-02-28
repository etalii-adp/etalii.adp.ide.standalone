using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtAlii.Adp.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Addedtagsandgroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NodeId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagGroups_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TagGroups_Links_LinkId1",
                        column: x => x.LinkId1,
                        principalTable: "Links",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TagGroups_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TagGroups_Nodes_NodeId1",
                        column: x => x.NodeId1,
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TagGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_TagGroups_TagGroupId",
                        column: x => x.TagGroupId,
                        principalTable: "TagGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_Id",
                table: "TagGroups",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_LinkId",
                table: "TagGroups",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_LinkId1",
                table: "TagGroups",
                column: "LinkId1");

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_NodeId",
                table: "TagGroups",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TagGroups_NodeId1",
                table: "TagGroups",
                column: "NodeId1");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Id",
                table: "Tags",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_TagGroupId",
                table: "Tags",
                column: "TagGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "TagGroups");
        }
    }
}
