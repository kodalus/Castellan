# Castellan — specyfikacja techniczna

Aplikacja do zarządzania budżetem domowym. Android, offline, jeden użytkownik, bez backendu.

Dokument roboczy, po polsku. Publiczne README pisane osobno.

---

## 1. Przegląd

### 1.1 Zadanie

Przenieść na telefon metodę budżetu kopertowego opartego na dostępnych środkach (sprawdzona w praktyce w Excelu, działała) i wyeliminować jedyną przyczynę, dla której metoda została porzucona: **tarcie przy wprowadzaniu transakcji**.

Sformułowanie problemu dosłownie: wieczorem nie pamięta się, na co poszły pieniądze. Zadanie to nie „przypominaj o zapisaniu", lecz „nie wymagaj pamięci".

### 1.2 Kluczowa zasada

> Aplikacja nie powinna polegać na pamięci użytkownika.

Wszystkie wydatki są płacone kartą, BLIK-iem lub online — gotówki nie ma. Każda transakcja generuje powiadomienie bankowe zawierające kwotę i nazwę sprzedawcy. Sprzedawca pamięta za użytkownika. Z tego wynika architektura: **głównym źródłem danych jest przechwytywanie powiadomień, ręczne wprowadzanie jest awaryjne**.

### 1.3 Druga zasada

> Pominięcie danych nie powinno psuć systemu.

Jedynym źródłem prawdy o saldzie jest uzgodnienie z faktycznym stanem konta, a nie suma zapisanych transakcji. Wszystko niezapisane automatycznie trafia do kategorii „Nierozpoznane". Zapominalstwo zwiększa jedną liczbę, ale nie przekreśla miesiąca.

### 1.4 Ograniczenia

Nigdy nie wchodzi w zakres: backend, konta użytkowników, synchronizacja w chmurze, drugi użytkownik, wielowalutowość, integracja z API banków, publikacja w Google Play (zob. 15.3).

---

## 2. Etapy

| Etap | Zawartość | Rezultat |
|---|---|---|
| 0 | Szkielet rozwiązania, BD, testy, CI | Pusta aplikacja uruchamia się na telefonie |
| 1 | Konta, kategorie, transakcje, budżet miesiąca | Działające ręczne prowadzenie budżetu |
| 2 | Uzgodnienie, „Nierozpoznane", szybkie wprowadzanie, widget | Budżet przeżywający pominięcia |
| 3 | Przechwytywanie powiadomień, skrzynka odbiorcza, deduplikacja, autokategoryzacja | Budżet bez ręcznego wprowadzania — stan docelowy |
| 4 | Fundusze nieregularnych płatności | Ubezpieczenie, podatki, urlop |
| 5 | Aktywa, płynność, poduszka finansowa | Odpowiedź na pytanie „na ile wystarczy" |
| 6 | Backup, eksport, publiczne README | Projekt nadaje się do portfolio |
| 7 | Zobowiązania, plan spłaty, majątek netto | Dług przestaje być tematem omijanym |
| 8 | Jeden system stylów, ikony, przewodnik w aplikacji | Aplikacja do oddania w cudze ręce |

Etapy 7–8 dołożone po wdrożeniu pierwszych sześciu. Poza tabelą doszły też: planowanie
przychodów obok kopert, statystyki sześciu miesięcy oraz wybór trybu przechwytywania
(powiadomienia albo pełne wprowadzanie ręczne) — ten ostatni dlatego, że aplikacji
zaczęła używać osoba bez powiadomień bankowych, dla której ostrzeżenie o ich braku
było stałym elementem ekranu.

**Ważne ostrzeżenie dotyczące etapów 1–2.** Dają ręczne wprowadzanie — to samo narzędzie, które zostało już porzucone. Trzeba je przejść szybko i nie próbować „żyć" z nimi: prawdziwa codzienna eksploatacja zaczyna się od etapu 3. Jeśli między etapem 2 a etapem 3 powstanie przerwa miesięczna, istnieje ryzyko rozczarowania projektem zanim zacznie działać zgodnie z przeznaczeniem.

---

## 3. Decyzje techniczne

### 3.1 Platforma

**.NET MAUI, .NET 10 (LTS), target Android 16 (API 36), minimum Android 10 (API 29).**

Uzasadnienie:

- C# + XAML + MVVM — znane środowisko od 2013 roku.
- Bezpośredni dostęp do Android API przez bindingi .NET for Android — warunek konieczny dla `NotificationListenerService` (etap 3).
- Jedyna docelowa platforma to Android. Wieloplatformowość nie jest potrzebna, ale nie przeszkadza.

Odrzucone:

- **Avalonia** — porównywalny UI, ale platformowe serwisy Androida wymagają więcej ręcznego opakowania; brak zysku.
- **Angular + Capacitor** — dostęp do powiadomień przez plugin, czyli cudzy kod w najważniejszym miejscu projektu. Sprzeczne z celem.
- **Kotlin / natywny Android** — lepszy dostęp do platformy, ale nauka języka i ekosystemu pochłonie projekt.

### 3.2 Przechowywanie danych

**EF Core 10 + SQLite**, plik BD w `FileSystem.AppDataDirectory`.

- Migracje EF Core stosowane przy starcie (`Database.Migrate()`).
- Odrzucono `sqlite-net-pcl`: łatwiejszy na początku, ale brak migracji i brak przeniesienia wiedzy na roboczy stos.

### 3.3 Pieniądze

**Przechowywać jako liczby całkowite w groszach (`long`).** SQLite nie ma typu `decimal`, EF Core konwertuje go na `TEXT` lub `REAL`; drugie daje błędy zaokrąglenia w sumach.

Typ `Money` — value object nad `long Grosze`. Waluta jedna (PLN), nie wynosić do modelu.

### 3.4 Identyfikatory

`Guid` wersji 7 (`Guid.CreateVersion7()`), monotonicznie rosnące — nie fragmentują indeksów SQLite, w przeciwieństwie do v4.

### 3.5 Czas

`DateTimeOffset` wszędzie, przechowywanie w ISO-8601 (`TEXT`). Lokalna strefa — Europe/Warsaw. Granice miesiąca liczone w lokalnej strefie, nie w UTC (inaczej transakcja 1. dnia o 00:30 trafi do poprzedniego miesiąca).

### 3.6 Biblioteki

| Przeznaczenie | Wybór |
|---|---|
| MVVM | `CommunityToolkit.Mvvm` (source generators) |
| DI | wbudowany `Microsoft.Extensions.DependencyInjection` |
| Logowanie | `Microsoft.Extensions.Logging.Debug` — **tylko** w kompilacji DEBUG |
| Testy | `xUnit`, `FluentAssertions` |
| Serializacja | `System.Text.Json` |

Mediator (MediatR i odpowiedniki) nie jest używany: scenariuszy jest mało, dodatkowa warstwa ukrywa przepływ sterowania, który trzeba umieć wyjaśnić.

Dostawca plikowy z rotacją nie powstał. Wydanie nie pisze żadnych logów, a diagnostyka
opiera się na tabeli `RawNotifications` (nieparsowalne powiadomienia trafiają tam
z redakcją) i na `Android.Util.Log` w nasłuchu. Jest to zgodne z 15.6: plik logów nie
jest chroniony piaskownicą, więc jego brak upraszcza sprawę zamiast ją komplikować.

---

## 4. Architektura

### 4.1 Projekty

```
Castellan.sln
├── src/
│   ├── Castellan.Domain/           bez zewnętrznych zależności
│   ├── Castellan.Application/      → Domain
│   ├── Castellan.Infrastructure/   → Domain, Application (EF Core, parsery)
│   └── Castellan.App/              → wszystko (MAUI, XAML, ViewModels, serwisy Android)
└── tests/
    ├── Castellan.Domain.Tests/
    ├── Castellan.Application.Tests/
    └── Castellan.Infrastructure.Tests/
```

Reguła zależności: ściśle do wewnątrz. `Castellan.Domain` nie odwołuje się do niczego, łącznie z EF Core.

### 4.2 Warstwy

- **Domain** — agregaty, value objects, niezmienniki, serwisy domenowe (czyste obliczenia). Tu żyje wszystko, co warto chronić podczas code review.
- **Application** — scenariusze (use cases), interfejsy repozytoriów, DTO. Jedna klasa na scenariusz, metoda `ExecuteAsync`.
- **Infrastructure** — `DbContext`, konfiguracje, implementacje repozytoriów, parsery powiadomień, backup plikowy.
- **App** — MAUI: strony, ViewModel, konwertery, kod platformowy Android (`Platforms/Android/`).

