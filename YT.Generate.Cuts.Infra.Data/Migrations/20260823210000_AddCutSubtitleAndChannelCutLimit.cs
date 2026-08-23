using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YT.Generate.Cuts.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCutSubtitleAndChannelCutLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Legenda do corte: recortada da legenda do vídeo, com os tempos
            // rebaseados para zero. Anulável porque corte sem legenda é um
            // resultado legítimo (vídeo sem legenda, ou janela sem falas).
            migrationBuilder.AddColumn<string>(
                name: "SubtitlePath",
                table: "Cuts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubtitleLanguage",
                table: "Cuts",
                type: "text",
                nullable: true);

            // Teto de cortes não publicados por canal. Anulável: null usa o
            // padrão global (Cuts:MaxPendingPerChannel), então nenhum canal
            // existente precisa ser atualizado.
            migrationBuilder.AddColumn<int>(
                name: "MaxPendingCuts",
                table: "MonitoringChannels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SubtitlePath", table: "Cuts");
            migrationBuilder.DropColumn(name: "SubtitleLanguage", table: "Cuts");
            migrationBuilder.DropColumn(name: "MaxPendingCuts", table: "MonitoringChannels");
        }
    }
}
