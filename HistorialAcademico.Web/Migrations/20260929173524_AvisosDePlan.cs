using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class AvisosDePlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Instantanea",
                table: "PlanesEstudio",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AvisosPlan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PlanNombre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Leido = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosPlan", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosPlan_Leido",
                table: "AvisosPlan",
                column: "Leido");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvisosPlan");

            migrationBuilder.DropColumn(
                name: "Instantanea",
                table: "PlanesEstudio");
        }
    }
}
