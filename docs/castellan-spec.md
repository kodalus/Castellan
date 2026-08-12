# Castellan — техническая спецификация

Приложение домашнего бюджета. Android, офлайн, один пользователь, без серверной части.

Документ рабочий, на русском. Публичный README пишется отдельно.

---

## 1. Обзор

### 1.1 Задача

Перенести на телефон метод конвертного бюджета, распределяемого от располагаемых средств (проверен на практике в экселе, работал), и устранить единственную причину, по которой метод был заброшен: **трение при вводе операций**.

Формулировка проблемы дословно: вечером не вспоминается, на что ушли деньги. Значит, задача не «напомнить записать», а «не требовать памяти».

### 1.2 Ключевой принцип

> Приложение не должно полагаться на память пользователя.

Все траты проходят картой, BLIK-ом или онлайн, наличных нет. Каждая порождает уведомление банка, содержащее сумму и мерчанта. Мерчант помнит за пользователя. Из этого следует архитектура: **основной источник данных — перехват уведомлений, ручной ввод аварийный**.

### 1.3 Второй принцип

> Пропуск данных не должен ломать систему.

Единственный источник истины по остатку — сверка с фактическим балансом счёта, а не сумма записанных операций. Всё незаписанное автоматически становится категорией «Неопознанное». Забывчивость увеличивает одну цифру, а не обесценивает месяц.

### 1.4 Границы

Не входит никогда: серверная часть, аккаунты, облачная синхронизация, второй пользователь, мультивалютность, интеграция с банковскими API, публикация в Google Play (см. 15.3).

---

## 2. Этапы

| Этап | Содержание | Результат |
|---|---|---|
| 0 | Каркас решения, БД, тесты, CI | Пустое приложение запускается на телефоне |
| 1 | Счета, категории, операции, бюджет месяца | Работающий ручной учёт |
| 2 | Сверка, «Неопознанное», быстрый ввод, виджет | Учёт, переживающий пропуски |
| 3 | Перехват уведомлений, инбокс, дедупликация, автокатегоризация | Учёт без ручного ввода — целевое состояние |
| 4 | Фонды нерегулярных платежей | Страховка, налоги, отпуск |
| 5 | Активы, ликвидность, запас прочности | Ответ на вопрос «на сколько хватит» |
| 6 | Бэкап, экспорт, публичный README | Проект пригоден для портфолио |

**Важное предупреждение по этапам 1–2.** Они дают ручной ввод — тот самый инструмент, который уже был заброшен. Их надо пройти быстро и не пытаться на них «пожить»: реальная ежедневная эксплуатация начинается с этапа 3. Если между этапом 2 и этапом 3 образуется пауза в месяц, есть риск разочароваться в проекте раньше, чем он заработает по назначению.

---

## 3. Технические решения

### 3.1 Платформа

**.NET MAUI, .NET 10 (LTS), target Android 15 (API 35), minimum Android 10 (API 29).**

Обоснование:

- C# + XAML + MVVM — родная территория с 2013 года.
- Прямой доступ к Android API через биндинги .NET for Android — обязательное условие для `NotificationListenerService` (этап 3).
- Единственная целевая платформа — Android. Кроссплатформенность не нужна, но и не мешает.

Отклонено:

- **Avalonia** — сопоставима по UI, но платформенные сервисы Android требуют больше ручной обвязки; выигрыша нет.
- **Angular + Capacitor** (стек Bastion) — доступ к уведомлениям через плагин, то есть чужой код в самом важном месте проекта. Противоречит цели.
- **Kotlin / нативный Android** — лучший доступ к платформе, но обучение языку и экосистеме съест проект.

### 3.2 Хранилище

**EF Core 10 + SQLite**, файл БД в `FileSystem.AppDataDirectory`.

- Миграции EF Core применяются при старте (`Database.Migrate()`).
- Отклонён `sqlite-net-pcl`: легче на старте, но нет миграций и нет переноса опыта на рабочий стек.

### 3.3 Деньги

**Хранить целыми числами в грошах (`long`).** SQLite не имеет типа `decimal`, EF Core преобразует его в `TEXT` или `REAL`; второе даёт ошибки округления в суммах.

