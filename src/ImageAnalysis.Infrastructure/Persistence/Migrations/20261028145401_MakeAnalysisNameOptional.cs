using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageAnalysis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakeAnalysisNameOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Analyses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // O defaultValue não preenche as linhas existentes: sem isso, o SET NOT NULL falha
            // assim que houver alguma análise criada sem nome.
            migrationBuilder.Sql("UPDATE \"Analyses\" SET \"Name\" = '' WHERE \"Name\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Analyses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
