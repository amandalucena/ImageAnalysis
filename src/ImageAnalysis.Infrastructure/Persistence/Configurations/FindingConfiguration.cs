using ImageAnalysis.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageAnalysis.Infrastructure.Persistence.Configurations;

public class FindingConfiguration : IEntityTypeConfiguration<Finding>
{
    public void Configure(EntityTypeBuilder<Finding> builder)
    {
        builder.ToTable("Findings");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(f => f.SourceFindingType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(f => f.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(f => f.AnalysisId);

        // 1:N — o EF usa o campo _evidences, já que Evidences é somente leitura.
        builder.HasMany(f => f.Evidences)
            .WithOne()
            .HasForeignKey(e => e.FindingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Evidences).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