### 4.3 Persystencja agregatów

Repozytorium na agregat, nie na tabelę:

```csharp
public interface IAccountRepository
{
    Task<Account?> GetAsync(AccountId id, CancellationToken ct);
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct);
    Task AddAsync(Account account, CancellationToken ct);
}
```

`SaveChangesAsync` wywoływane przez scenariusz, nie przez repozytorium (`IUnitOfWork`).

---

## 5. Model domenowy

### 5.1 Value objects

| Typ | Zawartość | Zasady |
|---|---|---|
| `Money` | `long Grosze` | arytmetyka, porównanie, `Abs`, `IsNegative`; formatowanie `#,##0.00 zł` |
| `YearMonth` | `int Year, int Month` | `Contains(DateTimeOffset)`, `Next()`, `Previous()`, granice w strefie lokalnej |
| `MerchantKey` | `string` | znormalizowana nazwa sprzedawcy (zob. 11.3) |
| `Percentage` | `decimal` | 0..1 |

Znak kwoty: **wydatek ujemny, przychód dodatni**. Jednolita reguła w całym systemie, bez wyjątków w warstwie UI.

### 5.2 Account (agregat)

| Pole | Typ | Uwaga |
|---|---|---|
| `Id` | `AccountId` | |
| `Name` | `string` | |
| `BankKey` | `string` | klucz zestawu reguł parsowania, etap 3 |
| `Kind` | `AccountKind` | `Checking`, `Savings` |
| `LiquidityTier` | `LiquidityTier` | `Immediate`, `Month`, `Locked` — etap 5, domyślnie `Immediate` |
| `LastReconciledBalance` | `Money` | |
| `LastReconciledAt` | `DateTimeOffset` | |
| `IsArchived` | `bool` | konta nie są usuwane |

Bieżące saldo nie jest przechowywane. Obliczane jest jako:

```
CurrentBalance = LastReconciledBalance + Σ Transaction.Amount, gdzie OccurredAt > LastReconciledAt
```

### 5.3 Category (agregat)

| Pole | Typ |
|---|---|
| `Id` | `CategoryId` |
| `Name` | `string` |
| `Kind` | `Expense` \| `Income` |
| `IsSystem` | `bool` |
| `IsArchived` | `bool` |

Kategorie systemowe, tworzone przez migrację, nie są usuwane ani zmieniane:

- **`Unsorted`** — „Nieprzypisane": transakcja przechwycona, kategoria nieprzypisana.
- **`Unidentified`** — „Nierozpoznane": rozbieżność wykryta podczas uzgodnienia.
- **`Transfer`** — „Przelew": techniczna, wykluczona ze wszystkich sum.

### 5.4 Transaction (agregat)

| Pole | Typ | Uwaga |
|---|---|---|
| `Id` | `TransactionId` | |
| `AccountId` | `AccountId` | |
| `Amount` | `Money` | znak według zasady 5.1 |
| `OccurredAt` | `DateTimeOffset` | |
| `CategoryId` | `CategoryId` | nigdy null; przy przechwyceniu — `Unsorted` |
| `RawMerchant` | `string?` | surowy ciąg z powiadomienia |
| `MerchantKey` | `MerchantKey?` | znormalizowany |
| `Note` | `string?` | |
| `Source` | `Manual` \| `Notification` \| `Reconciliation` | |
| `Kind` | `Regular` \| `Authorization` \| `Transfer` \| `Unidentified` | |
| `TransferGroupId` | `Guid?` | łączy obie strony potwierdzonego przelewu |
| `ProposedTransferGroupId` | `Guid?` | łączy parę czekającą na rozstrzygnięcie w skrzynce |
| `SupersededById` | `TransactionId?` | autoryzacja scalona z obciążeniem |
| `RawNotificationId` | `Guid?` | odniesienie do źródłowego powiadomienia |
| `PaidFromFundId` | `FundId?` | wydatek pokryty z funduszu |

**Odejście od pierwotnego założenia.** Spec zakładał transakcję niemodyfikowalną poza
kategorią i notatką: błędny wpis miał być usuwany i wprowadzany ponownie, żeby historia
uzgodnień pozostała odtwarzalna. W praktyce najczęstsza poprawka to literówka w kwocie
zaraz po wpisaniu, a usuwanie i wpisywanie od nowa okazało się karą za pomyłkę.
`UpdateTransactionUseCase` edytuje dziś konto, kwotę, datę, kategorię i notatkę.
Odtwarzalność historii uzgodnień zabezpiecza co innego: uzgodnienie patrzy wyłącznie
na okno od poprzedniego uzgodnienia (N-6), więc edycja starszej transakcji nie
przepisuje przeszłych rozliczeń.

**Wykluczone z obliczeń** (`IsExcludedFromCalculations`): `Kind == Transfer`,
`SupersededById != null`, `PaidFromFundId != null`. Trzeci przypadek doszedł wraz
z funduszami: odpisy obciążyły koperty w poprzednich miesiącach, więc sama zapłata
nie może obciążyć budżetu drugi raz.

### 5.5 MonthBudget (agregat)

| Pole | Typ |
|---|---|
| `Id` | `MonthBudgetId` |
| `Month` | `YearMonth` |
| `AvailableFunds` | `Money` — migawka dostępnych środków w momencie planowania |
| `Envelopes` | `List<Envelope>` |
| `IncomePlans` | `List<IncomePlan>` |
| `PlannedAt` | `DateTimeOffset` |

`Envelope` (encja wewnątrz agregatu): `CategoryId`, `PlannedAmount`.
`IncomePlan` (encja wewnątrz agregatu): `CategoryId`, `PlannedAmount`.

Metody agregatu: `Plan(categoryId, amount)`, `Remove(categoryId)`, `RefreshAvailableFunds(money)`,
`PlanIncome(categoryId, amount)`, `RemoveIncome(categoryId)`. Metody kopertowe sprawdzają
niezmiennik N-1 i rzucają `BudgetOverAllocatedException` przy naruszeniu.

**Dlaczego plan przychodu to osobna encja, a nie koperta.** Koperty dzielą pulę
`AvailableFunds` i podlegają N-1. Plan przychodu jest przewidywaniem wpływu — gdyby
trafił do tej samej listy, zwiększałby limit do rozdzielenia o pieniądze, których
jeszcze nie ma.

### 5.6 Reconciliation (agregat)

| Pole | Typ |
|---|---|
| `Id` | `ReconciliationId` |
| `AccountId` | `AccountId` |
| `ObservedBalance` | `Money` |
| `ObservedAt` | `DateTimeOffset` |
| `PreviousBalance` | `Money` |
| `PreviousAt` | `DateTimeOffset` |
| `RecordedDelta` | `Money` — suma transakcji między uzgodnieniami |
| `Discrepancy` | `Money` — rozbieżność |
| `GeneratedTransactionId` | `TransactionId?` |

### 5.7 CategoryRule (agregat) — etap 3

| Pole | Typ |
|---|---|
| `Id` | `Guid` |
| `Pattern` | `string` — podciąg znormalizowanej nazwy sprzedawcy |
| `CategoryId` | `CategoryId` |
| `Origin` | `Learned` \| `Manual` |
| `HitCount` | `int` |
| `LastUsedAt` | `DateTimeOffset?` |

Przy konflikcie reguł wygrywa **najdłuższy** `Pattern`; przy równej długości — z większym `HitCount`.

### 5.8 RawNotification — etap 3

| Pole | Typ |
|---|---|
| `Id` | `Guid` |
| `PackageName` | `string` |
| `Title`, `Text` | `string` |
| `PostedAt` | `DateTimeOffset` |
| `ParseStatus` | `Parsed` \| `Unparsed` \| `Ignored` |
| `TransactionId` | `TransactionId?` |

Przechowywane po redakcji tekstu wyłącznie dla powiadomień z białej listy pakietów, łącznie z nierozpoznanymi. Powiadomienia spoza białej listy nie są nigdy zapisywane. To materiał do doskonalenia parserów i zabezpieczenie przed utratą danych.

### 5.9 Fund (agregat) — etap 4

