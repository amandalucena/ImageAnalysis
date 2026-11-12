using ImageAnalysis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageAnalysis.Infrastructure.Persistence.Configurations;

public class ImageMetadataConfiguration : IEntityTypeConfiguration<ImageMetadata>
{
    public void Configure(EntityTypeBuilder<ImageMetadata> builder)
    {
        builder.ToTable("ImageMetadata");

        builder.HasKey(m => m.Id);

        // Garante o 1:1 no banco: no máximo um ImageMetadata por análise.
        builder.HasIndex(m => m.AnalysisId).IsUnique();

        builder.Property(m => m.RawMetadataPath)
            .IsRequired()
            .HasMaxLength(500);

        // Value Object gravado nas mesmas colunas Width/Height de antes: não exige migration.
        builder.OwnsOne(m => m.Dimensions, dimensions =>
        {
            dimensions.Property(d => d.Width).HasColumnName("Width").IsRequired();
            dimensions.Property(d => d.Height).HasColumnName("Height").IsRequired();
        });
        builder.Navigation(m => m.Dimensions).IsRequired();
    }
}