Тип `Money` — value object над `long Grosze`. Валюта одна (PLN), в модель не выносится.

### 3.4 Идентификаторы

`Guid` версии 7 (`Guid.CreateVersion7()`), монотонно возрастающие — не фрагментируют индексы SQLite, в отличие от v4.

### 3.5 Время

`DateTimeOffset` везде, хранение в ISO-8601 (`TEXT`). Локальная зона — Europe/Warsaw. Границы месяца считаются в локальной зоне, не в UTC (иначе операция 1 числа в 00:30 попадёт в прошлый месяц).

### 3.6 Библиотеки

| Назначение | Выбор |
|---|---|
| MVVM | `CommunityToolkit.Mvvm` (source generators) |
| DI | встроенный `Microsoft.Extensions.DependencyInjection` |
| Логирование | `Microsoft.Extensions.Logging` + файловый провайдер, ротация 7 дней |
| Тесты | `xUnit`, `FluentAssertions` |
| Сериализация | `System.Text.Json` |

Медиатор (MediatR и аналоги) не используется: сценариев мало, лишний слой скрывает поток управления, а его нужно уметь объяснить.

---

## 4. Архитектура

### 4.1 Проекты

```
Castellan.sln
├── src/
│   ├── Castellan.Domain/           без внешних зависимостей
│   ├── Castellan.Application/      → Domain
│   ├── Castellan.Infrastructure/   → Domain, Application (EF Core, парсеры)
│   └── Castellan.App/              → всё (MAUI, XAML, ViewModels, Android-сервисы)
└── tests/
    ├── Castellan.Domain.Tests/
    ├── Castellan.Application.Tests/
    └── Castellan.Infrastructure.Tests/
```

Правило зависимостей: строго внутрь. `Castellan.Domain` не ссылается ни на что, включая EF Core.

### 4.2 Слои

- **Domain** — агрегаты, value objects, инварианты, доменные сервисы (чистые вычисления). Здесь живёт всё, что интересно защищать на code review.
- **Application** — сценарии (use cases), интерфейсы репозиториев, DTO. Один класс на сценарий, метод `ExecuteAsync`.
- **Infrastructure** — `DbContext`, конфигурации, реализации репозиториев, парсеры уведомлений, файловый бэкап.
- **App** — MAUI: страницы, ViewModel, конвертеры, платформенный код Android (`Platforms/Android/`).

### 4.3 Персистентность агрегатов

Репозиторий на агрегат, не на таблицу:

```csharp
public interface IAccountRepository
{
    Task<Account?> GetAsync(AccountId id, CancellationToken ct);
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct);
    Task AddAsync(Account account, CancellationToken ct);
}
```

`SaveChangesAsync` вызывается сценарием, не репозиторием (`IUnitOfWork`).

---

## 5. Доменная модель

### 5.1 Value objects

| Тип | Содержание | Правила |
|---|---|---|
| `Money` | `long Grosze` | арифметика, сравнение, `Abs`, `IsNegative`; форматирование `#,##0.00 zł` |
| `YearMonth` | `int Year, int Month` | `Contains(DateTimeOffset)`, `Next()`, `Previous()`, границы в локальной зоне |
| `MerchantKey` | `string` | нормализованное имя мерчанта (см. 11.3) |
| `Percentage` | `decimal` | 0..1 |

Знак суммы: **расход отрицательный, доход положительный**. Единое правило по всей системе, без исключений в UI-слое.

### 5.2 Account (агрегат)

| Поле | Тип | Примечание |
|---|---|---|
| `Id` | `AccountId` | |
| `Name` | `string` | |
| `BankKey` | `string` | ключ набора правил парсинга, этап 3 |
| `Kind` | `AccountKind` | `Checking`, `Savings` |
| `LiquidityTier` | `LiquidityTier` | `Immediate`, `Month`, `Locked` — этап 5, по умолчанию `Immediate` |
| `LastReconciledBalance` | `Money` | |
| `LastReconciledAt` | `DateTimeOffset` | |
| `IsArchived` | `bool` | счета не удаляются |

Текущий остаток не хранится. Он вычисляется:

```
CurrentBalance = LastReconciledBalance + Σ Transaction.Amount, где OccurredAt > LastReconciledAt
```

