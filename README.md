# Castellan

Prywatna aplikacja do budżetu domowego na Androida. Offline, jeden użytkownik, bez backendu i bez konta — dane nie opuszczają telefonu.

Metoda: budżet kopertowy oparty na dostępnych środkach. Założenie, wokół którego zbudowana jest całość: **aplikacja nie może polegać na pamięci użytkownika**. Stąd wyłapywanie transakcji z powiadomień bankowych zamiast proszenia o ich wpisywanie.

Pełny przewodnik po działaniu aplikacji jest **w niej samej** — zakładka **Pomoc** (pod trzema kropkami na pasku). Ten plik opisuje projekt od strony technicznej.

## Moduły

| Obszar | Co robi |
|--------|---------|
| Konta | Konta rozliczeniowe i oszczędnościowe, uzgadnianie salda z bankiem |
| Transakcje | Ręczne, z powiadomień i z uzgodnienia; kategorie, reguły, przelewy wewnętrzne |
| Koperty | Plan miesiąca — podział dostępnych środków na kategorie |
| Przychody | Plan kontra faktyczne wpływy z każdego źródła |
| Skrzynka | Nieprzypisane transakcje i propozycje przelewów do rozstrzygnięcia |
| Fundusze | Cel i termin, rata liczona na wypłaty, pokrywanie wydatków z odłożonych pieniędzy |
| Zobowiązania | Salda kredytów, raty, plan spłaty metodą kuli śnieżnej |
| Majątek | Poduszka finansowa w miesiącach wg czterech poziomów płynności, wartość netto |
| Statystyki | Sześć miesięcy wydatków, przychodów i największych kategorii |
| Kopia | Eksport i import JSON |
| Pomoc | Przewodnik po wszystkich ścieżkach, w aplikacji |

### Wyłapywanie transakcji z powiadomień

Nasłuch przez `NotificationListenerService`. Obsługiwane pakiety:

| Pakiet | Źródło |
|--------|--------|
| `pl.ing.mojeing` | ING |
| `com.revolut.revolut` | Revolut |
| `com.google.android.apps.walletnfcrel` | Portfel Google (płatności NFC telefonem) |

Powiadomienia z pozostałych aplikacji są odrzucane. Przed zapisem treść jest maskowana — ciągi 4–8 cyfr (numery kart i rachunków) zamieniane na `****`.

Ścieżka: powiadomienie → parser danego banku → dopasowanie konta (po nazwie zawierającej „ING” / „Revolut”, dla Portfela Google po podpowiedzi z treści, w ostateczności pierwsze konto rozliczeniowe) → reguła kategorii (wygrywa najdłuższy wzorzec, remis rozstrzyga liczba trafień) → odrzucanie duplikatów → wykrycie przelewu wewnętrznego.

**Odrzucanie duplikatów** działa dwutorowo, bo jedna płatność bywa zgłoszona przez dwa źródła naraz:
- po kluczu sprzedawcy i kwocie — okno 25 h, tolerancja kwoty 2%;
- po samej kwocie co do grosza na tym samym koncie w oknie 15 minut — dla par Portfel Google plus bank, które podają zupełnie inne nazwy sprzedawcy („JMP S.A. BIEDRONKA 591” kontra „Biedronka”).

Blokada autoryzacyjna zastąpiona późniejszym właściwym obciążeniem zostaje w historii jako `Superseded` i wypada z budżetu.

**Wykrywanie przelewów wewnętrznych**: przeciwna kwota na innym koncie w oknie 48 h daje propozycję do potwierdzenia w Skrzynce. Potwierdzone przelewy są wykluczone z kopert i z przychodów.

## Architektura

```
Castellan.Domain/         — agregaty, value objects, enumy (zero zależności zewnętrznych)
Castellan.Application/    — use case'y, interfejsy repozytoriów, DTO, parsery powiadomień
Castellan.Infrastructure/ — EF Core + SQLite, repozytoria, implementacje parserów, kopia zapasowa
Castellan.App/            — MAUI: ViewModels (CommunityToolkit.Mvvm), Pages (XAML), kod Androida
```

Czysta architektura, zależności wyłącznie do wewnątrz: Domain ← Application ← Infrastructure ← App.

