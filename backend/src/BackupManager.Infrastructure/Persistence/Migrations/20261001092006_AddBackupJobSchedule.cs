using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackupManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupJobSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ScheduleEnabled",
                table: "BackupJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleTime",
                table: "BackupJobs",
                type: "TEXT",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScheduleTimeZone",
                table: "BackupJobs",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SqlServerBackupDirectory",
                table: "BackupJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduleEnabled",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "ScheduleTime",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "ScheduleTimeZone",
                table: "BackupJobs");

            migrationBuilder.DropColumn(
                name: "SqlServerBackupDirectory",
                table: "BackupJobs");
        }
    }
}
