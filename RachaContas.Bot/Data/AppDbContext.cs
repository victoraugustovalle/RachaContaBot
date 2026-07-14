using Microsoft.EntityFrameworkCore;
using RachaContas.Bot.Models;

namespace RachaContas.Bot.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Grupo> Grupos => Set<Grupo>();
    public DbSet<Participante> Participantes => Set<Participante>();
    public DbSet<Despesa> Despesas => Set<Despesa>();
    public DbSet<DespesaParticipante> DespesaParticipantes => Set<DespesaParticipante>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Grupo>()
            .HasIndex(g => g.TelegramChatId)
            .IsUnique();

        modelBuilder.Entity<Participante>()
            .HasIndex(p => new { p.GrupoId, p.TelegramUserId })
            .IsUnique();

        modelBuilder.Entity<Despesa>()
            .Property(d => d.Valor)
            .HasPrecision(10, 2);

        modelBuilder.Entity<DespesaParticipante>()
            .HasKey(dp => new { dp.DespesaId, dp.ParticipanteId });
    }
}