| Pole | Typ | Uwaga |
|---|---|---|
| `Id` | `FundId` | |
| `Name` | `string` | „OC+AC", „Podatek od nieruchomości", „Urlop" |
| `Kind` | `Insurance` \| `Vacation` \| `Tax` \| `Custom` | |
| `TargetAmount` | `Money` | kwota do uzbierania |
| `StartMonth` | `DateOnly` | pierwszy dzień miesiąca założenia — kotwica wyliczeń |
| `Deadline` | `DateOnly` | pierwszy dzień miesiąca, na kiedy potrzebne są pieniądze |
| `Balance` | `Money` | zgromadzone |
| `LastContributionMonth` | `DateOnly?` | miesiąc ostatniej wpłaty |
| `IsArchived` | `bool` | |

Operacje: `Contribute(money)`, `Withdraw(money)`, `Update(...)`, `Archive()`.

**Odejście od pierwotnego założenia.** Spec zakładał fundusz cykliczny: okresowość plus
data następnej płatności, a `Spend` zerował zgromadzone i przesuwał termin. Wdrożony
model jest jednorazowy — cel plus termin — bo realne przypadki (OC auto, podatek,
urlop) mają różne kwoty w kolejnych latach, a odnawianie funduszu tą samą kwotą
zakłamywałoby ratę. Zapłata odbywa się dziś przez powiązanie istniejącego wydatku
z funduszem (`PaidFromFundId`), nie przez metodę `Spend`.

`Update` celowo nie rusza `Balance` ani `StartMonth`: saldo zmienia się wyłącznie
wpłatami, a `StartMonth` jest kotwicą wyliczenia „ile powinno być odłożone do teraz" —
przesunięcie go zafałszowałoby historię opóźnień.

`LastContributionMonth` doszedł po zgłoszeniu z eksploatacji: bez niego bieżący okres
liczył się jako niezrobiony aż do dnia wypłaty, więc rata przeliczała się na nowo zaraz
po wpłacie, tak jakby trzeba było dołożyć drugi raz.

### 5.10 Asset (agregat) — etap 5

| Pole | Typ |
|---|---|
| `Id` | `AssetId` |
| `Name` | `string` |
| `Liquidity` | `Immediate` \| `Fast` \| `Medium` \| `Slow` |
| `Value` | `Money` |
| `UpdatedOn` | `DateOnly` |
| `IsArchived` | `bool` |

Poziomów płynności są cztery, nie trzy jak zakładał pierwotny spec: między „dostępne
jutro" a „zamrożone" mieści się zbyt wiele (obligacje, fundusze inwestycyjne, lokaty
z karą), żeby wrzucać to do jednego worka. Nazwy pokazywane użytkownikowi mówią o
czasie, nie o kategorii: „Natychmiastowa", „Szybka (1–3 dni)", „Średnia (tygodnie)",
„Wolna (miesiące)".

Pole `IsInMonthlyBudget` nie powstało — aktywa z założenia nie wchodzą do budżetu
miesiąca, a flaga „z wyjątkiem szczególnych przypadków" nie miała ani jednego
zastosowania.

### 5.11 Debt (agregat) — etap 7

| Pole | Typ | Uwaga |
|---|---|---|
| `Id` | `DebtId` | |
| `Name` | `string` | |
| `Kind` | `Mortgage` \| `CashLoan` \| `Installment` \| `FromFamily` \| `Other` | |
| `InitialAmount` | `Money` | punkt odniesienia paska postępu |
| `Balance` | `Money` | pozostało do spłaty |
| `InstallmentAmount` | `Money` | rata miesięczna; zero znaczy brak harmonogramu |
| `IsArchived` | `bool` | |

Operacje: `Pay(money)`, `SetBalance(money)`, `Update(...)`, `Archive()`.

Lustrzane odbicie funduszu: saldo maleje do zera zamiast rosnąć do celu. `Pay` przycina
saldo do zera — ujemny dług nie ma sensu, a nadpłata jest normalną sytuacją.

Termin spłaty nie jest przechowywany, tylko liczony z bieżącego salda i raty
(`ProjectedPayoff`). Dzięki temu nadpłata natychmiast przesuwa datę wyjścia na zero,
zamiast zostawiać nieaktualną datę z umowy. `InstallmentsRemaining` jest `null` przy
racie równej zeru — bez harmonogramu nie ma z czego policzyć odliczania.

Odsetki nie są modelowane. Rozjazd salda względem sumy zapłaconych rat koryguje się
ręcznie przez `SetBalance`.

---

## 6. Niezmienniki

| Nr | Sformułowanie | Gdzie sprawdzane |
|---|---|---|
| **N-1** | `Σ Envelope.PlannedAmount ≤ MonthBudget.AvailableFunds` | `MonthBudget.Plan()` |
| **N-2** | Transakcja zawsze ma kategorię; nieprzypisana otrzymuje `Unsorted` i **uczestniczy** w sumach wydatków | konstruktor `Transaction` |
| **N-3** | Obie strony przelewu wewnętrznego mają wspólny `TransferGroupId` i są wykluczone z wydatków i przychodów | `IngestRawNotificationUseCase`, `ConfirmTransferUseCase` |
| **N-4** | Scalona autoryzacja ma `SupersededById` i nie uczestniczy w obliczeniach; wygrywa obciążenie | `IngestRawNotificationUseCase` |
| **N-5** | **Dodatnia** rozbieżność przy uzgodnieniu nie tworzy automatycznie przychodu — wymaga jawnej decyzji użytkownika. Ujemna tworzy wydatek `Nierozpoznane` | `ReconcileAccountUseCase` |
| **N-6** | Uzgodnienie nie modyfikuje przeszłych transakcji, tylko dodaje nową; patrzy wyłącznie na okno od poprzedniego uzgodnienia | `ReconcileAccountUseCase` |
| **N-7** | `Debt.Balance ≥ 0` — nadpłata zeruje dług, nie schodzi poniżej | `Debt.Pay()` |
| **N-8** | Konta i kategorie nie są usuwane, lecz archiwizowane: mają historię | repozytoria |

**Sprostowanie do N-5.** Pierwotne sformułowanie mówiło o rozbieżności ujemnej, co było
odwróceniem sensu. Decyzji wymaga nadwyżka: pieniędzy jest więcej, niż wynika z zapisów,
co znaczy albo niezapisany wpływ, albo policzony podwójnie wydatek — i tylko użytkownik
wie który. Niedobór jest jednoznaczny (wydatek, o którym aplikacja nie wiedziała), więc
zapisuje się sam.

**Sprostowanie do N-7.** Fundusze nie doczekały się przycięcia salda ani limitu
`TargetAmount`: cel bywa przekroczony celowo, a `Withdraw` służy pokrywaniu wydatków,
więc blokada przeszkadzałaby. Przycięcie do zera obowiązuje za to przy zobowiązaniach.

**Sprostowanie do N-8.** Fundusze i zobowiązania **są** usuwane, nie archiwizowane.
Usunięcie funduszu odpina powiązane transakcje (`PaidFromFundId = null`), więc wracają
one do kopert — inaczej zostałyby wykluczone z budżetu ze wskaźnikiem donikąd. Ekran
usuwania wypisuje wprost obie konsekwencje przed potwierdzeniem.

Niezmiennik N-1 jest centralny. Właśnie jego brak w Excelu pozwalał na planowy deficyt, który można było zignorować. W aplikacji operacja naruszająca zostaje odrzucona.

---

## 7. Schemat BD