### 5.3 Category (агрегат)

| Поле | Тип |
|---|---|
| `Id` | `CategoryId` |
| `Name` | `string` |
| `Kind` | `Expense` \| `Income` |
| `IsSystem` | `bool` |
| `IsArchived` | `bool` |

Системные категории, создаются миграцией, не удаляются и не переименовываются:

- **`Unsorted`** — «Не разобрано»: операция захвачена, категория не назначена.
- **`Unidentified`** — «Неопознанное»: расхождение, выявленное сверкой.
- **`Transfer`** — «Перевод между счетами»: техническая, исключается из всех сумм.

### 5.4 Transaction (агрегат)

| Поле | Тип | Примечание |
|---|---|---|
| `Id` | `TransactionId` | |
| `AccountId` | `AccountId` | |
| `Amount` | `Money` | знак по правилу 5.1 |
| `OccurredAt` | `DateTimeOffset` | |
| `CategoryId` | `CategoryId` | никогда не null; при захвате — `Unsorted` |
| `RawMerchant` | `string?` | сырая строка из уведомления |
| `MerchantKey` | `MerchantKey?` | нормализованная |
| `Note` | `string?` | |
| `Source` | `Manual` \| `Notification` \| `Reconciliation` | |
| `Kind` | `Regular` \| `Authorization` \| `Transfer` \| `Unidentified` | |
| `TransferGroupId` | `Guid?` | связывает две стороны перевода |
| `SupersededById` | `TransactionId?` | авторизация, схлопнутая в списание |
| `RawNotificationId` | `Guid?` | ссылка на исходное уведомление |

Операция неизменяема, кроме `CategoryId`, `Note`, `Kind`, `TransferGroupId`, `SupersededById`. Сумма и дата не редактируются — ошибочная операция удаляется и вводится заново, чтобы история сверок оставалась воспроизводимой.

**Исключается из расчёта расходов:** `Kind == Transfer`, `SupersededById != null`.

### 5.5 MonthBudget (агрегат)

| Поле | Тип |
|---|---|
| `Id` | `MonthBudgetId` |
| `Month` | `YearMonth` |
| `AvailableFunds` | `Money` — снимок располагаемых средств на момент планирования |
| `Envelopes` | `List<Envelope>` |
| `PlannedAt` | `DateTimeOffset` |

`Envelope` (сущность внутри агрегата): `CategoryId`, `PlannedAmount`.

Методы агрегата: `Plan(categoryId, amount)`, `Remove(categoryId)`, `RefreshAvailableFunds(money)`.
Все они проверяют инвариант И-1 и бросают `BudgetOverAllocatedException` при нарушении.

### 5.6 Reconciliation (агрегат)

| Поле | Тип |
|---|---|
| `Id` | `ReconciliationId` |
| `AccountId` | `AccountId` |
| `ObservedBalance` | `Money` |
| `ObservedAt` | `DateTimeOffset` |
| `PreviousBalance` | `Money` |
| `PreviousAt` | `DateTimeOffset` |
| `RecordedDelta` | `Money` — сумма операций между сверками |
| `Discrepancy` | `Money` — расхождение |
| `GeneratedTransactionId` | `TransactionId?` |

### 5.7 CategoryRule (агрегат) — этап 3

| Поле | Тип |
|---|---|
| `Id` | `Guid` |
| `Pattern` | `string` — подстрока нормализованного мерчанта |
| `CategoryId` | `CategoryId` |
| `Origin` | `Learned` \| `Manual` |
| `HitCount` | `int` |
| `LastUsedAt` | `DateTimeOffset?` |

При конфликте правил выигрывает **самый длинный** `Pattern`; при равной длине — с большим `HitCount`.

### 5.8 RawNotification — этап 3

| Поле | Тип |
|---|---|
| `Id` | `Guid` |
| `PackageName` | `string` |
| `Title`, `Text` | `string` |
| `PostedAt` | `DateTimeOffset` |
| `ParseStatus` | `Parsed` \| `Unparsed` \| `Ignored` |
| `TransactionId` | `TransactionId?` |

Хранится всегда, включая нераспознанные. Это материал для доработки парсеров и страховка от потери данных.

### 5.9 Fund (агрегат) — этап 4

