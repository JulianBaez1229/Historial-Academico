using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CursosEnProgreso",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Periodo = table.Column<string>(type: "TEXT", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Materia = table.Column<string>(type: "TEXT", nullable: false),
                    Curso = table.Column<string>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", nullable: false),
                    HorasCredito = table.Column<decimal>(type: "TEXT", nullable: false),
                    Campus = table.Column<string>(type: "TEXT", nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CursosEnProgreso", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatosAlumno",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FechaNacimiento = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TipoAlumno = table.Column<string>(type: "TEXT", nullable: true),
                    Programa = table.Column<string>(type: "TEXT", nullable: true),
                    Escuela = table.Column<string>(type: "TEXT", nullable: true),
                    Campus = table.Column<string>(type: "TEXT", nullable: true),
                    Carrera = table.Column<string>(type: "TEXT", nullable: true),
                    GradoAObtener = table.Column<string>(type: "TEXT", nullable: true),
                    EstadoAcademico = table.Column<string>(type: "TEXT", nullable: true),
                    TotalHorasIntentadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalHorasAprobadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalHorasGanadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalHorasPga = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalPuntosCalidad = table.Column<decimal>(type: "TEXT", nullable: false),
                    TotalPga = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatosAlumno", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equivalencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CodigoBanner = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CodigoPensum = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Nota = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equivalencias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MateriasPensum",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", nullable: false),
                    Creditos = table.Column<int>(type: "INTEGER", nullable: false),
                    Cuatrimestre = table.Column<int>(type: "INTEGER", nullable: false),
                    Prerrequisitos = table.Column<string>(type: "TEXT", nullable: true),
                    EsElectiva = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriasPensum", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Periodos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Orden = table.Column<int>(type: "INTEGER", nullable: false),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", nullable: false),
                    Escuela = table.Column<string>(type: "TEXT", nullable: true),
                    Carrera = table.Column<string>(type: "TEXT", nullable: true),
                    TipoAlumno = table.Column<string>(type: "TEXT", nullable: true),
                    EstadoAcademico = table.Column<string>(type: "TEXT", nullable: true),
                    HorasIntentadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    HorasAprobadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    HorasGanadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    HorasPga = table.Column<decimal>(type: "TEXT", nullable: false),
                    PuntosCalidad = table.Column<decimal>(type: "TEXT", nullable: false),
                    Pga = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumHorasIntentadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumHorasAprobadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumHorasGanadas = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumHorasPga = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumPuntosCalidad = table.Column<decimal>(type: "TEXT", nullable: false),
                    AcumPga = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Periodos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sincronizaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Fecha = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Resultado = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Mensaje = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sincronizaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MateriasCursadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PeriodoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Materia = table.Column<string>(type: "TEXT", nullable: false),
                    Curso = table.Column<string>(type: "TEXT", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", nullable: false),
                    Calificacion = table.Column<string>(type: "TEXT", nullable: false),
                    HorasCredito = table.Column<decimal>(type: "TEXT", nullable: false),
                    PuntosCalidad = table.Column<decimal>(type: "TEXT", nullable: false),
                    Campus = table.Column<string>(type: "TEXT", nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriasCursadas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MateriasCursadas_Periodos_PeriodoId",
                        column: x => x.PeriodoId,
                        principalTable: "Periodos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CursosEnProgreso_Codigo",
                table: "CursosEnProgreso",
                column: "Codigo");

            migrationBuilder.CreateIndex(
                name: "IX_Equivalencias_CodigoBanner_CodigoPensum",
                table: "Equivalencias",
                columns: new[] { "CodigoBanner", "CodigoPensum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MateriasCursadas_Codigo",
                table: "MateriasCursadas",
                column: "Codigo");

            migrationBuilder.CreateIndex(
                name: "IX_MateriasCursadas_PeriodoId",
                table: "MateriasCursadas",
                column: "PeriodoId");

            migrationBuilder.CreateIndex(
                name: "IX_MateriasPensum_Codigo",
                table: "MateriasPensum",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Periodos_Nombre",
                table: "Periodos",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Periodos_Orden",
                table: "Periodos",
                column: "Orden",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sincronizaciones_Fecha",
                table: "Sincronizaciones",
                column: "Fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CursosEnProgreso");

            migrationBuilder.DropTable(
                name: "DatosAlumno");

            migrationBuilder.DropTable(
                name: "Equivalencias");

            migrationBuilder.DropTable(
                name: "MateriasCursadas");

            migrationBuilder.DropTable(
                name: "MateriasPensum");

            migrationBuilder.DropTable(
                name: "Sincronizaciones");

            migrationBuilder.DropTable(
                name: "Periodos");
        }
    }
}
