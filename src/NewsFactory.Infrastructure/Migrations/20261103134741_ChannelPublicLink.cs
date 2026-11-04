using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChannelPublicLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicName",
                table: "Channels",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicUrl",
                table: "Channels",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicName",
                table: "Channels");

            migrationBuilder.DropColumn(
                name: "PublicUrl",
                table: "Channels");
        }
    }
}
