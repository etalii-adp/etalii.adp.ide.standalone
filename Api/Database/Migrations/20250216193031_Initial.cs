using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EtAlii.Adp.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JoinDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExternalIdentifier = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Diagrams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModificationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Zoom = table.Column<double>(type: "float", nullable: false),
                    DiagramPositionX = table.Column<double>(type: "float", nullable: false),
                    DiagramPositionY = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagrams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Diagrams_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodePositionX = table.Column<double>(type: "float", nullable: false),
                    NodePositionY = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Nodes_Diagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "Diagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartPort = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EndNodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EndPort = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Diagrams_DiagramId",
                        column: x => x.DiagramId,
                        principalTable: "Diagrams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Links_Nodes_EndNodeId",
                        column: x => x.EndNodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Links_Nodes_StartNodeId",
                        column: x => x.StartNodeId,
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Diagrams_Id",
                table: "Diagrams",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Diagrams_OwnerId",
                table: "Diagrams",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_DiagramId",
                table: "Links",
                column: "DiagramId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_EndNodeId",
                table: "Links",
                column: "EndNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_Id",
                table: "Links",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Links_StartNodeId",
                table: "Links",
                column: "StartNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_DiagramId",
                table: "Nodes",
                column: "DiagramId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Id",
                table: "Nodes",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Id",
                table: "Users",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "Diagrams");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
