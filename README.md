# Castellan

Prywatna aplikacja do zarządzania budżetem domowym na Android (MAUI).

## Funkcje

| Etap | Funkcja |
|------|---------|
| 1 | Konta, ręczne dodawanie transakcji |
| 2 | Powiadomienia Android → automatyczne transakcje (ING, Revolut) |
| 3 | Kategoryzacja, reguły, transfery wewnętrzne, BLIK ING |
| 4 | Fundusze (oszczędności) z naliczaniem wg wypłat — ubezpieczenia, urlop, podatki |
| 5 | Aktywa i poduszka finansowa w miesiącach per poziom płynności |
| 6 | Eksport/import JSON — kopia zapasowa i przeniesienie danych |

### Szczegóły

- **Dashboard** — saldo wszystkich kont, skrót do bieżącego miesiąca
- **Konta** — konta bankowe i gotówkowe, reconciliacja salda
- **Transakcje** — lista, kategoryzacja ręczna i automatyczna
- **Koperty** — budżet miesięczny (metoda kopert), planowanie wydatków
- **Powiadomienia** — inbox z nieprzypisanymi transakcjami z powiadomień
- **Fundusze** — cel + termin, automatyczna kalkulacja wymaganej miesięcznej składki
- **Aktywa** — wartość netto w 4 poziomach płynności, autonomia w miesiącach
- **Kopia zapasowa** — eksport do JSON (Share Sheet), import z pliku

## Architektura

```
Castellan.Domain/         — agregaty, value objects, enumy (bez zewnętrznych zależności)
Castellan.Application/    — use cases, interfejsy repozytoriów, DTO
Castellan.Infrastructure/ — EF Core + SQLite, implementacje repozytoriów, parsery powiadomień
Castellan.App/            — MAUI, ViewModels (CommunityToolkit.Mvvm), Pages (XAML)
```

Czysta architektura: zależności wyłącznie do wewnątrz (Domain ← Application ← Infrastructure ← App).

## Stos technologiczny

- **.NET MAUI** — Android
- **EF Core + SQLite** — lokalna baza danych
- **CommunityToolkit.Mvvm** — `[ObservableProperty]`, `[RelayCommand]`, `[QueryProperty]`
- **System.Text.Json** — eksport/import danych

## Budowanie

```bash
dotnet build src/Castellan.App/Castellan.App.csproj -f net9.0-android
```

Wdrożenie na urządzenie:

```bash
dotnet run --project src/Castellan.App -f net9.0-android
```

## Baza danych

SQLite, lokalizacja: `FileSystem.AppDataDirectory/castellan.db`.

Migracje EF Core — stosowane automatycznie przy starcie (`Database.Migrate()`).

## Kopia zapasowa

Zakładka **Kopia** umożliwia:
- **Eksport** — serializuje wszystkie dane do JSON i udostępnia przez Android Share Sheet (Drive, e-mail, itp.)
- **Import** — wczytuje plik JSON z poprzedniego eksportu; **zastępuje wszystkie obecne dane**

Format pliku: `castellan_YYYYMMDD_HHmmss.json`, wersja schematu `v1`.
