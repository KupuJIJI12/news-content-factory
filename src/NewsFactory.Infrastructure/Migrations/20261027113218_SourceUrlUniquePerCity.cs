using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceUrlUniquePerCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_Url",
                table: "Sources");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_Url",
                table: "Sources",
                column: "Url");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_Url",
                table: "Sources");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_Url",
                table: "Sources",
                column: "Url",
                unique: true);
        }
    }
}
