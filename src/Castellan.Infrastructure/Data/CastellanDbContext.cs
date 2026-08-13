using Castellan.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Data;

public sealed class CastellanDbContext : DbContext
{
    public CastellanDbContext(DbContextOptions<CastellanDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MonthBudget> MonthBudgets => Set<MonthBudget>();
    public DbSet<Reconciliation> Reconciliations => Set<Reconciliation>();
    public DbSet<RawNotification> RawNotifications => Set<RawNotification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CastellanDbContext).Assembly);
    }
}
