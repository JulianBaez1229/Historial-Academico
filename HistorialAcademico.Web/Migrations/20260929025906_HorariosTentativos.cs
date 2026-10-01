using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class HorariosTentativos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AperturasSolicitadas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Nota = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Marcada = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AperturasSolicitadas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BloquesNoDisponibles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Dia = table.Column<int>(type: "INTEGER", nullable: false),
                    DesdeMin = table.Column<int>(type: "INTEGER", nullable: false),
                    HastaMin = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloquesNoDisponibles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HorariosTentativos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nombre = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Periodo = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    PlanEstudioId = table.Column<int>(type: "INTEGER", nullable: true),
                    Creado = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actualizado = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HorariosTentativos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HorariosTentativos_PlanesEstudio_PlanEstudioId",
                        column: x => x.PlanEstudioId,
                        principalTable: "PlanesEstudio",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SeccionesElegidas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HorarioTentativoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nrc = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Etiqueta = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeccionesElegidas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeccionesElegidas_HorariosTentativos_HorarioTentativoId",
                        column: x => x.HorarioTentativoId,
                        principalTable: "HorariosTentativos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AperturasSolicitadas_Codigo",
                table: "AperturasSolicitadas",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BloquesNoDisponibles_Dia_DesdeMin",
                table: "BloquesNoDisponibles",
                columns: new[] { "Dia", "DesdeMin" });

            migrationBuilder.CreateIndex(
                name: "IX_HorariosTentativos_Nombre",
                table: "HorariosTentativos",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HorariosTentativos_PlanEstudioId",
                table: "HorariosTentativos",
                column: "PlanEstudioId");

            migrationBuilder.CreateIndex(
                name: "IX_SeccionesElegidas_HorarioTentativoId_Codigo",
                table: "SeccionesElegidas",
                columns: new[] { "HorarioTentativoId", "Codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AperturasSolicitadas");

            migrationBuilder.DropTable(
                name: "BloquesNoDisponibles");

            migrationBuilder.DropTable(
                name: "SeccionesElegidas");

            migrationBuilder.DropTable(
                name: "HorariosTentativos");
        }
    }
}
