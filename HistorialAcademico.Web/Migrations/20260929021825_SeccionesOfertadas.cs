using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class SeccionesOfertadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsultasSecciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Periodo = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Secciones = table.Column<int>(type: "INTEGER", nullable: false),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultasSecciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeccionesOfertadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Periodo = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    Nrc = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Seccion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Creditos = table.Column<decimal>(type: "TEXT", precision: 4, scale: 1, nullable: false),
                    Campus = table.Column<string>(type: "TEXT", nullable: false),
                    Metodo = table.Column<string>(type: "TEXT", nullable: false),
                    Profesor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CupoMaximo = table.Column<int>(type: "INTEGER", nullable: false),
                    Inscritos = table.Column<int>(type: "INTEGER", nullable: false),
                    CuposDisponibles = table.Column<int>(type: "INTEGER", nullable: false),
                    Abierta = table.Column<bool>(type: "INTEGER", nullable: false),
                    BloquesJson = table.Column<string>(type: "TEXT", nullable: false),
                    Consultada = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeccionesOfertadas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasSecciones_Periodo_Codigo",
                table: "ConsultasSecciones",
                columns: new[] { "Periodo", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeccionesOfertadas_Codigo_Periodo",
                table: "SeccionesOfertadas",
                columns: new[] { "Codigo", "Periodo" });

            migrationBuilder.CreateIndex(
                name: "IX_SeccionesOfertadas_Periodo_Nrc",
                table: "SeccionesOfertadas",
                columns: new[] { "Periodo", "Nrc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsultasSecciones");

            migrationBuilder.DropTable(
                name: "SeccionesOfertadas");
        }
    }
}