| Поле | Тип | Примечание |
|---|---|---|
| `Id` | `FundId` | |
| `Name` | `string` | «OC+AC», «Podatek od nieruchomości», «Отпуск» |
| `TargetAmount` | `Money` | сумма платежа |
| `Periodicity` | `Monthly` \| `Bimonthly` \| `Quarterly` \| `SemiAnnual` \| `Annual` | |
| `NextDueDate` | `DateOnly` | |
| `AccruedBalance` | `Money` | **накоплено** — то, чего не было в экселе |
| `LinkedAccountId` | `AccountId?` | где физически лежат деньги |

Операции: `Accrue(money)`, `Spend(money)` (сбрасывает накопленное и сдвигает `NextDueDate`).

### 5.10 Asset (агрегат) — этап 5

| Поле | Тип |
|---|---|
| `Id` | `AssetId` |
| `Name` | `string` |
| `CurrentValue` | `Money` |
| `ValuedAt` | `DateTimeOffset` |
| `LiquidityTier` | `Immediate` \| `Month` \| `Locked` |
| `IsInMonthlyBudget` | `bool` — всегда `false`, кроме особых случаев |

---

## 6. Инварианты

| № | Формулировка | Где проверяется |
|---|---|---|
| **И-1** | `Σ Envelope.PlannedAmount ≤ MonthBudget.AvailableFunds` | `MonthBudget.Plan()` |
| **И-2** | У операции всегда есть категория; неразобранная получает `Unsorted` и **участвует** в суммах расхода | конструктор `Transaction` |
| **И-3** | Обе стороны внутреннего перевода имеют общий `TransferGroupId` и исключаются из расходов и доходов | `TransferMatcher` |
| **И-4** | Схлопнутая авторизация имеет `SupersededById` и не участвует в расчётах; выигрывает списание | `DuplicateMatcher` |
| **И-5** | Отрицательное расхождение при сверке не создаёт доход автоматически — требует явного решения пользователя | `Reconciliation.Create()` |
| **И-6** | Сверка не изменяет прошлые операции, только добавляет новую | `Reconciliation.Create()` |
| **И-7** | `Fund.AccruedBalance ≥ 0` и не превышает `TargetAmount` без явного подтверждения | `Fund.Accrue()` |
| **И-8** | Счета, категории и фонды не удаляются, а архивируются: у них есть история | репозитории |

Инвариант И-1 — центральный. Именно его отсутствие в экселе давало плановый дефицит, который можно было проигнорировать. В приложении нарушающая операция отклоняется.

---

## 7. Схема БД

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

Таблицы этапов 3–5 создаются миграциями соответствующих этапов, не заранее.

---

## 8. Прикладной слой

Один класс на сценарий. Именование: `<Глагол><Существительное>UseCase`.

| Сценарий | Этап | Вход → Выход |
|---|---|---|
| `AddManualTransactionUseCase` | 1 | сумма, дата, счёт, категория → `TransactionId` |
| `DeleteTransactionUseCase` | 1 | `TransactionId` → void |
| `PlanMonthUseCase` | 1 | месяц, список (категория, сумма) → `MonthBudgetId`, может бросить `BudgetOverAllocatedException` |
| `GetMonthOverviewUseCase` | 1 | месяц → располагаемые средства, осталось распределить, конверты с план/факт/остаток |
| `ReconcileAccountUseCase` | 2 | счёт, наблюдаемый остаток, дата → `Discrepancy`, созданная операция |
| `GetDashboardUseCase` | 2 | — → сводка главного экрана |
| `IngestNotificationUseCase` | 3 | сырое уведомление → операция или `Unparsed` |
| `AssignCategoryUseCase` | 3 | `TransactionId`, категория, флаг «создать правило» → void |
| `GetInboxUseCase` | 3 | — → операции с категорией `Unsorted` |
| `AccrueFundsForMonthUseCase` | 4 | месяц → начисления по всем фондам |
| `GetRunwayUseCase` | 5 | — → месяцев автономии по уровням ликвидности |
| `ExportBackupUseCase` / `ImportBackupUseCase` | 6 | → JSON-файл |

---

## 9. Инфраструктура

### 9.1 EF Core

