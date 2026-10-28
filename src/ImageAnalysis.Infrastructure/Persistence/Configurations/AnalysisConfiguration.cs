using ImageAnalysis.Domain.Entities;
using ImageAnalysis.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageAnalysis.Infrastructure.Persistence.Configurations;

public class AnalysisConfiguration : IEntityTypeConfiguration<Analysis>
{
    public void Configure(EntityTypeBuilder<Analysis> builder)
    {
        builder.ToTable("Analyses");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name)
            .HasMaxLength(Analysis.MaxNameLength);

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.StartedAt);
        builder.Property(a => a.FinishedAt);

        // Concorrência otimista: a coluna de sistema xmin do PostgreSQL muda a cada UPDATE
        // na linha, então serve de token sem coluna nova. Vale para o agregado inteiro,
        // porque o AnalysisRepository sempre atualiza a raiz ao salvar.
        builder.Property<uint>("xmin").IsRowVersion();

        // Value Objects gravados como colunas da própria tabela Analyses.
        builder.OwnsOne(a => a.Image, image =>
        {
            image.Property(i => i.OriginalFileName).HasColumnName("ImageOriginalFileName").HasMaxLength(ImageFile.MaxOriginalFileNameLength).IsRequired();
            image.Property(i => i.StoragePath).HasColumnName("ImageStoragePath").HasMaxLength(500).IsRequired();
            image.Property(i => i.SizeInBytes).HasColumnName("ImageSizeInBytes").IsRequired();
            image.Property(i => i.Sha256).HasColumnName("ImageSha256").HasMaxLength(64).IsFixedLength().IsRequired();
            image.Property(i => i.MimeType).HasColumnName("ImageMimeType").HasMaxLength(100).IsRequired();
        });
        builder.Navigation(a => a.Image).IsRequired();

        builder.OwnsOne(a => a.Failure, failure =>
        {
            failure.Property(f => f.Type).HasColumnName("FailureType").HasConversion<string>().HasMaxLength(32);
            failure.Property(f => f.Message).HasColumnName("FailureMessage").HasMaxLength(1000);
        });

        // 1:1 — a FK fica no filho (AnalysisId), não em Analyses.
        builder.HasOne(a => a.Metadata)
            .WithOne()
            .HasForeignKey<ImageMetadata>(m => m.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Result)
            .WithOne()
            .HasForeignKey<AnalysisResult>(r => r.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);

        // 1:N — o EF usa o campo _findings, já que Findings é somente leitura.
        builder.HasMany(a => a.Findings)
            .WithOne()
            .HasForeignKey(f => f.AnalysisId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.Findings).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
