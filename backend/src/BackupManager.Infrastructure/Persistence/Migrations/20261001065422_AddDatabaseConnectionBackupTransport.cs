using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackupManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseConnectionBackupTransport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackupTransportJson",
                table: "DatabaseConnections",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackupTransportJson",
                table: "DatabaseConnections");
        }
    }
}
