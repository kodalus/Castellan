using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Castellan.Infrastructure.Data;

// Используется только dotnet-ef при генерации миграций, не в рантайме.
internal sealed class CastellanDbContextFactory : IDesignTimeDbContextFactory<CastellanDbContext>
{
    public CastellanDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CastellanDbContext>()
            .UseSqlite("Data Source=castellan_dev.db")
            .AddInterceptors(new SqlitePragmaInterceptor())
            .Options;

        return new CastellanDbContext(options);
    }
}