- `CastellanDbContext`, конфигурации через `IEntityTypeConfiguration<T>`, отдельный класс на агрегат.
- `Money` — конвертер значений `Money ↔ long`.
- `YearMonth` — раскладывается на два столбца (`Year`, `Month`).
- `DateTimeOffset` — конвертер в ISO-8601 строку (провайдер SQLite теряет смещение при стандартном маппинге).
- Запросы на чтение — `AsNoTracking()`.
- Прагмы при открытии соединения: `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`.

### 9.2 Перехват уведомлений (этап 3)

```
Platforms/Android/Services/CastellanNotificationListenerService.cs
```

- Наследник `Android.Service.Notification.NotificationListenerService`, зарегистрирован через `[Service]` с `Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE"` и intent-filter `android.service.notification.NotificationListenerService`.
- Разрешение **нельзя запросить обычным диалогом**: пользователя надо отправить в `Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS` и проверять `NotificationManagerCompat.getEnabledListenerPackages()` при каждом старте.
- `OnNotificationPosted` фильтрует по `PackageName` (список пакетов банков в настройках), извлекает `EXTRA_TITLE` и `EXTRA_TEXT`, пишет `RawNotification` и вызывает `IngestNotificationUseCase`. Пакет `com.google.android.apps.walletnfcrel` (Google Portfel) — всегда `Ignored`: при NFC-платежах он дублирует банковское уведомление, но не содержит нужных данных.
- Сервис работает вне жизненного цикла UI: собственный scope DI и собственное подключение к БД. Долгую работу в `OnNotificationPosted` не делать — только запись и передача в очередь.

### 9.3 Парсеры банков

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

Реализация на регулярных выражениях, вынесенных в конфигурационный JSON, чтобы правила правились без пересборки. Один парсер на банк; неизвестный пакет — `Ignored`.

Формат уведомлений у каждого банка свой и меняется без предупреждения. Отсюда требование хранить сырой текст (5.8) и покрывать парсеры тестами на реальных примерах.

### 9.4 Виджет и быстрый ввод (этап 2)

`AppWidgetProvider` с кнопкой, открывающей полупрозрачное `Activity` быстрого ввода: числовая клавиатура, сетка категорий, кнопка «Готово». Цель — три касания.

---

## 10. Интерфейс

### 10.1 Экраны

| Экран | Этап | Содержание |
|---|---|---|
| Главный | 1 → 2 | располагаемые средства; осталось распределить; осталось потратить; счётчик инбокса; предупреждение о здоровье захвата |
| Конверты месяца | 1 | категория, план, потрачено, остаток, полоса прогресса; красная при перерасходе |
| Планирование | 1 | распределение по конвертам, живой счётчик «осталось распределить», блокировка сохранения при нарушении И-1 |
| Операции | 1 | список по датам, фильтры по счёту и категории, поиск |
| Счета | 1 → 2 | список счетов с вычисленным остатком, кнопка «сверить» |
| Сверка | 2 | ввод фактического остатка, показ расхождения до подтверждения |
| Быстрый ввод | 2 | сумма → категория → готово |
| Инбокс | 3 | операции `Unsorted`, назначение категории в одно касание, переключатель «запомнить правило» |
| Правила | 3 | список правил, редактирование, удаление |
| Фонды | 4 | цель, срок, накоплено, «нужно в месяц», индикатор отставания |
| Активы | 5 | список с уровнем ликвидности |
| Запас прочности | 5 | месяцы автономии по трём уровням ликвидности |
| Настройки | 6 | банки и пакеты, экспорт/импорт, доступ к уведомлениям |

### 10.2 Правила отображения

- Расход всегда с минусом и в одном цвете; никаких «красное — плохо» на обычных тратах.
- Перерасход конверта — единственное место, где допустим тревожный цвет.
- «Неопознанное» показывается наравне с другими категориями, без выделения и без формулировок вины.
- Ни одного экрана, требующего вспоминать прошлое.

---

## 11. Алгоритмы

### 11.1 Дедупликация (этап 3)

Кандидат в дубликаты для новой операции T:

