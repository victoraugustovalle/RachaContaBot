using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RachaContas.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddMoedaEstrangeira : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MoedaOriginal",
                table: "Despesas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxaCambio",
                table: "Despesas",
                type: "TEXT",
                precision: 10,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ValorOriginal",
                table: "Despesas",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MoedaOriginal",
                table: "Despesas");

            migrationBuilder.DropColumn(
                name: "TaxaCambio",
                table: "Despesas");

            migrationBuilder.DropColumn(
                name: "ValorOriginal",
                table: "Despesas");
        }
    }
}
