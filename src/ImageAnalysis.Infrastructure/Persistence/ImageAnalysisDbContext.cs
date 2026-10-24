using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace ImageAnalysis.Infrastructure.Persistence;

public class ImageAnalysisDbContext : DbContext
{
    public ImageAnalysisDbContext(DbContextOptions<ImageAnalysisDbContext> options)
        : base(options)
    {
    }

    // Analysis é a raiz do agregado; Findings, Evidences, ImageMetadata e Result
    // são alcançados pelas navegações.
    public DbSet<Analysis> Analyses => Set<Analysis>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mapeamentos das entidades (IEntityTypeConfiguration) ficam em Persistence/Configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ImageAnalysisDbContext).Assembly);

        // O Id é gerado no domínio (BaseEntity). Sem isso, o EF trata filhos novos com Id
        // preenchido (ex.: Findings adicionados a uma Analysis carregada) como existentes e gera UPDATE.
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType))
            .ToList();

        foreach (var entityType in entityTypes)
        {
            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.Id))
                .ValueGeneratedNever();
        }
    }
}
