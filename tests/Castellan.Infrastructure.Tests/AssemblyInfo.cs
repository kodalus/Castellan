using Xunit;

// Testy chodzą po prawdziwych plikach SQLite i sprzątają przez
// SqliteConnection.ClearAllPools() — a to działa na cały proces, nie na jeden plik.
// Przy domyślnym zrównolegleniu xUnit (każda klasa to osobna kolekcja) sprzątanie
// jednej klasy zamykało połączenie, którego druga właśnie używała: raz na kilka
// przebiegów wywracał się losowy, zawsze inny test. Szeregowo cały zestaw i tak
// schodzi w kilka sekund, a wynik przestaje być loterią.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
