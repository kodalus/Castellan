using Castellan.Application;
using Castellan.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace Castellan.Infrastructure.Data;

internal sealed class UnitOfWork(CastellanDbContext db) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            FixNewChildrenMisdetectedAsModified();
            return await db.SaveChangesAsync(ct);
        }
        finally
        {
            // MAUI nie tworzy nowego DI scope per nawigacja, więc ten sam
            // CastellanDbContext żyje przez całą sesję aplikacji. Bez czyszczenia
            // trackera po każdym zapisie kolejne operacje mogłyby: (a) trafić na
            // encje śledzone sprzed importu z kopii zapasowej — surowe SQL w
            // BackupService omija tracker, więc wiersze usunięte/wstawione na nowo
            // nie są mu znane, co przy kolejnym DELETE/UPDATE po ID kończy się
            // DbUpdateConcurrencyException ("affected 0 rows"); (b) po nieudanym
            // zapisie pokazać niezapisaną, wycofaną w bazie zmianę jako "zapisaną"
            // przy powrocie na ten sam ekran, bo EF nie cofa automatycznie zmian
            // na już śledzonym obiekcie po rzuceniu wyjątku.
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// MonthBudget.Plan()/PlanIncome() replace their child collections wholesale —
    /// Remove() empties the list, then re-adds brand-new objects with fresh
    /// client-generated GUIDs. Since those objects are only reachable via the
    /// already-tracked MonthBudget's collection navigation (never through an explicit
    /// db.Add()), and their PK is already set, EF's graph-tracking heuristic can't
    /// tell "new" from "existing" and defaults to Modified — generating an UPDATE for
    /// a row that was never inserted, which SQLite reports as "0 rows affected".
    /// A child's MonthBudgetId never legitimately changes after creation (children are
    /// re-parented by delete+recreate, not by reassignment), so a Modified child whose
    /// FK shows as changed is really a brand-new one — nudge it to Added before saving.
    /// </summary>
    private void FixNewChildrenMisdetectedAsModified()
    {
        db.ChangeTracker.DetectChanges();

        foreach (var entry in db.ChangeTracker.Entries<Envelope>())
        {
            if (entry.State == EntityState.Modified
                && entry.Property(nameof(Envelope.MonthBudgetId)).IsModified)
                entry.State = EntityState.Added;
        }

        foreach (var entry in db.ChangeTracker.Entries<IncomePlan>())
        {
            if (entry.State == EntityState.Modified
                && entry.Property(nameof(IncomePlan.MonthBudgetId)).IsModified)
                entry.State = EntityState.Added;
        }
    }
}
