using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HistorialAcademico.Web.Migrations
{
    /// <inheritdoc />
    public partial class AgregarNombreAlumno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Nombre",
                table: "DatosAlumno",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nombre",
                table: "DatosAlumno");
        }
    }
}
