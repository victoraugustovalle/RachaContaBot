using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RachaContas.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddNomeCompletoParticipante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NomeCompleto",
                table: "Participantes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NomeCompleto",
                table: "Participantes");
        }
    }
}