Kwoty trzymane są jako `Money` (grosze w `long`) — nigdy jako `decimal` ani `double`. Identyfikatory to typowane struktury (`AccountId`, `TransactionId`, …) generowane przez `Guid.CreateVersion7()`.

Trzy rzeczy wypadają z budżetu miesiąca (`Transaction.IsExcludedFromCalculations`): przelewy wewnętrzne, transakcje pokryte z funduszu i zastąpione autoryzacje.

## Stos technologiczny

- **.NET MAUI** — `net10.0-android36.0`, minimalne API 29
- **EF Core + SQLite** — baza lokalna, migracje stosowane przy starcie
- **CommunityToolkit.Mvvm** — `[ObservableProperty]`, `[RelayCommand]`, `[QueryProperty]`
- **System.Text.Json** — eksport i import
- **xUnit + FluentAssertions** — testy

## Budowanie

```bash
dotnet build
```

Testy:

```bash
dotnet test
```

Instalacja na podpiętym urządzeniu:

```bash
dotnet run --project src/Castellan.App -f net10.0-android36.0
```

Plik APK do ręcznej instalacji:

```bash
dotnet publish src/Castellan.App -c Release -f net10.0-android36.0 -p:AndroidPackageFormats=apk
```

Wynik trafia do `src/Castellan.App/bin/Release/net10.0-android36.0/publish/dev.castellan.app-Signed.apk`.

## Baza danych

SQLite w `FileSystem.AppDataDirectory/castellan.db`. Migracje EF Core wykonują się automatycznie przy starcie (`Database.Migrate()`).

Nowa migracja:

```bash
dotnet ef migrations add NazwaMigracji --project src/Castellan.Infrastructure
```

Projekt `Castellan.Infrastructure` ma `IDesignTimeDbContextFactory`, więc nie trzeba podawać `--startup-project` (i nie da się — projekt MAUI nie wystawia `deps.json` dla narzędzi EF).

Przy pierwszym uruchomieniu zakładany jest zestaw kategorii domowych. Kategorie dodane w późniejszych wersjach są dokładane także do istniejących baz, z pominięciem tych, które użytkownik zarchiwizował.

## Wygląd

Paleta jest wyłącznie ciemna — aplikacja wymusza tryb ciemny, więc nigdzie nie ma `AppThemeBinding`.

- `Resources/Styles/Colors.xaml` — kamienne tło plus cztery akcenty, z których każdy coś znaczy: mosiądz to akcja i stan bieżący, zieleń to przychód i „na bieżąco”, koral to przekroczenie i dług, stal to fundusze i majątek. Kolor nigdy nie jest dekoracją.
- `Resources/Styles/Styles.xaml` — sześciostopniowa skala rozmiarów tekstu i style współdzielone przez ekrany. W widokach nie powinno być twardych kolorów ani liczbowych `FontSize`.
- `Resources/Styles/Palette.cs` — dostęp do tych samych kolorów z kodu (wykresy, konwertery).

Natywne powierzchnie Androida — arkusz „więcej”, alerty, wybór daty — nie widzą zasobów MAUI. Ich kolory pochodzą z `Platforms/Android/Resources/values/styles.xml`, a tryb nocny jest wymuszany w `MainApplication`, bo `Theme.MaterialComponents.DayNight` patrzy na ustawienie systemowe, nie na ustawienie aplikacji.

Ikony zakładek to jednokolorowe obrysy SVG 24×24 w `Resources/Images/tab_*.svg` — pasek sam barwi je mosiądzem na wybranej pozycji.

## Kopia zapasowa

Zakładka **Kopia**:
- **Eksport** — serializuje wszystko do JSON i udostępnia przez Android Share Sheet (Dysk, mail, cokolwiek).
- **Import** — wczytuje plik i **zastępuje nim wszystkie obecne dane**; operacji nie da się cofnąć.

Nazwa pliku: `castellan_RRRRMMDD_GGmmss.json`; schemat niesie pole `Version`, obecnie `1`. Kopie sprzed dodania jakiegoś modułu wczytują się poprawnie — brakujące sekcje są traktowane jako puste.

## Dokumentacja

`docs/castellan-spec.md` — specyfikacja techniczna i uzasadnienia decyzji projektowych.

## Licencja

Patrz [LICENSE](LICENSE).
