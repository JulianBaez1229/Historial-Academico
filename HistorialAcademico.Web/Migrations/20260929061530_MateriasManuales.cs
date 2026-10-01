using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class MateriasManuales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MateriasManuales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Periodo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Calificacion = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Creada = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriasManuales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MateriasManuales_Codigo_Periodo",
                table: "MateriasManuales",
                columns: new[] { "Codigo", "Periodo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MateriasManuales");
        }
    }
}
