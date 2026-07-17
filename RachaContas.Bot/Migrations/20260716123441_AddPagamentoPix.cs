using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RachaContas.Bot.Migrations
{
    /// <inheritdoc />
    public partial class AddPagamentoPix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChavePix",
                table: "Participantes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Fechamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GrupoId = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fechamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fechamentos_Grupos_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "Grupos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Cobrancas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FechamentoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DevedorParticipanteId = table.Column<int>(type: "INTEGER", nullable: false),
                    CredorParticipanteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PixCopiaECola = table.Column<string>(type: "TEXT", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PagoEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cobrancas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cobrancas_Fechamentos_FechamentoId",
                        column: x => x.FechamentoId,
                        principalTable: "Fechamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Cobrancas_Participantes_CredorParticipanteId",
                        column: x => x.CredorParticipanteId,
                        principalTable: "Participantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cobrancas_Participantes_DevedorParticipanteId",
                        column: x => x.DevedorParticipanteId,
                        principalTable: "Participantes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cobrancas_CredorParticipanteId",
                table: "Cobrancas",
                column: "CredorParticipanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Cobrancas_DevedorParticipanteId",
                table: "Cobrancas",
                column: "DevedorParticipanteId");

            migrationBuilder.CreateIndex(
                name: "IX_Cobrancas_FechamentoId",
                table: "Cobrancas",
                column: "FechamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Fechamentos_GrupoId",
                table: "Fechamentos",
                column: "GrupoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cobrancas");

            migrationBuilder.DropTable(
                name: "Fechamentos");

            migrationBuilder.DropColumn(
                name: "ChavePix",
                table: "Participantes");
        }
    }
}