```sql
CREATE TABLE Accounts (
    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, BankKey TEXT NULL,
    Kind INTEGER NOT NULL, LiquidityTier INTEGER NOT NULL DEFAULT 0,
    LastReconciledBalance INTEGER NOT NULL, LastReconciledAt TEXT NOT NULL,
    IsArchived INTEGER NOT NULL DEFAULT 0);

CREATE TABLE Categories (
    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, Kind INTEGER NOT NULL,
    IsSystem INTEGER NOT NULL DEFAULT 0, IsArchived INTEGER NOT NULL DEFAULT 0);

CREATE TABLE Transactions (
    Id TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    Amount INTEGER NOT NULL, OccurredAt TEXT NOT NULL,
    CategoryId TEXT NOT NULL REFERENCES Categories(Id),
    RawMerchant TEXT NULL, MerchantKey TEXT NULL, Note TEXT NULL,
    Source INTEGER NOT NULL, Kind INTEGER NOT NULL,
    TransferGroupId TEXT NULL, SupersededById TEXT NULL,
    RawNotificationId TEXT NULL);

CREATE INDEX IX_Tx_Account_Occurred ON Transactions(AccountId, OccurredAt);
CREATE INDEX IX_Tx_Category_Occurred ON Transactions(CategoryId, OccurredAt);
CREATE INDEX IX_Tx_MerchantKey ON Transactions(MerchantKey);

CREATE TABLE MonthBudgets (
    Id TEXT PRIMARY KEY, Year INTEGER NOT NULL, Month INTEGER NOT NULL,
    AvailableFunds INTEGER NOT NULL, PlannedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IX_Budget_Month ON MonthBudgets(Year, Month);

CREATE TABLE Envelopes (
    Id TEXT PRIMARY KEY, MonthBudgetId TEXT NOT NULL REFERENCES MonthBudgets(Id),
    CategoryId TEXT NOT NULL REFERENCES Categories(Id), PlannedAmount INTEGER NOT NULL);
CREATE UNIQUE INDEX IX_Envelope_Budget_Category ON Envelopes(MonthBudgetId, CategoryId);

CREATE TABLE Reconciliations (
    Id TEXT PRIMARY KEY, AccountId TEXT NOT NULL REFERENCES Accounts(Id),
    ObservedBalance INTEGER NOT NULL, ObservedAt TEXT NOT NULL,
    PreviousBalance INTEGER NOT NULL, PreviousAt TEXT NOT NULL,
    RecordedDelta INTEGER NOT NULL, Discrepancy INTEGER NOT NULL,
    GeneratedTransactionId TEXT NULL);

CREATE TABLE CategoryRules (
    Id TEXT PRIMARY KEY, Pattern TEXT NOT NULL,
    CategoryId TEXT NOT NULL REFERENCES Categories(Id),
    Origin INTEGER NOT NULL, HitCount INTEGER NOT NULL DEFAULT 0, LastUsedAt TEXT NULL);

CREATE TABLE RawNotifications (
    Id TEXT PRIMARY KEY, PackageName TEXT NOT NULL, Title TEXT NULL, Text TEXT NULL,
    PostedAt TEXT NOT NULL, ParseStatus INTEGER NOT NULL, TransactionId TEXT NULL);

CREATE TABLE Funds (
    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, TargetAmount INTEGER NOT NULL,
    Periodicity INTEGER NOT NULL, NextDueDate TEXT NOT NULL,
    AccruedBalance INTEGER NOT NULL DEFAULT 0, LinkedAccountId TEXT NULL);

CREATE TABLE Assets (
    Id TEXT PRIMARY KEY, Name TEXT NOT NULL, CurrentValue INTEGER NOT NULL,
    ValuedAt TEXT NOT NULL, LiquidityTier INTEGER NOT NULL,
    IsInMonthlyBudget INTEGER NOT NULL DEFAULT 0);
```

> **Powyższy DDL jest historyczny i nie odpowiada bazie.** Tabele `Funds` i `Assets`
> mają dziś inne kolumny (patrz 5.9 i 5.10), `Transactions` doszły
> `ProposedTransferGroupId` i `PaidFromFundId`, doszły też tabele `IncomePlans`
> i `Debts`. Zostawiony jako zapis pierwotnego zamysłu.
>
> **Źródłem prawdy o schemacie są migracje EF Core** w
> `Castellan.Infrastructure/Data/Migrations/` oraz konfiguracje w
> `Data/Configurations/`. Ręcznie utrzymywany DDL w dokumencie rozjeżdżał się po
> każdej migracji, więc nie jest już aktualizowany.

Pełna lista tabel: `Accounts`, `Categories`, `Transactions`, `MonthBudgets`,
`Envelopes`, `IncomePlans`, `Reconciliations`, `CategoryRules`, `RawNotifications`,
`Funds`, `Assets`, `Debts`.

---

## 8. Warstwa aplikacji

Jedna klasa na scenariusz. Nazewnictwo: `<Czasownik><Rzeczownik>UseCase`.

| Scenariusz | Etap | Wejście → Wyjście |
|---|---|---|
| `AddManualTransactionUseCase` | 1 | kwota, data, konto, kategoria → `TransactionId` |
| `DeleteTransactionUseCase` | 1 | `TransactionId` → void |
| `PlanMonthUseCase` | 1 | miesiąc, lista (kategoria, kwota) → `MonthBudgetId`, może rzucić `BudgetOverAllocatedException` |
| `GetMonthOverviewUseCase` | 1 | miesiąc → dostępne środki, pozostało do rozdzielenia, koperty z plan/fakt/pozostało |
| `ReconcileAccountUseCase` | 2 | konto, obserwowane saldo, data → `Discrepancy`, utworzona transakcja |
| `IngestRawNotificationUseCase` | 3 | surowe powiadomienie → transakcja lub `Unparsed` |
| `AssignCategoryUseCase` | 3 | `TransactionId`, kategoria, flaga „utwórz regułę" → void |
| `GetTransferProposalsUseCase` | 3 | — → pary czekające na rozstrzygnięcie |
| `ConfirmTransferUseCase` / `RejectTransferUseCase` | 3 | `GroupId` → void |
| `GetFundOverviewUseCase` | 4 | dzień wypłaty → salda, raty, opóźnienia |
| `ContributeToFundUseCase` | 4 | fundusz, kwota → void |
| `PayTransactionFromFundUseCase` | 4 | transakcja, fundusz → void (plus `UndoAsync`) |
| `GetCushionOverviewUseCase` | 5 | liczba miesięcy → poziomy płynności i autonomia |
| `ExportDataUseCase` / `ImportDataUseCase` | 6 | → plik JSON |
| `PayDebtInstallmentUseCase` | 7 | zobowiązanie, kwota, konto, kategoria → wydatek **i** niższe saldo |
| `ApplyDebtPaymentUseCase` | 7 | zobowiązanie, kwota → tylko niższe saldo |
| `SimulateDebtPayoffUseCase` | 7 | budżet miesięczny → kolejność spłaty i data wyjścia na zero |
| `GetMonthlyStatsUseCase` | — | — → sześć miesięcy wydatków i przychodów |

Nazwy w tej tabeli były pierwotnie zgadywane; powyższa lista odpowiada plikom w
`Castellan.Application/UseCases/`. `GetDashboardUseCase`, `GetInboxUseCase`,
`AccrueFundsForMonthUseCase` i `GetRunwayUseCase` nie powstały: pierwsze dwa okazały
się zbędne (ekran główny składa `GetMonthOverviewUseCase` z przeglądem długu, a
skrzynka czyta transakcje `Unsorted` prosto z repozytorium), trzeci odpadł wraz ze
zmianą modelu funduszu na jednorazowy, czwarty nazywa się `GetCushionOverviewUseCase`.

**Rozróżnienie warte uwagi:** `PayDebtInstallmentUseCase` tworzy transakcję i obniża
saldo, `ApplyDebtPaymentUseCase` tylko obniża saldo. Drugi obsługuje sytuację, w której
wydatek już istnieje — został wpisany ręcznie albo złapany z powiadomienia — a
użytkownik dopiero teraz wskazuje, którego kredytu dotyczył. Użycie tam pierwszego
zdublowałoby wydatek.

---

## 9. Infrastruktura

### 9.1 EF Core

- `CastellanDbContext`, konfiguracje przez `IEntityTypeConfiguration<T>`, oddzielna klasa na agregat.
- `Money` — konwerter wartości `Money ↔ long`.
- `YearMonth` — rozkładany na dwie kolumny (`Year`, `Month`).
- `DateTimeOffset` — konwerter do ciągu ISO-8601 (dostawca SQLite gubi offset przy standardowym mapowaniu).
- Zapytania odczytu — `AsNoTracking()`.
- Pragmy przy otwieraniu połączenia: `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`.

### 9.2 Przechwytywanie powiadomień (etap 3)

```
Platforms/Android/Services/CastellanNotificationListenerService.cs
```

- Dziedzic `Android.Service.Notification.NotificationListenerService`, zarejestrowany przez `[Service]` z `Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE"` i intent-filter `android.service.notification.NotificationListenerService`.
- Uprawnienia **nie można poprosić zwykłym dialogiem**: użytkownika trzeba skierować do `Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS` i sprawdzać `NotificationManagerCompat.getEnabledListenerPackages()` przy każdym starcie.
- **Pierwsza linia `OnNotificationPosted` to filtr po `PackageName`** — sprawdzenie białej listy banków z ustawień. Wszystko spoza listy jest odrzucane bez czytania, parsowania, logowania ani zapisu. `PackageName` jest nadawany przez system i nie może być sfałszowany; tytuł powiadomienia — może.
- Pakiet `com.google.android.apps.walletnfcrel` (Google Portfel) — zawsze `Ignored`: przy płatnościach NFC duplikuje powiadomienie bankowe, ale nie zawiera potrzebnych danych.
- Przed zapisem do `RawNotifications` tekst przechodzi przez maskę redakcyjną usuwającą sekwencje 4–8 cyfr niepodobne do kwoty (kody 3D-Secure, hasła BLIK, OTP). Powiadomienia rozpoznane jako żądanie autoryzacji (nie zrealizowana operacja) są odrzucane w całości.
- Cały blok parsowania opakowany w `try/catch` — nieobsłużony wyjątek powoduje wyłączenie serwisu przez Androida bez komunikatu. Błąd = zapis surowego powiadomienia, bez propagacji wyjątku.
- Serwis działa poza cyklem życia UI: własny scope DI i własne połączenie z BD. Nie wykonywać długiej pracy w `OnNotificationPosted` — tylko zapis i przekazanie do kolejki.
- Szczegółowe zasady bezpieczeństwa — sekcja 15.

