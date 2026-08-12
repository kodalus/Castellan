using Castellan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Tests;

public class DatabaseCanBeCreatedTest
{
    [Fact]
    public void Migrate_creates_empty_database()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"castellan_test_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<CastellanDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var db = new CastellanDbContext(options))
            {
                db.Database.Migrate();
                db.Database.CanConnect().Should().BeTrue();
            }

            // Сбрасываем пул соединений, чтобы освободить WAL-файлы
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            File.Delete(dbPath);
            File.Delete(dbPath + "-wal");
            File.Delete(dbPath + "-shm");
        }
    }
}
