using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActivePublicationUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Publications_StoryId",
                table: "Publications");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_StoryId_ChannelId_Active",
                table: "Publications",
                columns: new[] { "StoryId", "ChannelId" },
                unique: true,
                filter: "\"Status\" IN ('Scheduled', 'Sending', 'Published')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Publications_StoryId_ChannelId_Active",
                table: "Publications");

            migrationBuilder.CreateIndex(
                name: "IX_Publications_StoryId",
                table: "Publications",
                column: "StoryId");
        }
    }
}