### 9.3 Parsery banków

```csharp
public interface IBankNotificationParser
{
    string BankKey { get; }
    bool CanParse(string packageName);
    ParsedNotification? Parse(string title, string text, DateTimeOffset postedAt);
}

public sealed record ParsedNotification(
    Money Amount, string? RawMerchant, bool IsAuthorization, string? AccountHint);
```

Implementacja na wyrażeniach regularnych wyniesionych do konfiguracyjnego JSON, żeby reguły można było poprawiać bez przebudowy. Jeden parser na bank; nieznany pakiet — `Ignored`.

Format powiadomień każdego banku jest inny i zmienia się bez ostrzeżenia. Stąd wymóg przechowywania surowego tekstu (5.8) i pokrycia parserów testami na rzeczywistych przykładach.

### 9.4 Widget i szybkie wprowadzanie (etap 2)

`AppWidgetProvider` z przyciskiem otwierającym przezroczyste `Activity` szybkiego wprowadzania: klawiatura numeryczna, siatka kategorii, przycisk „Gotowe". Cel — trzy dotknięcia.

---

## 10. Interfejs

### 10.1 Ekrany

Dziewięć zakładek na pasku. Android pokazuje cztery pierwsze, reszta chowa się pod
trzema kropkami — stąd kolejność jest decyzją, nie przypadkiem.

| Zakładka | Zawartość |
|---|---|
| Główna | „pozostało do wydania" jako jedyna wielka liczba; środki i do przydzielenia jako kafelki; pasek długu; lista kopert; ostrzeżenie o kondycji przechwytywania |
| Konta | lista kont z obliczonym saldem, „ustaw domyślne", „uzgodnij" |
| Transakcje | lista miesiąca; przeciągnięcie odsłania „z funduszu" i „usuń"; pod trzema kropkami przelew, reguły, kategorie |
| Koperty | trzy kafelki miesiąca plus lista kopert z paskami; „Planuj" |
| Skrzynka | wybór trybu przechwytywania; propozycje przelewów; transakcje `Unsorted` |
| Fundusze | dzień wypłaty; suma odpisów; cel, termin, zgromadzone, rata, wskaźnik opóźnienia |
| Majątek | poduszka w miesiącach; wartość netto; aktywa wg płynności; fundusze; zobowiązania |
| Kopia | eksport i import JSON |
| Pomoc | przewodnik po wszystkich ścieżkach, rozdziały zwijane |

Ekrany poza paskiem: planowanie miesiąca, przychody, statystyki, plan spłaty, szybkie
wprowadzanie, uzgodnienie, przypisanie kategorii, reguły, kategorie oraz formularze
dodawania i edycji.

Osobny ekran ustawień nie powstał — było ich za mało, żeby uzasadnić dziesiątą zakładkę.
Dzień wypłaty stoi w Funduszach, tryb przechwytywania w Skrzynce, eksport i import
w Kopii: każde ustawienie tam, gdzie widać jego skutek.

### 10.2 Zasady wyświetlania

- Wydatek zawsze z minusem i w jednym kolorze; żadnych „czerwone — złe" przy zwykłych wydatkach.
- Przekroczenie koperty — jedyne miejsce, gdzie dopuszczalny jest kolor alarmowy.
- „Nierozpoznane" pokazywane na równi z innymi kategoriami, bez wyróżnienia i bez sformułowań winy.
- Żadnego ekranu wymagającego przypominania sobie przeszłości.

### 10.3 Język i lokalizacja

**Głównym językiem interfejsu jest polski (pl-PL).** Aplikacja jest osobista, polski to język codziennego użytkowania.

Implementacja:

- `CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("pl-PL")` przy starcie.
- Ciągi interfejsu w `Resources/Strings/AppResources.resx`; dostęp przez statyczną owijkę `AppResources` i `{x:Static}` w XAML.
- `<NeutralLanguage>pl-PL</NeutralLanguage>` w `Castellan.App.csproj`.

Dodanie języka w przyszłości: utworzyć `AppResources.{kod}.resx` — bez zmian w kodzie.

---

## 11. Algorytmy

### 11.1 Deduplikacja (etap 3)

Kandydat na duplikat dla nowej transakcji T:

- ten sam `AccountId`;
- `|T.OccurredAt − C.OccurredAt| ≤ 1 dzień`;
- `T.MerchantKey == C.MerchantKey`;
- `|T.Amount| == |C.Amount|` lub rozbieżność `≤ 2%` (przeliczenie waluty, napiwki).

Jeśli znaleziono kandydata z `Kind == Authorization`, a nowa transakcja jest `Regular` — ustawić kandydatowi `SupersededById = T.Id`. Odwrotna kolejność (obciążenie przyszło przed autoryzacją) — nowa oznaczana jako scalona.

Próg 2% i okno 1 dzień wynieść do ustawień: dobrać empirycznie na podstawie własnych banków.

### 11.2 Scalanie przelewów (etap 3)

Dwie transakcje A i B tworzą przelew, jeśli:

- `A.AccountId != B.AccountId`, oba konta własne;
- `A.Amount == −B.Amount`;
- `|A.OccurredAt − B.OccurredAt| ≤ 48 godzin`;
- żadna nie należy jeszcze do `TransferGroup`.

Obu przypisywany jest wspólny `TransferGroupId`, `Kind = Transfer`, `CategoryId = Transfer`.

Fałszywe trafienie możliwe przy zbieżności kwot — dlatego proponować potwierdzenie, a nie scalać w ciszy.

### 11.3 Normalizacja nazwy sprzedawcy

1. Wielkie litery, zastąpienie znaków niealfanumerycznych spacją, scalenie spacji.
2. Odcięcie znanych prefiksów agregatorów: `PAYU`, `PAYPAL`, `GOOGLE`, `APPLE PAY`, `TPAY`, `PRZELEWY24`, `BLIK`.
3. Usunięcie numerów punktów: końcowe tokeny w stylu `Z1234`, `NR 12`, `#0345`.
4. Obcięcie do 40 znaków.

Płatności online przez agregatora często pozostawiają tylko nazwę agregatora. To ograniczenie metody: takie transakcje zostają w skrzynce i wymagają ręcznej decyzji. Oczekiwany udział — ocenić na rzeczywistych danych, to jedna ze sprawdzanych hipotez projektu.

### 11.4 Autokategoryzacja (etap 3)

Przy przechwyceniu: znaleźć wszystkie `CategoryRule`, których `Pattern` zawiera się w `MerchantKey`; wybrać z najdłuższym `Pattern`; przy równości — z większym `HitCount`; zinkrementować `HitCount`.

Przy ręcznym przypisaniu kategorii transakcji z niepustym `MerchantKey` i włączonym przełączniku „zapamiętaj" — utworzyć regułę `Origin = Learned` z `Pattern = MerchantKey`.

### 11.5 Uzgodnienie (etap 2)

```
RecordedDelta = Σ Transaction.Amount, gdzie AccountId = A
                i PreviousAt < OccurredAt ≤ ObservedAt
                i SupersededById IS NULL

Discrepancy = (ObservedBalance − PreviousBalance) − RecordedDelta
```

- `Discrepancy < 0` — niezapisane wydatki. Utworzyć transakcję `Kind = Unidentified`, `CategoryId = Unidentified`, `Amount = Discrepancy`.
- `Discrepancy > 0` — niezapisany przychód **lub** zduplikowane wydatki. Zgodnie z N-5 nie tworzyć automatycznie: pokazać listę transakcji okresu i zapytać, co zrobić.
- `Discrepancy == 0` — zaktualizować `LastReconciledBalance` i `LastReconciledAt`.

### 11.6 Kondycja przechwytywania (etap 3)

