using ImageAnalysis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageAnalysis.Infrastructure.Persistence.Configurations;

public class AnalysisResultConfiguration : IEntityTypeConfiguration<AnalysisResult>
{
    public void Configure(EntityTypeBuilder<AnalysisResult> builder)
    {
        builder.ToTable("AnalysisResults");

        builder.HasKey(r => r.Id);

        // Garante o 1:1 no banco: no máximo um resultado por análise.
        builder.HasIndex(r => r.AnalysisId).IsUnique();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Confidence)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.Summary)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(r => r.Limitations)
            .HasMaxLength(4000);
    }
}
