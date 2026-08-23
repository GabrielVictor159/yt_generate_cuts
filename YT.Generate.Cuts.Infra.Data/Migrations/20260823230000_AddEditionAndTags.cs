using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace YT.Generate.Cuts.Infra.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEditionAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EditionConfigurations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    BurnSubtitles = table.Column<bool>(type: "boolean", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditionConfigurations", x => x.Id);
                });

            // Anulável: sem perfil, o canal usa o padrão global da seção Edition.
            // Assim nenhum canal existente precisa ser atualizado.
            migrationBuilder.AddColumn<long>(
                name: "EditionConfigurationId",
                table: "MonitoringChannels",
                type: "bigint",
                nullable: true);

            // Tags: texto separado por vírgula. Anuláveis, então nenhuma linha
            // existente precisa ser atualizada.
            migrationBuilder.AddColumn<string>(
                name: "DefaultTags",
                table: "MonitoringChannels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Cuts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringChannels_EditionConfigurationId",
                table: "MonitoringChannels",
                column: "EditionConfigurationId");

            // SetNull e não Cascade: apagar um perfil de edição não pode levar
            // embora os canais que o usavam — eles apenas voltam ao padrão global.
            migrationBuilder.AddForeignKey(
                name: "FK_MonitoringChannels_EditionConfigurations_EditionConfigurationId",
                table: "MonitoringChannels",
                column: "EditionConfigurationId",
                principalTable: "EditionConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MonitoringChannels_EditionConfigurations_EditionConfigurationId",
                table: "MonitoringChannels");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringChannels_EditionConfigurationId",
                table: "MonitoringChannels");

            migrationBuilder.DropColumn(
                name: "EditionConfigurationId",
                table: "MonitoringChannels");

            migrationBuilder.DropColumn(name: "DefaultTags", table: "MonitoringChannels");
            migrationBuilder.DropColumn(name: "Tags", table: "Cuts");

            migrationBuilder.DropTable(name: "EditionConfigurations");
        }
    }
}