Przechowywać `LastNotificationAt` w ustawieniach. Jeśli starsze niż 1 dzień — baner na ekranie głównym ze sprawdzeniem `NotificationManagerCompat.getEnabledListenerPackages()`.

Android może po cichu zabić serwis w tle dla oszczędzania baterii, bez błędu. To najbardziej prawdopodobna awaria systemu w eksploatacji. Drugi poziom ochrony — uzgodnienie: wzrost „Nierozpoznanego" wykryje awarię niezależnie od banera.

**Tryb przechwytywania.** Ustawienie `capture_mode` (`notifications` domyślnie albo
`manual`) rozstrzyga, czy powyższe w ogóle obowiązuje. W trybie ręcznym:

- nasłuch odrzuca powiadomienie na wejściu, przed białą listą pakietów — nawet gdy
  uprawnienie zostało kiedyś przyznane. Bez tego użytkownik wpisujący wydatki sam
  dostawałby każdy z nich po raz drugi z banku;
- baner kondycji na Głównej i pasek braku uprawnienia w Skrzynce są ukryte. Cisza jest
  tu stanem zamierzonym, a baner stojący zawsze uczy ignorować banery;
- reszta aplikacji działa bez zmian — koperty, fundusze, zobowiązania i uzgadnianie
  salda nie zależą od powiadomień.

Ustawienie leży w `Preferences`, nie w bazie: dotyczy tego telefonu, nie finansów, więc
nie ma czego szukać w kopii zapasowej. Odcięcie następuje w
`CastellanNotificationListenerService`, a nie w use case — warstwa aplikacji nie ma
prawa wiedzieć o ustawieniach urządzenia.

Powód powstania jest praktyczny: aplikacji zaczęła używać osoba, która nie ma
powiadomień bankowych i nie zamierza ich włączać. Dla niej ostrzeżenie o braku
uprawnienia było nie diagnostyką, lecz nagabywaniem o rzecz świadomie odrzuconą.

### 11.7 Naliczanie do funduszy (etap 4)

Liczone na **wypłaty**, nie na miesiące kalendarzowe — użytkownik dzieli pieniądze
w dniu wypłaty, więc liczba wypłat do terminu jest tym, co realnie zostało.

```
OkresówZostało   = liczba dni wypłaty w przedziale (dziś … Deadline]
                   pomniejszona o bieżący, jeśli LastContributionMonth == ten miesiąc
Rata             = (TargetAmount − Balance) / OkresówZostało

OczekiwaneDotąd  = TargetAmount × (minione wypłaty od StartMonth / wszystkie wypłaty)
Opóźnienie       = max(0, OczekiwaneDotąd − Balance)
```

Gdy dzień wypłaty nie jest ustawiony, obie liczby liczą się kalendarzowo — działa,
ale mniej dokładnie, stąd pasek zachęcający do podania dnia wypłaty.

Pomniejszenie o bieżący okres po wpłacie jest istotne: bez niego rata przeliczała się
na nowo zaraz po wpłaceniu, pokazując, że wciąż trzeba dołożyć.

Suma rat wszystkich funduszy nie jest doklejana do planu automatycznie — ekran
planowania podpowiada ją przyciskiem, który jednym tapnięciem wpisuje kwotę do koperty
„Rezerwy". Kopertę wybiera użytkownik, więc suma normalnie uczestniczy w N-1.

### 11.8 Poduszka finansowa (etap 5)

```
ŚredniWydatek   = średnia z faktycznych wydatków miesięcznych z ostatnich 3 miesięcy
                  (liczone tylko miesiące z jakimkolwiek wydatkiem)

Płynne(poziom)  = Σ Asset.Value gdzie Liquidity == poziom
                  + salda kont Checking, gdy poziom == Immediate

Autonomia(poz.) = Płynne(narastająco do poziomu) / ŚredniWydatek   [miesiące]
```

**Odejście od pierwotnego założenia.** Spec zakładał „wydatek nieredukowalny" liczony
z kopert oznaczonych flagą „obowiązkowa" plus rat funduszy. Flaga nigdy nie powstała —
wymagałaby od użytkownika rozstrzygnięcia przy każdej kategorii, co jest obowiązkowe,
a odpowiedź zmienia się z miesiąca na miesiąc. Wdrożony wariant bierze średnią
z realnych wydatków ostatnich trzech miesięcy: mniej precyzyjny teoretycznie, ale
oparty na tym, jak faktycznie wygląda życie, i niewymagający żadnej konfiguracji.

Poziomów są cztery, nie trzy, a liczby podawane są narastająco — „ile wytrzymam
z tego, co mam pod ręką" kontra „ile wytrzymam, jeśli sięgnę też po rzeczy trudniejsze
do spieniężenia". Fundusze są liczone osobno, poza poziomami: te pieniądze mają już
przypisany przyszły wydatek, więc nie są rezerwą na czarną godzinę.

---

## 12. Etapy szczegółowo

### Etap 0 — szkielet

**Prace:** struktura rozwiązania według 4.1; `CastellanDbContext` i pierwsza migracja; rejestracja DI; MAUI Shell z zaślepkami nawigacji; projekty xUnit; GitHub Actions — build, testy, artefakt APK; `.gitignore`, `.editorconfig`, licencja; `android:allowBackup="false"` i `android:dataExtractionRules` z jawnym zakazem backupu w chmurze; `network_security_config.xml` blokujący wszystkie połączenia sieciowe; `android:usesCleartextTraffic="false"` w manifeście; `FLAG_SECURE` na oknie głównej aktywności.

**Gotowe, gdy:** APK instaluje się na telefonie i uruchamia; `dotnet test` jest zielony w CI; migracja tworzy pustą BD na urządzeniu.

### Etap 1 — ręczne prowadzenie

**Prace:** agregaty `Account`, `Category`, `Transaction`, `MonthBudget` z niezmienniki N-1, N-2, N-8; scenariusze `AddManualTransaction`, `DeleteTransaction`, `PlanMonth`, `GetMonthOverview`; ekrany „Główny", „Koperty", „Planowanie", „Transakcje", „Konta"; seed kategorii systemowych.

**Gotowe, gdy:** można założyć konta, zaplanować miesiąc (powyżej dostępnych środków nie przepuszcza), zapisać wydatki ręcznie i zobaczyć plan/fakt/pozostało dla kopert.

**Nie robić:** raporty, wykresy, eksport.

### Etap 2 — uzgodnienie i szybkie wprowadzanie

**Prace:** `Reconciliation`, niezmienniki N-5 i N-6; obliczane saldo konta; scenariusze `ReconcileAccount`, `GetDashboard`; ekran uzgodnienia; ekran szybkiego wprowadzania; widget na ekran główny.

**Gotowe, gdy:** wprowadzenie faktycznego salda tworzy „Nierozpoznane"; pominięcie kilku dni nie psuje obrazu; wydatek zapisuje się w trzech dotknięciach.

### Etap 3 — przechwytywanie powiadomień

Kluczowy etap. Dla niego to wszystko zostało zapoczątkowane.

**Prace:** `NotificationListenerService` i obsługa uprawnień; `RawNotification`; parsery dwóch banków na rzeczywistych przykładach; `IngestNotificationUseCase`; deduplikacja (11.1); scalanie przelewów (11.2); normalizacja nazwy sprzedawcy (11.3); `CategoryRule` i autokategoryzacja (11.4); ekrany „Skrzynka odbiorcza" i „Reguły"; kondycja przechwytywania (11.6).

**Kolejność wewnątrz etapu:** najpierw zbieranie surowych powiadomień i ekran ich podglądu (bez parsowania) — niech się zbierają rzeczywiste dane przez tydzień. Parsery pisać na zebranych danych, a nie na wyobrażonym formacie.

**Gotowe, gdy:** przez tydzień eksploatacji udział transakcji wymagających ręcznego wprowadzenia jest poniżej 20%, a skrzynka rozgrywana jest w mniej niż minutę dziennie.

### Etap 4 — fundusze nieregularnych płatności

**Prace:** agregat `Fund`, niezmiennik N-7; obliczenia 11.7; włączenie `PotrzebaWMiesiącu` do planu miesiąca; ekran „Fundusze" ze wskaźnikiem opóźnienia.

**Gotowe, gdy:** ubezpieczenie, podatek i urlop są założone, a aplikacja odpowiada na pytanie „czy do grudnia wystarczy, czy jestem w tyle".

### Etap 5 — aktywa i poduszka finansowa

**Prace:** agregat `Asset`; poziomy płynności na kontach i aktywach; flaga obowiązkowości na kategoriach; obliczenie 11.8; ekran „Poduszka finansowa".

