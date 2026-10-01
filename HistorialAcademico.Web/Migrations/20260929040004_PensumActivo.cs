using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class PensumActivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PensumActivo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Universidad = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Carrera = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    NombreCarrera = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Aplicado = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PensumActivo", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PensumActivo");
        }
    }
}
