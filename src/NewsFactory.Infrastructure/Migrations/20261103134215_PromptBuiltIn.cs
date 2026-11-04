using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PromptBuiltIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "PromptTemplates",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Version 1 of every task is created only by the seed, so it is the shipped prompt.
            migrationBuilder.Sql("""UPDATE "PromptTemplates" SET "IsBuiltIn" = TRUE WHERE "Version" = 1;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "PromptTemplates");
        }
    }
}