- тот же `AccountId`;
- `|T.OccurredAt − C.OccurredAt| ≤ 1 день`;
- `T.MerchantKey == C.MerchantKey`;
- `|T.Amount| == |C.Amount|` или расхождение `≤ 2%` (конвертация валюты, чаевые).

Если найден кандидат с `Kind == Authorization`, а новая операция `Regular` — проставить кандидату `SupersededById = T.Id`. Обратный порядок (списание пришло раньше авторизации) — новая помечается как схлопнутая.

Порог 2% и окно 1 день вынести в настройки: подобрать эмпирически по своим банкам.

### 11.2 Схлопывание переводов (этап 3)

Две операции A и B образуют перевод, если:

- `A.AccountId != B.AccountId`, оба счёта свои;
- `A.Amount == −B.Amount`;
- `|A.OccurredAt − B.OccurredAt| ≤ 48 часов`;
- ни одна ещё не входит в `TransferGroup`.

Обеим присваивается общий `TransferGroupId`, `Kind = Transfer`, `CategoryId = Transfer`.

Ложное срабатывание возможно при совпадении сумм — поэтому предлагать подтверждение, а не схлопывать молча.

### 11.3 Нормализация мерчанта

1. Верхний регистр, замена не-буквенно-цифровых символов на пробел, схлопывание пробелов.
2. Отсечение известных префиксов агрегаторов: `PAYU`, `PAYPAL`, `GOOGLE`, `APPLE PAY`, `TPAY`, `PRZELEWY24`, `BLIK`.
3. Удаление номеров точек: хвостовые токены вида `Z1234`, `NR 12`, `#0345`.
4. Обрезка до 40 символов.

Онлайн-платежи через агрегатор часто оставляют только имя агрегатора. Это ограничение метода: такие операции остаются в инбоксе и требуют ручного решения. Ожидаемая доля — оценить на реальных данных, это одна из проверяемых гипотез проекта.

### 11.4 Автокатегоризация (этап 3)

При захвате: найти все `CategoryRule`, чей `Pattern` содержится в `MerchantKey`; выбрать с самым длинным `Pattern`; при равенстве — с большим `HitCount`; инкрементировать `HitCount`.

При ручном назначении категории операции с непустым `MerchantKey` и включённом переключателе «запомнить» — создать правило `Origin = Learned` с `Pattern = MerchantKey`.

### 11.5 Сверка (этап 2)

```
RecordedDelta = Σ Transaction.Amount, где AccountId = A
                и PreviousAt < OccurredAt ≤ ObservedAt
                и SupersededById IS NULL

Discrepancy = (ObservedBalance − PreviousBalance) − RecordedDelta
```

- `Discrepancy < 0` — незаписанные расходы. Создать операцию `Kind = Unidentified`, `CategoryId = Unidentified`, `Amount = Discrepancy`.
- `Discrepancy > 0` — незаписанный доход **или задвоенные расходы**. По И-5 автоматически не создавать: показать список операций периода и спросить, что делать.
- `Discrepancy == 0` — обновить `LastReconciledBalance` и `LastReconciledAt`.

### 11.6 Здоровье захвата (этап 3)

Хранить `LastNotificationAt` в настройках. Если старше 1 дня — баннер на главном экране с проверкой `NotificationManagerCompat.getEnabledListenerPackages()`.

Android может убить фоновый сервис ради экономии батареи молча, без ошибки. Это самый вероятный отказ системы в эксплуатации. Второй рубеж защиты — сверка: рост «Неопознанного» обнаружит сбой независимо от баннера.

### 11.7 Начисление в фонды (этап 4)

```
МесяцевДоПлатежа   = целых месяцев между сегодня и NextDueDate (минимум 1)
НужноВМесяц        = (TargetAmount − AccruedBalance) / МесяцевДоПлатежа
Отставание         = ОжидаемоНакоплено − AccruedBalance
```

Где `ОжидаемоНакоплено = TargetAmount × (пройдено месяцев периода / всего месяцев периода)`.

Сумма `НужноВМесяц` по всем фондам добавляется в план месяца отдельной строкой и участвует в инварианте И-1 — иначе фонды остаются благим намерением.

### 11.8 Запас прочности (этап 5)

