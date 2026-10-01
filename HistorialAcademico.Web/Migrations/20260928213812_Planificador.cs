using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class Planificador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConfiguracionPlanificador",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LimiteBase = table.Column<int>(type: "INTEGER", nullable: false),
                    LimiteAlto = table.Column<int>(type: "INTEGER", nullable: false),
                    UmbralIndice = table.Column<decimal>(type: "TEXT", nullable: false),
                    Minimo = table.Column<int>(type: "INTEGER", nullable: false),
                    AsumirEnCurso = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracionPlanificador", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanesEstudio",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Creado = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actualizado = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanesEstudio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PeriodosPlanificados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlanEstudioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeriodosPlanificados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PeriodosPlanificados_PlanesEstudio_PlanEstudioId",
                        column: x => x.PlanEstudioId,
                        principalTable: "PlanesEstudio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MateriasPlanificadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeriodoPlanificadoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Razon = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriasPlanificadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MateriasPlanificadas_PeriodosPlanificados_PeriodoPlanificadoId",
                        column: x => x.PeriodoPlanificadoId,
                        principalTable: "PeriodosPlanificados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MateriasPlanificadas_PeriodoPlanificadoId_Codigo",
                table: "MateriasPlanificadas",
                columns: new[] { "PeriodoPlanificadoId", "Codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PeriodosPlanificados_PlanEstudioId_Nombre",
                table: "PeriodosPlanificados",
                columns: new[] { "PlanEstudioId", "Nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanesEstudio_Nombre",
                table: "PlanesEstudio",
                column: "Nombre",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConfiguracionPlanificador");

            migrationBuilder.DropTable(
                name: "MateriasPlanificadas");

            migrationBuilder.DropTable(
                name: "PeriodosPlanificados");

            migrationBuilder.DropTable(
                name: "PlanesEstudio");
        }
    }
}
