using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YT.Generate.Cuts.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCutDurationPerChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Anuláveis de propósito: null significa "usar o padrão global de
            // configuração", então nenhum canal existente precisa ser atualizado.
            migrationBuilder.AddColumn<int>(
                name: "MinCutSeconds",
                table: "MonitoringChannels",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxCutSeconds",
                table: "MonitoringChannels",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinCutSeconds",
                table: "MonitoringChannels");

            migrationBuilder.DropColumn(
                name: "MaxCutSeconds",
                table: "MonitoringChannels");
        }
    }
}