```
НесрезаемыйРасход = Σ PlannedAmount по категориям с флагом «обязательная»
                    + Σ НужноВМесяц по фондам

Ликвидные(tier)   = Σ Account.CurrentBalance где LiquidityTier ≤ tier
                    + Σ Asset.CurrentValue   где LiquidityTier ≤ tier

Автономия(tier)   = Ликвидные(tier) / НесрезаемыйРасход   [месяцев]
```

Показывать три числа по трём уровням ликвидности, а не одно усреднённое: «доступно завтра» и «заморожено» — принципиально разные деньги.

Это тот же расчёт, что `ReadinessScore` в Bastion, в других единицах: запас, делённый на скорость расходования, даёт горизонт автономии.

---

## 12. Этапы подробно

### Этап 0 — каркас

**Работы:** структура решения по 4.1; `CastellanDbContext` и первая миграция; регистрация DI; MAUI Shell с заглушками навигации; xUnit-проекты; GitHub Actions — сборка, тесты, артефакт APK; `.gitignore`, `.editorconfig`, лицензия.

**Готово, когда:** APK ставится на телефон и запускается; `dotnet test` зелёный в CI; миграция создаёт пустую БД на устройстве.

### Этап 1 — ручной учёт

**Работы:** агрегаты `Account`, `Category`, `Transaction`, `MonthBudget` с инвариантами И-1, И-2, И-8; сценарии `AddManualTransaction`, `DeleteTransaction`, `PlanMonth`, `GetMonthOverview`; экраны «Главный», «Конверты», «Планирование», «Операции», «Счета»; сид системных категорий.

**Готово, когда:** можно завести счета, распланировать месяц (сверх располагаемых средств не пускает), записать траты руками и увидеть план/факт/остаток по конвертам.

**Не делать:** отчёты, графики, экспорт.

### Этап 2 — сверка и быстрый ввод

**Работы:** `Reconciliation`, инварианты И-5 и И-6; вычисляемый остаток счёта; сценарии `ReconcileAccount`, `GetDashboard`; экран сверки; экран быстрого ввода; виджет на домашний экран.

**Готово, когда:** ввод фактического остатка порождает «Неопознанное»; пропуск нескольких дней не ломает картину; трата записывается в три касания.

### Этап 3 — перехват уведомлений

Ключевой этап. Ради него всё затевалось.

**Работы:** `NotificationListenerService` и обработка разрешения; `RawNotification`; парсеры двух банков на реальных примерах; `IngestNotificationUseCase`; дедупликация (11.1); схлопывание переводов (11.2); нормализация мерчанта (11.3); `CategoryRule` и автокатегоризация (11.4); экраны «Инбокс» и «Правила»; здоровье захвата (11.6).

**Порядок внутри этапа:** сначала сбор сырых уведомлений и экран их просмотра (без парсинга) — пусть накопятся реальные данные за неделю. Парсеры писать по накопленному, а не по воображаемому формату.

**Готово, когда:** за неделю эксплуатации доля операций, потребовавших ручного ввода, ниже 20%, а инбокс разбирается меньше чем за минуту в день.

### Этап 4 — фонды нерегулярных платежей

**Работы:** агрегат `Fund`, инвариант И-7; расчёты 11.7; включение `НужноВМесяц` в план месяца; экран «Фонды» с индикатором отставания.

**Готово, когда:** страховка, налог и отпуск заведены, и приложение отвечает на вопрос «к декабрю хватит или отстаю».

### Этап 5 — активы и запас прочности

**Работы:** агрегат `Asset`; уровни ликвидности на счетах и активах; флаг обязательности на категориях; расчёт 11.8; экран «Запас прочности».

**Готово, когда:** видно, на сколько месяцев хватит по трём уровням ликвидности, и как цифра меняется при отключении источника дохода.

### Этап 6 — эксплуатация и портфолио

**Работы:** экспорт и импорт JSON; напоминание о бэкапе раз в месяц; README (стек, архитектура, доменная модель, обоснование решений, тесты, ограничения); диаграммы; скриншоты; подписанный релизный APK в GitHub Releases.

**Готово, когда:** приложение можно переставить на новый телефон без потери данных, а репозиторий читается посторонним человеком.

---

## 13. Тестирование

