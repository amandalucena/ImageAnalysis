using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageAnalysis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nada a fazer no banco: o token de concorrência é a coluna de sistema xmin, que
            // já existe em toda tabela do PostgreSQL. O EF gerou um AddColumn para ela, que o
            // Npgsql ignora; foi removido para não parecer que uma coluna é criada aqui.
            // A migration existe para registrar o xmin no model snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nada a desfazer: ver Up.
        }
    }
}
