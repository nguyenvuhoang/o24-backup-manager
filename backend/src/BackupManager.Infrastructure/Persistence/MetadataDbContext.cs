using BackupManager.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Text.Json;

namespace BackupManager.Infrastructure.Persistence;

public sealed class MetadataDbContext(DbContextOptions<MetadataDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseConnection> DatabaseConnections => Set<DatabaseConnection>();
    public DbSet<BackupJobRow> BackupJobs => Set<BackupJobRow>();
    public DbSet<BackupJobDatabaseRow> BackupJobDatabases => Set<BackupJobDatabaseRow>();
    public DbSet<MetadataState> MetadataStates => Set<MetadataState>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var c = model.Entity<DatabaseConnection>();
        c.ToTable("DatabaseConnections"); c.HasKey(x => x.Id); c.Ignore(x => x.PasswordSecret);
        c.OwnsOne(x => x.Settings, settings =>
        {
            foreach (var property in typeof(DatabaseConnectionSettings).GetProperties())
                settings.Property(property.Name).HasColumnName(property.Name);
            settings.Property(x => x.Name).IsRequired().HasMaxLength(200);
            settings.Property(x => x.Provider).IsRequired(); settings.Property(x => x.Host).IsRequired();
        });
        c.Navigation(x => x.Settings).IsRequired();
        var j = model.Entity<BackupJobRow>();
        j.ToTable("BackupJobs"); j.HasKey(x => x.Id);
        j.HasOne<DatabaseConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        j.HasMany(x => x.Databases).WithOne().HasForeignKey(x => x.BackupJobId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<BackupJobDatabaseRow>().ToTable("BackupJobDatabases").HasKey(x => new { x.BackupJobId, x.DatabaseName });
        model.Entity<MetadataState>().HasKey(x => x.Key);
    }
}

public sealed class BackupJobRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public Guid? ConnectionId { get; set; }
    public bool Enabled { get; set; }
    public string BackupDirectory { get; set; } = "";
    public int RetentionDays { get; set; }
    public string? SftpJson { get; set; }
    public string? TelegramJson { get; set; }
    // Read-only compatibility for imported jobs; new writes use ConnectionId.
    public string? LegacySqlServerJson { get; set; }
    public List<BackupJobDatabaseRow> Databases { get; set; } = [];
    public static BackupJobRow From(BackupJob job) => new()
    {
        Id = job.Id, Name = job.Name, ConnectionId = job.ConnectionId, Enabled = job.Enabled,
        BackupDirectory = job.BackupDirectory, RetentionDays = job.RetentionDays,
        SftpJson = job.Sftp is null ? null : JsonSerializer.Serialize(job.Sftp),
        TelegramJson = job.Telegram is null ? null : JsonSerializer.Serialize(job.Telegram),
        LegacySqlServerJson = job.ConnectionId is null && job.SqlServer is not null ? JsonSerializer.Serialize(job.SqlServer) : null,
        Databases = job.Databases.Distinct(StringComparer.Ordinal).Select(n => new BackupJobDatabaseRow { BackupJobId = job.Id, DatabaseName = n }).ToList()
    };
    public BackupJob ToDomain() => new()
    {
        Id = Id, Name = Name, ConnectionId = ConnectionId, Enabled = Enabled,
        BackupDirectory = BackupDirectory, RetentionDays = RetentionDays,
        Sftp = SftpJson is null ? null : JsonSerializer.Deserialize<SftpOptions>(SftpJson),
        Telegram = TelegramJson is null ? null : JsonSerializer.Deserialize<TelegramOptions>(TelegramJson),
        SqlServer = LegacySqlServerJson is null ? null : JsonSerializer.Deserialize<SqlServerOptions>(LegacySqlServerJson),
        Databases = Databases.Select(x => x.DatabaseName).ToArray()
    };
}
public sealed class BackupJobDatabaseRow
{
    public Guid BackupJobId { get; set; }
    public string DatabaseName { get; set; } = "";
}
public sealed class MetadataState { public string Key { get; set; } = ""; }

public sealed class MetadataDesignFactory : IDesignTimeDbContextFactory<MetadataDbContext>
{
    public MetadataDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<MetadataDbContext>()
        .UseSqlite("Data Source=data/backupmanager.db").Options);
}