| Уровень | Что покрывается | Инструмент |
|---|---|---|
| Domain | инварианты И-1…И-8, арифметика `Money`, границы `YearMonth`, начисление фондов, автономия | xUnit, чистые тесты без БД |
| Application | сценарии на репозиториях-заглушках | xUnit |
| Infrastructure | конфигурации EF, конвертеры, миграции | xUnit + SQLite in-memory |
| Парсеры | реальные тексты уведомлений обоих банков: покупка, онлайн, перевод, авторизация, списание | xUnit, табличные тесты |
| Алгоритмы | дедупликация и схлопывание переводов на сконструированных наборах, включая ложные срабатывания | xUnit |

Testcontainers не нужен: внешних зависимостей нет.

Обязательные негативные тесты: планирование сверх средств; сверка с положительным расхождением; двойное уведомление; перевод между своими счетами с равными суммами; уведомление неизвестного формата.

---

## 14. Нефункциональные требования

- Холодный старт до интерактивного главного экрана — менее 2 секунд на среднем устройстве.
- Обработка уведомления — менее 100 мс в `OnNotificationPosted`; всё тяжёлое асинхронно.
- Размер БД за 5 лет — единицы мегабайт; оптимизация не требуется.
- Расход батареи фоновым сервисом — на уровне погрешности: сервис событийный, без опроса.
- Все данные только на устройстве. Никаких сетевых вызовов вообще — в проекте нет HTTP-клиента.
- Приложение работает полностью офлайн, включая первый запуск.

---

## 15. Риски

| Риск | Вероятность | Что делать |
|---|---|---|
| Уведомления банка не содержат мерчанта, только сумму | средняя | проверить **до** этапа 3 на реальных примерах; если так — часть замысла отпадает, останется «сумма + выбор категории», что всё равно лучше эксель |
| Android убивает фоновый сервис | высокая | здоровье захвата (11.6) + сверка как второй рубеж; отключение оптимизации батареи для приложения |
| Банк меняет формат уведомлений | средняя | сырые уведомления хранятся всегда; правила в конфиге; тесты на примерах |
| Онлайн-платежи дают только имя агрегатора | высокая | принять как ограничение; такие операции остаются в инбоксе |
| Этапы 1–2 затягиваются, интерес гаснет | **высокая** | жёстко ограничить объём этапов 1–2; не добавлять в них ничего сверх списка |
| Проект повторяет судьбу Bastion: написан, но не используется | средняя | критерий успеха — эксплуатация, а не наличие кода (раздел 16) |

### 15.3 О публикации

Google Play жёстко ограничивает приложения, запрашивающие доступ к уведомлениям, и требует обоснования назначения. Приложение личное, публикация не планируется; распространение — установка подписанного APK на своё устройство. Если публикация когда-нибудь понадобится, это отдельная работа с политикой доступа и декларацией.

---

## 16. Критерий успеха

Не «написано», а через два месяца после этапа 3:

- доля «Неопознанного» в общем расходе — менее 10%;
- ручного ввода — менее 20% операций;
- разбор инбокса — менее минуты в день;
- приложение открывается ежедневно без напоминаний;
- бюджет на месяц планируется до его начала.

Если «Неопознанное» устойчиво выше 25% — механизм захвата не работает; чинить его, а не добавлять функции.

---

## 17. Метод работы

Следствие ретроспективы по Bastion: приложение получилось, навык — нет.

Проверка на каждом этапе: получится ли объяснить вслух, почему сделано именно так, и какие альтернативы отвергнуты. Если нет — этап не закрыт.

---

## 18. Открытые вопросы

1. Названия пакетов приложений обоих банков и реальные примеры уведомлений — **блокирует этап 3**, собрать заранее.
2. ~~Присылает ли хотя бы один банк отдельные уведомления об авторизации и о списании, или только одно.~~ **Решено:** банк присылает только уведомление о списании. При оплате телефоном (NFC) приходит два уведомления: от банка (парсить) и от Google Portfel (игнорировать по `PackageName`).
3. Список категорий: перенести из польского шаблона или составить заново под фактические траты.
4. Признак «обязательная» на категориях — вводить на этапе 1 (дешевле) или на этапе 5 (когда понадобится).
5. Нужна ли история планов по месяцам или достаточно текущего и прошлого.