**Gotowe, gdy:** widać, na ile miesięcy wystarczy według trzech poziomów płynności, i jak liczba zmienia się po wyłączeniu źródła dochodu.

### Etap 6 — eksploatacja i portfolio

**Prace:** eksport i import JSON; przypomnienie o backupie raz w miesiącu; README (stos, architektura, model domenowy, uzasadnienie decyzji, testy, ograniczenia); diagramy; zrzuty ekranu; podpisany APK wydania w GitHub Releases.

**Gotowe, gdy:** aplikację można przeinstalować na nowym telefonie bez utraty danych, a repozytorium jest czytelne dla postronnej osoby.

**Zrealizowane częściowo.** Eksport i import działają, README jest napisane. Nie powstały:
przypomnienie o backupie raz w miesiącu, diagramy ani wydania w GitHub Releases — APK
budowany jest lokalnie i przenoszony ręcznie.

### Etap 7 — zobowiązania i plan spłaty

**Prace:** agregat `Debt` jako lustro funduszu; ekrany dodawania, edycji i płacenia raty;
powiązanie istniejącego wydatku z kredytem przez kategorię „Kredyty i pożyczki";
symulacja spłaty metodą kuli śnieżnej; wartość netto i przemianowanie zakładki
„Aktywa" na „Majątek".

**Gotowe, gdy:** spojrzenie na sumę długu i datę wyjścia na zero nie wymaga wejścia
w osobną zakładkę — stąd pasek na Głównej, a nie dziewiąta pozycja pod trzema kropkami.

### Etap 8 — wygląd i przewodnik

**Prace:** jedna paleta i jedna skala tekstu na wszystkich ekranach zamiast kolorów
dobieranych osobno na każdym; ikony zakładek; ciemne okna systemowe Androida; przewodnik
po aplikacji jako zakładka „Pomoc"; wybór trybu przechwytywania.

**Gotowe, gdy:** aplikację da się podać komuś obcemu bez tłumaczenia jej na głos.

---

## 13. Testowanie

| Poziom | Co pokrywane | Narzędzie |
|---|---|---|
| Domain | niezmienniki N-1…N-8, arytmetyka `Money`, granice `YearMonth`, naliczanie funduszy, autonomia | xUnit, czyste testy bez BD |
| Application | scenariusze na zaślepkach repozytoriów | xUnit |
| Infrastructure | konfiguracje EF, konwertery, migracje | xUnit + SQLite na pliku tymczasowym |
| Parsery | rzeczywiste teksty powiadomień obu banków: zakup, online, przelew, autoryzacja, obciążenie | xUnit, testy tabelaryczne |
| Algorytmy | deduplikacja i scalanie przelewów na skonstruowanych zestawach, w tym fałszywe trafienia | xUnit |

Testcontainers nie jest potrzebny: brak zewnętrznych zależności. Baza in-memory
odpadła — nie odtwarza zachowania migracji ani konwerterów typów, a to właśnie one
są tu przedmiotem testu. Każdy test zakłada plik w katalogu tymczasowym, wykonuje na
nim `Database.Migrate()` i kasuje go w `finally` po `SqliteConnection.ClearAllPools()`.

Obowiązkowe testy negatywne: planowanie powyżej środków; uzgodnienie z dodatnią rozbieżnością; podwójne powiadomienie; przelew między własnymi kontami z równymi kwotami; powiadomienie nieznanego formatu.

---

## 14. Wymagania niefunkcjonalne

- Zimny start do interaktywnego ekranu głównego — poniżej 2 sekund na przeciętnym urządzeniu.
- Przetwarzanie powiadomienia — poniżej 100 ms w `OnNotificationPosted`; wszystko ciężkie asynchronicznie.
- Rozmiar BD przez 5 lat — kilka megabajtów; optymalizacja nie jest wymagana.
- Zużycie baterii przez serwis w tle — na poziomie szumu: serwis jest zdarzeniowy, bez odpytywania.
- Wszystkie dane tylko na urządzeniu. Żadnych wywołań sieciowych — w projekcie nie ma klienta HTTP.
- Aplikacja działa w pełni offline, w tym przy pierwszym uruchomieniu.

---

## 15. Bezpieczeństwo

### 15.1 Przechwytywanie powiadomień

`BIND_NOTIFICATION_LISTENER_SERVICE` daje dostęp do **wszystkich** powiadomień na telefonie — wiadomości, poczty, kodów dwuskładnikowych. Android nie umie zawęzić tego dostępu wyborowo.

Trzy obowiązkowe reguły:

- **Filtr po `PackageName` jako pierwsza operacja w `OnNotificationPosted`.** Wszystko spoza białej listy banków jest odrzucane bez czytania, parsowania, logowania ani zapisu. `PackageName` nadaje system i nie może być sfałszowany; tytuł powiadomienia — może.
- **Redakcja tekstu przed zapisem.** Powiadomienia bankowe zawierają kody 3D-Secure, hasła BLIK i jednorazowe OTP. Przed zapisem do `RawNotifications` tekst przechodzi przez maskę usuwającą sekwencje 4–8 cyfr niepodobne do kwoty. Powiadomienia rozpoznane jako żądanie autoryzacji (nie zrealizowana operacja) są odrzucane w całości.
- **Sformułowanie „przechowywane zawsze" w sekcji 5.8** odnosi się wyłącznie do powiadomień z białej listy po redakcji. Powiadomienia spoza listy nie są nigdy zapisywane ani logowane.

**Stan wdrożenia.** Filtr po `PackageName` jest pierwszą operacją — z jednym wyjątkiem
przed nim: sprawdzeniem trybu przechwytywania (11.6), które również niczego nie czyta.
Maska działa i usuwa ciągi 4–8 cyfr niebędące kwotą.

Dwa punkty rozminęły się z wdrożeniem:

- **Powiadomienia Portfela Google nie są odrzucane, tylko parsowane.** Przy płatności
  telefonem zbliżeniowo bywają jedynym śladem transakcji — ING nie wysyła wtedy
  własnego powiadomienia. Odrzucanie ich gubiło te płatności całkowicie. Konsekwencją
  jest druga ścieżka deduplikacji (11.1): Portfel podaje nazwę prawną spółki
  („JMP S.A. BIEDRONKA 591"), a bank markę („Biedronka"), więc dopasowanie po nazwie
  sprzedawcy dla tej pary zawodzi.
- **Powiadomienia autoryzacyjne nie są odrzucane w całości.** Żaden parser nie tworzy
  dziś transakcji `Kind = Authorization`; obsługa zastępowania blokady obciążeniem
  istnieje w kodzie deduplikacji, ale pozostaje uśpiona, bo banki użytkownika przysyłają
  wyłącznie powiadomienie o obciążeniu (patrz 19.2).

### 15.2 Izolacja sieciowa

Aplikacja z dostępem do wszystkich powiadomień i z dostępem do sieci to gotowe narzędzie szpiegowskie. Brak sieci w Castellan musi być wymuszony strukturalnie, nie tylko „niezaimplementowany":

- Zero klientów HTTP w zależemnościach.
- `android:usesCleartextTraffic="false"` w manifeście.
- `network_security_config.xml` bez żadnych `<domain-config>` i bez wyjątków — blokuje wyjście nawet bibliotece z telemetrią dodanej przez nieuwagę.

Konfiguracja sieciowa jest częścią **Etapu 0**.

### 15.3 Dane na urządzeniu

**`android:allowBackup="false"` — ustawić na Etapie 0.** Domyślnie Android automatycznie backupuje dane aplikacji do Google Drive użytkownika. Bez tego flagi baza z transakcjami, saldami i nazwami sprzedawców trafia do chmury bez wiedzy użytkownika. Uzupełnić `android:dataExtractionRules` z jawnym zakazem cloud-backup i device-transfer.

**Szyfrowanie bazy — opcjonalne.** Android szyfruje storage aplikacji przez file-based encryption (FBE) pod warunkiem, że telefon ma włączoną blokadę ekranu — pokrywa scenariusz „zgubiony telefon". SQLCipher dodaje ochronę przed wydobyciem z urządzenia z rootem lub narzędziami forensics — scenariusze mało prawdopodobne w tym kontekście. Jeśli SQLCipher: `SQLitePCLRaw.bundle_e_sqlcipher`, klucz generowany raz i przechowywany w Android Keystore (nie w kodzie, nie w SharedPreferences). Uwaga: klucz z Keystore jest nieprzenośny — backup musi być szyfrowany osobno hasłem. Rozważyć na **Etapie 6**, nie wcześniej.

