using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompraProgramada.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelAfterAdesaoRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "Clientes",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DataAdesao",
                table: "Clientes",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clientes",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ContasGraficasFilhote",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClienteId = table.Column<int>(type: "int", nullable: false),
                    DataCriacao = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Ativa = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasGraficasFilhote", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE Clientes SET Cpf = CONCAT('LEGACY-', Id) WHERE Cpf = '' OR Cpf IS NULL;");
            migrationBuilder.Sql("UPDATE Clientes SET Email = CONCAT('legacy', Id, '@invalid.local') WHERE Email = '' OR Email IS NULL;");
            migrationBuilder.Sql("UPDATE Clientes SET DataAdesao = NOW(6) WHERE DataAdesao = '0001-01-01 00:00:00';");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Cpf",
                table: "Clientes",
                column: "Cpf",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasGraficasFilhote_ClienteId",
                table: "ContasGraficasFilhote",
                column: "ClienteId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContasGraficasFilhote");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_Cpf",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "DataAdesao",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clientes");
        }
    }
}
