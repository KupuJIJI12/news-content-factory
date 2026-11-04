using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsFactory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Sources");
        }
    }
}