### 15.4 Eksport i backup

Plik eksportu to niezaszyfrowana kopia bazy leżąca poza piaskownicą aplikacji:

- Zapis wyłącznie przez **Storage Access Framework** — użytkownik sam wybiera miejsce; aplikacja nie pisze do wspólnych folderów.
- Szyfrowanie eksportu hasłem: AES-GCM, klucz wyprowadzany z PBKDF2 lub Argon2.
- Wyraźne ostrzeżenie na ekranie eksportu: „Ten plik zawiera wszystkie Twoje dane finansowe".

**Stan wdrożenia: zrealizowany jeden punkt z trzech.** Eksport idzie przez systemowy
arkusz udostępniania, więc miejsce wybiera użytkownik. Plik jest jednak **zwykłym,
nieszyfrowanym JSON-em**, a ekran eksportu opisuje, co plik zawiera, ale nie ostrzega
przed konsekwencjami jego wycieku. Do nadrobienia — to najpoważniejsza otwarta luka
w tej sekcji, bo plik z założenia opuszcza urządzenie.

### 15.5 Ekran i dostęp fizyczny

- **`FLAG_SECURE`** na oknie głównej aktywności — blokuje zrzuty ekranu i ukrywa zawartość w liście ostatnich aplikacji. Standardowa praktyka dla aplikacji finansowych; ustawić na Etapie 0. **Nieustawione.** Świadomy kompromis na czas budowy: zrzuty ekranu są głównym sposobem zgłaszania uwag do wyglądu. Do włączenia, gdy aplikacja przestanie być codziennie przebudowywana.
- **Biometria przy otwarciu aplikacji** — opcjonalna, z zastrzeżeniem: widget szybkiego wprowadzania nie może wymagać odcisku palca, bo cel „trzy dotknięcia" przestaje działać. Kompromis: biometria chroni podgląd (pełna aplikacja), widget pozwala zapisać transakcję bez wyświetlania salda.

### 15.6 Logowanie

- Do logów trafiają wyłącznie identyfikatory i kody zdarzeń — nigdy wartości: `Transaction {Id} categorized by rule {RuleId}`, a nie `Biedronka 87,40 → Jedzenie`.
- Tekst powiadomienia nie może trafić do logu przy żadnym błędzie parsowania. Niezapisane powiadomienie ląduje w tabeli `RawNotifications` (zarządzanej jak baza), nie w pliku logów. Plik logów nie jest chroniony piaskownicą — przy dołączaniu do raportu o błędzie jedzie razem z nim.

### 15.7 Zależności

Każdy pakiet NuGet obok danych o wszystkich powiadomieniach to cudzy kod przy bardzo wrażliwych danych:

- Lista zależności minimalna (w specyfikacji już krótka).
- Wersje przypięte; Dependabot włączony.
- Zero pakietów analityki, crash reportingu i reklam — żadnego, nawet „darmowego i niegroźnego".

### 15.8 Aspekty prawne

GDPR nie ma zastosowania: przetwarzanie danych osobowych i rodzinnych na własnym urządzeniu podlega wyjątkowi domowemu (art. 2 ust. 2 lit. c). Żadnych polityk prywatności ani formularzy zgody pisać nie trzeba. Zmienia się to natychmiast, gdy aplikacja trafi do Google Play lub zacznie z niej korzystać ktoś inny — publikacja nie jest jednak planowana (sekcja 16.3).

---

## 16. Ryzyka

| Ryzyko | Prawdopodobieństwo | Co robić |
|---|---|---|
| Powiadomienia banku nie zawierają nazwy sprzedawcy, tylko kwotę | średnie | sprawdzić **przed** etapem 3 na rzeczywistych przykładach; jeśli tak — część zamysłu odpada, zostanie „kwota + wybór kategorii", co i tak jest lepsze niż Excel |
| Android zabija serwis w tle | wysokie | kondycja przechwytywania (11.6) + uzgodnienie jako drugi poziom; wyłączenie optymalizacji baterii dla aplikacji |
| Bank zmienia format powiadomień | średnie | surowe powiadomienia przechowywane zawsze; reguły w konfigu; testy na przykładach |
| Płatności online dają tylko nazwę agregatora | wysokie | przyjąć jako ograniczenie; takie transakcje zostają w skrzynce |
| Etapy 1–2 przeciągają się, zainteresowanie opada | **wysokie** | ściśle ograniczyć zakres etapów 1–2; nic nie dodawać ponad listę |
| Projekt powtarza losy Bastion: napisany, ale nieużywany | średnie | kryterium sukcesu to eksploatacja, a nie obecność kodu (sekcja 17) |

### 16.3 O publikacji

Google Play surowo ogranicza aplikacje żądające dostępu do powiadomień i wymaga uzasadnienia przeznaczenia. Aplikacja jest osobista, publikacja nie jest planowana; dystrybucja — instalacja podpisanego APK na własnym urządzeniu. Jeśli kiedyś publikacja będzie potrzebna, to osobna praca z polityką dostępu i deklaracją.

---

## 17. Kryterium sukcesu

Nie „napisane", lecz dwa miesiące po etapie 3:

- udział „Nierozpoznanego" w łącznych wydatkach — poniżej 10%;
- ręczne wprowadzanie — poniżej 20% transakcji;
- rozgrywanie skrzynki — poniżej minuty dziennie;
- aplikacja otwierana codziennie bez przypomnień;
- budżet na miesiąc planowany przed jego początkiem.

Jeśli „Nierozpoznane" trwale powyżej 25% — mechanizm przechwytywania nie działa; naprawiać go, a nie dodawać funkcji.

---

## 18. Metoda pracy

Wniosek z retrospektywy po Bastion: aplikacja powstała, umiejętność — nie.

Weryfikacja na każdym etapie: czy da się wyjaśnić głośno, dlaczego zrobiono właśnie tak i jakie alternatywy odrzucono. Jeśli nie — etap nie jest zamknięty.

---

## 19. Otwarte pytania

1. ~~Nazwy pakietów aplikacji obu banków i rzeczywiste przykłady powiadomień.~~
   **Rozwiązane:** `pl.ing.mojeing`, `com.revolut.revolut`,
   `com.google.android.apps.walletnfcrel`.
2. ~~Czy chociaż jeden bank przysyła osobne powiadomienia o autoryzacji i obciążeniu, czy tylko jedno.~~
   **Rozwiązane:** bank przysyła tylko powiadomienie o obciążeniu, więc obsługa autoryzacji
   pozostaje uśpiona. Przy płatności telefonem (NFC) przychodzą dwa powiadomienia: od banku
   i od Portfela Google. Pierwotna odpowiedź — ignorować Portfel po `PackageName` — okazała
   się błędna: ING przy zbliżeniówce z telefonu nie wysyła własnego powiadomienia, więc
   Portfel bywa jedynym śladem. Oba są dziś parsowane, a duplikaty rozstrzyga deduplikacja.
3. ~~Lista kategorii: przenieść z polskiego szablonu czy ułożyć od nowa.~~
   **Rozwiązane:** zestaw domyślny zakładany przy pierwszym uruchomieniu, korygowany
   w eksploatacji. „Jedzenie" ustąpiło „Produktom do domu", bo jeden paragon ze sklepu to
   zwykle jedzenie plus chemia plus higiena naraz. Kategorie dokładane w kolejnych wersjach
   trafiają też do istniejących baz, z pominięciem tych zarchiwizowanych przez użytkownika.
4. ~~Flaga „obowiązkowa" na kategoriach — etap 1 czy etap 5.~~
   **Rozwiązane: nie wprowadzać.** Wymagałaby rozstrzygnięcia przy każdej kategorii, co jest
   obowiązkowe, a odpowiedź zmienia się z miesiąca na miesiąc. Poduszka liczy się ze średniej
   z faktycznych wydatków (11.8).
5. Czy potrzebna jest historia planów miesięcznych, czy wystarczy bieżący i poprzedni.
   **Otwarte.** Plany są trzymane bez ograniczenia, a przeglądarka miesięcy sięga dowolnie
   wstecz; nie wiadomo, czy ktokolwiek tam zagląda.
6. Czy tryb ręcznego wprowadzania (11.6) wystarcza osobie bez powiadomień bankowych, czy
   potrzebne są dodatkowe ułatwienia — np. wprowadzanie wielu transakcji jednym ciągiem.
   **Otwarte, do sprawdzenia w eksploatacji.**
