using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackupManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatabaseConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: true),
                    InstanceName = table.Column<string>(type: "TEXT", nullable: true),
                    UseNamedInstanceDiscovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    AuthenticationType = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: true),
                    Database = table.Column<string>(type: "TEXT", nullable: true),
                    Encrypt = table.Column<bool>(type: "INTEGER", nullable: false),
                    TrustServerCertificate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectionTimeout = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandTimeout = table.Column<int>(type: "INTEGER", nullable: false),
                    ApplicationName = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedPassword = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataStates",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataStates", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BackupDirectory = table.Column<string>(type: "TEXT", nullable: false),
                    RetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    SftpJson = table.Column<string>(type: "TEXT", nullable: true),
                    TelegramJson = table.Column<string>(type: "TEXT", nullable: true),
                    LegacySqlServerJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupJobs_DatabaseConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "DatabaseConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BackupJobDatabases",
                columns: table => new
                {
                    BackupJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobDatabases", x => new { x.BackupJobId, x.DatabaseName });
                    table.ForeignKey(
                        name: "FK_BackupJobDatabases_BackupJobs_BackupJobId",
                        column: x => x.BackupJobId,
                        principalTable: "BackupJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_ConnectionId",
                table: "BackupJobs",
                column: "ConnectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupJobDatabases");

            migrationBuilder.DropTable(
                name: "MetadataStates");

            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropTable(
                name: "DatabaseConnections");
        }
    }
}
