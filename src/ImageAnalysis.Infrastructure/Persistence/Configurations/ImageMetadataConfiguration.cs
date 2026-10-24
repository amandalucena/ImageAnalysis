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

        builder.Property(m => m.Width).IsRequired();
        builder.Property(m => m.Height).IsRequired();
    }
}
