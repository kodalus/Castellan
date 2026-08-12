using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Data;

public sealed class CastellanDbContext : DbContext
{
    public CastellanDbContext(DbContextOptions<CastellanDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CastellanDbContext).Assembly);
    }
}
