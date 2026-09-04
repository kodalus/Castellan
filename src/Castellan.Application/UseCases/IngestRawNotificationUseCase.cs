using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Castellan.Application.Parsers;
using Castellan.Application.Repositories;
using Castellan.Application.Services;
using Castellan.Domain;
using Castellan.Domain.Aggregates;
using Castellan.Domain.ValueObjects;

namespace Castellan.Application.UseCases;

public sealed partial class IngestRawNotificationUseCase(
    IRawNotificationRepository rawNotifications,
    IAccountRepository accounts,
    ITransactionRepository transactions,
    ICategoryRuleRepository categoryRules,
    IUnitOfWork uow,
    IEnumerable<INotificationParser> parsers)
{
    private static readonly string[] BankKeywords = Banks.Known;

    /// <summary>Okno na parę „bank + Portfel Google" dla jednej płatności.</summary>
    private const int CrossSourceWindowMinutes = 180;

    /// <summary>
    /// Okno na pojedyncze powiadomienie opisujące nogę przelewu własnego, dla którego
    /// para już istnieje. Wąskie, bo warunek opiera się na samej kwocie.
    /// </summary>
    private const int OwnTransferLegWindowMinutes = 5;

    public static readonly IReadOnlySet<string> AllowedPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        "pl.ing.mojeing",
        "com.revolut.revolut",
        "com.google.android.apps.walletnfcrel",
    };

    public sealed record Input(string PackageName, string Title, string Text, DateTimeOffset PostedAt);

    /// <summary>
    /// Jedno powiadomienie naraz w calym procesie.
    ///
    /// Nasluch Androida odpala kazde powiadomienie przez „Task.Run" i nie czeka na wynik,
    /// a kazde takie wywolanie dostaje WLASNY kontener i wlasny DbContext. Bez tej blokady
    /// dwa doreczenia tego samego powiadomienia czytaja baze zanim ktorekolwiek zdazy
    /// zapisac — obydwa widza pustke, obydwa uznaja sie za pierwsze i deduplikacja nie ma
    /// czego z czym porownac. Skutek: KAZDE powiadomienie zaklada dwie transakcje.
    ///
    /// Wystarczy odstep milisekund, a odczyty i zapis do SQLite na telefonie trwaja
    /// znacznie dluzej — wiec „dorecza po kolei" nie znaczy „wykonuje sie po kolei".
    ///
    /// Blokada procesowa jest tu na miejscu: aplikacja ma jednego uzytkownika i jeden
    /// proces, a przyjmowanie powiadomien to z natury sekcja krytyczna na wspoldzielonym
    /// stanie, ktorej nie da sie obronic w obrebie jednego DbContextu.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task ExecuteAsync(Input input, CancellationToken ct = default)
    {
        if (!AllowedPackages.Contains(input.PackageName)) return;

        await Gate.WaitAsync(ct);
        try
        {
            await IngestAsync(input, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task IngestAsync(Input input, CancellationToken ct)
    {
        var maskedTitle = MaskSensitiveData(input.Title);
        var maskedText  = MaskSensitiveData(input.Text);

        // To samo powiadomienie doreczone drugi raz nie jest druga platnoscia. Deduplikacja
        // transakcji by je wprawdzie wychwycila, ale dopiero po zalozeniu drugiego wpisu
        // w Skrzynce i po oznaczeniu go jako sparsowany wskazaniem na transakcje, ktorej
        // nigdy nie dodano. Taniej i uczciwiej odrzucic je od razu.
        if (await rawNotifications.ExistsAsync(input.PackageName, maskedTitle, maskedText, input.PostedAt, ct))
            return;

        // Portfel Google bywa jedynym śladem płatności NFC telefonem (np. dla ING,
        // który przy zbliżeniówce z telefonu nie wysyła własnego powiadomienia) —
        // wcześniej był tu na stałe ignorowany, co gubiło te transakcje całkowicie.
        var notification = RawNotification.CreateUnparsed(input.PackageName, maskedTitle, maskedText, input.PostedAt);

        await rawNotifications.AddAsync(notification, ct);

        if (notification.ParseStatus == ParseStatus.Unparsed)
        {
            var rules = await categoryRules.ListAsync(ct);
            await TryAutoParseAsync(notification, input.PackageName, input.PostedAt, rules, ct);
        }

        await uow.SaveChangesAsync(ct);
    }

    private async Task TryAutoParseAsync(
        RawNotification notification,
        string packageName,
        DateTimeOffset postedAt,
        IReadOnlyList<CategoryRule> rules,
        CancellationToken ct)
    {
        var parser = parsers.FirstOrDefault(p => p.PackageName == packageName);
        if (parser is null) return;

        // Przelew własny ma własną ścieżkę: jedno powiadomienie opisuje obie strony,
        // więc powstaje od razu para, a nie pojedynczy wpis.
        var transfer = parser.TryParseTransfer(notification.Title, notification.Text);
        if (transfer is not null
            && await TryIngestOwnTransferAsync(notification, transfer, packageName, postedAt, ct))
            return;

        var parsed = parser.TryParse(notification.Title, notification.Text);
        if (parsed is null) return;

        var account = await FindAccountAsync(packageName, parsed.AccountHint, ct);
        if (account is null) return;

        var tx = Transaction.CreateFromNotification(
            account.Id, parsed.Amount, postedAt, notification.Id, parsed.Merchant);

        // Normalize and set MerchantKey
        var merchantKey = MerchantKeyNormalizer.Normalize(parsed.Merchant);
        tx.SetMerchantKey(merchantKey);

        // Apply best matching rule: longest pattern wins, tie-break by HitCount
        var matchText = merchantKey ?? parsed.Merchant;
        var matchedRule = rules
            .Where(r => r.Matches(matchText))
            .OrderByDescending(r => r.Pattern.Length)
            .ThenByDescending(r => r.HitCount)
            .FirstOrDefault();

        if (matchedRule is not null)
        {
            tx.AssignCategory(matchedRule.CategoryId);
            matchedRule.RecordHit();
        }

        // Deduplication check (spec 11.1)
        var dedupResult = await TryDeduplicateAsync(tx, account.Id, postedAt, merchantKey, packageName, ct);
        if (dedupResult == DeduplicateResult.ExactDuplicate)
        {
            // Drop silently — notification still marked as parsed below
            notification.MarkParsed(tx.Id);
            return;
        }

        await transactions.AddAsync(tx, ct);

        // Transfer detection (spec 11.2) — propose after adding tx so it's reachable
        await TryProposeTransferAsync(tx, account.Id, postedAt, ct);

        notification.MarkParsed(tx.Id);
    }

    private enum DeduplicateResult { None, Authorization, ExactDuplicate }

    private async Task<DeduplicateResult> TryDeduplicateAsync(
        Transaction tx,
        AccountId accountId,
        DateTimeOffset postedAt,
        string? merchantKey,
        string packageName,
        CancellationToken ct)
    {
        var since = postedAt.AddHours(-25);
        var recent = await transactions.ListRecentAsync(since, ct);

        var candidate = merchantKey is null ? null : recent.FirstOrDefault(t =>
            t.AccountId == accountId &&
            !t.SupersededById.HasValue &&
            t.MerchantKey is not null &&
            t.MerchantKey.Equals(merchantKey, StringComparison.OrdinalIgnoreCase) &&
            AmountMatches(t.Amount.Grosze, tx.Amount.Grosze));

        // Portfel Google i apka banku często zgłaszają tę samą płatność NFC pod
        // zupełnie inną nazwą sprzedawcy — Portfel pokazuje nazwę prawną spółki
        // ("JMP S.A. BIEDRONKA 591"), a bank markę ("Biedronka") — więc dopasowanie
        // po kluczu sprzedawcy regularnie zawodzi dla tej pary. Gdy nic nie znajdzie
        // po nazwie, spróbuj wąskiego okna: ta sama kwota co do grosza, to samo
        // konto, kilkanaście minut różnicy — wystarczająco rzadki zbieg okoliczności,
        // żeby bezpiecznie uznać to za tę samą transakcję zgłoszoną z dwóch źródeł.
        // Ta sama kwota co do grosza, to samo konto, ale zgłoszona przez INNĄ aplikację
        // — to para bank plus Portfel Google dla jednej płatności. Okno może być tu
        // szerokie, bo warunek „różne źródła" sam w sobie odsiewa przypadkowe zbiegi:
        // żeby wpaść fałszywie, dwie RÓŻNE płatności na identyczną kwotę musiałyby
        // zostać zgłoszone, każda tylko przez jedno źródło, i to inne dla każdej.
        //
        // Szerokie okno jest konieczne, bo „Twój Asystent" z ING nie przychodzi od razu
        // po płatności — przy oknie kilkunastominutowym para gubiła się i powstawał
        // duplikat.
        var sourcePackage = (await rawNotifications.ListParsedSinceAsync(since, ct))
            .Where(r => r.TransactionId is not null)
            .GroupBy(r => r.TransactionId!.Value)
            .ToDictionary(g => g.Key, g => g.First().PackageName);

        // Warunek „to samo konto" jest tu ZŁAGODZONY: wystarczy to samo konto ALBO
        // WSPÓLNE SŁOWO w nazwie sprzedawcy. Dopasowanie konta bywa niepewne (bank
        // i Portfel Google nazywają to samo konto inaczej), a gdy się rozjedzie, twardy
        // warunek na koncie kasuje deduplikację i powstaje duplikat.
        //
        // Porównanie CAŁYCH nazw też nie wystarcza, bo te dwa źródła prawie nigdy nie
        // nazywają sprzedawcy tak samo: Portfel podaje „CASTORAMA TARNOWSKIEG8", bank
        // „Castorama"; przy Biedronce Portfel podaje nazwę prawną spółki, a bank markę.
        // Wspólne słowo jest tym, co naprawdę je łączy — i nadal wystarczająco rzadkim
        // zbiegiem okoliczności przy identycznej kwocie z DWÓCH różnych aplikacji.
        candidate ??= recent.FirstOrDefault(t =>
            !t.SupersededById.HasValue &&
            t.Amount.Grosze == tx.Amount.Grosze &&
            sourcePackage.TryGetValue(t.Id, out var pkg) &&
            !string.Equals(pkg, packageName, StringComparison.Ordinal) &&
            (t.AccountId == accountId || MerchantsShareAWord(t.MerchantKey, merchantKey)) &&
            Math.Abs((t.OccurredAt - postedAt).TotalMinutes) <= CrossSourceWindowMinutes);

        // Bank potrafi wysłać i powiadomienie zbiorcze o przelewie własnym, i osobne
        // o obciążeniu czy uznaniu. Wpis z pary już istnieje i ma właściwe konto —
        // pojedyncze powiadomienie o tej samej kwocie jest jego duplikatem, nawet jeśli
        // wskazuje inne konto (bo samo konta nie zna).
        //
        // Okno jest tu CELOWO wąskie, węższe niż przy parze bank + Portfel Google.
        // Ten warunek patrzy na samą kwotę, więc trafiłby też w prawdziwy zakup za
        // dokładnie tę samą kwotę — a cicho połknięta transakcja jest gorsza niż
        // widoczny duplikat, który da się skasować. Powiadomienia banku o JEDNEJ
        // operacji przychodzą w odstępie sekund, nie minut.
        candidate ??= recent.FirstOrDefault(t =>
            !t.SupersededById.HasValue &&
            (t.TransferGroupId is not null || t.ProposedTransferGroupId is not null) &&
            t.Amount.Grosze == tx.Amount.Grosze &&
            Math.Abs((t.OccurredAt - postedAt).TotalMinutes) <= OwnTransferLegWindowMinutes);

        // Zapasowo wąskie okno bez rozróżniania źródła — na wypadek powiadomień,
        // dla których nie znamy pakietu (np. sprzed dodania tego zapisu).
        //
        // RÓŻNI SPRZEDAWCY ROZSTRZYGAJĄ NA NIE. Ta reguła patrzyła wcześniej wyłącznie
        // na kwotę i konto, więc prawdziwy zakup ginął, gdy przypadkiem kosztował tyle
        // samo co coś sprzed kilkunastu minut. Wykryte własnym testem kontrolnym: zakup
        // w Biedronce za 1 zł zniknął, bo dziesięć minut wcześniej poszedł przelew na
        // 1 zł. Reguła po samej kwocie zostaje tam, po co powstała — gdy przynajmniej
        // jedna ze stron nie wie, u kogo zapłacono.
        // Noga przelewu jest już w pełni wyjaśniona przez sam przelew, a własnej nazwy
        // sprzedawcy nie ma — więc porównanie po sprzedawcy nie ma jej z czym zestawić.
        // Powiadomienie, które faktycznie opisuje tę noge, przygarnia przejście wyżej
        // (okno 5 minut). Wszystko poza nim to osobne zdarzenie i nie ma prawa w nią wpaść.
        candidate ??= recent.FirstOrDefault(t =>
            t.AccountId == accountId &&
            !t.SupersededById.HasValue &&
            t.TransferGroupId is null &&
            t.ProposedTransferGroupId is null &&
            t.Amount.Grosze == tx.Amount.Grosze &&
            !MerchantsKnownAndDifferent(t.MerchantKey, merchantKey) &&
            Math.Abs((t.OccurredAt - postedAt).TotalMinutes) <= 15);

        if (candidate is null) return DeduplicateResult.None;

        if (candidate.Kind == TransactionKind.Authorization)
        {
            // Authorization pre-auth → supersede it with the real charge
            candidate.Supersede(tx.Id);
            return DeduplicateResult.Authorization;
        }

        // Same kind — this is an exact duplicate; don't add it
        return DeduplicateResult.ExactDuplicate;
    }

    /// <summary>
    /// Czy nazwy sprzedawcy mają choć jedno wspólne znaczące słowo. Krótkie człony
    /// („S", „A", „PL") są pomijane, bo trafiałyby się przypadkiem.
    /// </summary>
    private static bool MerchantsShareAWord(string? a, string? b)
    {
        if (a is null || b is null) return false;

        var first = SignificantWords(a);
        return first.Count > 0 && SignificantWords(b).Any(first.Contains);
    }

    private static HashSet<string> SignificantWords(string merchant) =>
        [.. NonLetters().Split(Fold(merchant)).Where(w => w.Length >= 4)];

    /// <summary>
    /// Prawda tylko wtedy, gdy OBIE strony wiedzą, u kogo zapłacono, i są to różni
    /// sprzedawcy. Brak nazwy po którejkolwiek stronie to brak informacji, a nie dowód
    /// różnicy — wtedy decydują pozostałe warunki.
    /// </summary>
    private static bool MerchantsKnownAndDifferent(string? a, string? b) =>
        a is not null && b is not null
        && !a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static bool AmountMatches(long a, long b)
    {
        if (a == b) return true;
        if (b == 0) return a == 0;
        return Math.Abs(a - b) <= Math.Abs(b) * 2 / 100; // ≤2% diff
    }

    /// <summary>
    /// Buduje parę wpisów z jednego powiadomienia o przelewie własnym. Zwraca false, gdy
    /// nie da się rozstrzygnąć kont — wtedy powiadomienie idzie zwykłą ścieżką.
    ///
    /// Powstaje PROPOZYCJA, a nie gotowy przelew, bo przelew na konto oszczędnościowe
    /// kryje dwa różne zdarzenia — przekładanie i odkładanie na rezerwę — i tylko
    /// użytkownik wie które.
    /// </summary>
    private async Task<bool> TryIngestOwnTransferAsync(
        RawNotification notification,
        ParsedTransfer parsed,
        string packageName,
        DateTimeOffset postedAt,
        CancellationToken ct)
    {
        var active = (await accounts.ListAsync(ct)).Where(a => !a.IsArchived).ToList();
        var pool = NarrowToBank(active, BankOf(packageName, accountHint: null));
        if (pool.Count < 2) return false;

        var from = BestByHint(pool, parsed.FromAccountHint);
        var to = BestByHint(pool, parsed.ToAccountHint);

        // Gdy jedna strona się nie dopasowała, a w banku są dokładnie dwa konta, druga
        // jest wyznaczona przez eliminację. To realny przypadek: bank nazywa konta po
        // swojemu („Direct Rika"), a użytkownik po swojemu („ING") — żadne słowo się
        // nie pokrywa, ale skoro to nie konto docelowe, to musi być to drugie.
        if (from is null && to is not null && pool.Count == 2)
            from = pool.First(a => a.Id != to.Id);
        if (to is null && from is not null && pool.Count == 2)
            to = pool.First(a => a.Id != from.Id);

        if (from is null || to is null || from.Id == to.Id) return false;

        var magnitude = Math.Abs(parsed.Amount.Grosze);
        if (magnitude == 0) return false;

        var recent = await transactions.ListRecentAsync(postedAt.AddMinutes(-15), ct);

        var outgoing = await AdoptOrCreateLegAsync(recent, pool, from.Id, -magnitude, postedAt, notification, ct);
        var incoming = await AdoptOrCreateLegAsync(recent, pool, to.Id, magnitude, postedAt, notification, ct);

        var groupId = Guid.NewGuid();
        outgoing.ProposeTransfer(groupId);
        incoming.ProposeTransfer(groupId);

        notification.MarkParsed(outgoing.Id);
        return true;
    }

    /// <summary>
    /// Bierze wpis, który powstał już z pojedynczego powiadomienia o tej samej kwocie,
    /// i przypina go do właściwego konta — zamiast dokładać drugi obok. Bank potrafi
    /// wysłać i powiadomienie zbiorcze, i osobne o obciążeniu; kolejność jest loterią,
    /// a bez przygarniania z jednego przelewu robiłyby się cztery wpisy.
    /// </summary>
    private async Task<Transaction> AdoptOrCreateLegAsync(
        IReadOnlyList<Transaction> recent,
        List<Account> pool,
        AccountId accountId,
        long grosze,
        DateTimeOffset postedAt,
        RawNotification notification,
        CancellationToken ct)
    {
        var orphan = recent.FirstOrDefault(t =>
            t.Amount.Grosze == grosze &&
            !t.SupersededById.HasValue &&
            t.TransferGroupId is null &&
            t.ProposedTransferGroupId is null &&
            pool.Any(a => a.Id == t.AccountId) &&
            Math.Abs((t.OccurredAt - postedAt).TotalMinutes) <= 15);

        if (orphan is not null)
        {
            orphan.SetAccount(accountId);
            return orphan;
        }

        var leg = Transaction.CreateFromNotification(
            accountId, new Money(grosze), postedAt, notification.Id, "Przelew własny");
        await transactions.AddAsync(leg, ct);
        return leg;
    }

    private static string? BankOf(string packageName, string? accountHint) =>
        packageName switch
        {
            "pl.ing.mojeing"      => Banks.Ing,
            "com.revolut.revolut" => Banks.Revolut,
            _                     => BankKeywords.FirstOrDefault(b =>
                                         accountHint?.Contains(b, StringComparison.OrdinalIgnoreCase) == true),
        };

    private static List<Account> NarrowToBank(List<Account> active, string? bank)
    {
        if (bank is null) return active;

        var byKey = active
            .Where(a => string.Equals(a.BankKey, bank, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byKey.Count > 0) return byKey;

        var byName = active
            .Where(a => a.Name.Contains(bank, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count > 0 ? byName : active;
    }

    /// <summary>
    /// Najlepsze dopasowanie konta do tekstowej podpowiedzi. Null, gdy nic nie pasuje —
    /// wywołujący decyduje, czy zgadywać dalej.
    /// </summary>
    private static Account? BestByHint(List<Account> pool, string? accountHint, string? ignoreWord = null)
    {
        if (string.IsNullOrWhiteSpace(accountHint)) return null;

        var ignored = ignoreWord is null ? null : Fold(ignoreWord);
        var hintTokens = Tokenize(accountHint).Where(t => t != ignored).ToArray();

        // Podpowiedz zlozona z samej nazwy banku nie niesie zadnej informacji o koncie.
        // Wazne, zeby wtedy NIE schodzic do zapasowego dopasowania po zawieraniu: ono
        // preferuje najdluzsza nazwe, wiec podpowiedz „karta Revolut” trafialaby
        // w „Revolut Wspólny” zamiast w „Revolut”. Lepiej oddac decyzje regule domyslnej.
        if (hintTokens.Length == 0) return null;
        var best = pool
            .Select(a =>
            {
                var tokens = Tokenize(a.Name);
                var score = TokenOverlap(hintTokens, tokens);

                // Skrótowiec: „OKO" to inicjały „Otwarte Konto Oszczędnościowe" — tak
                // ten sam rachunek nazywa bank w jednym miejscu i użytkownik w drugim,
                // a wspólnego słowa nie ma tam ani jednego.
                if (score == 0 && AcronymMatches(tokens, accountHint)) score = 1;

                // Przy remisie wygrywa konto, którego nazwa NIE ma słów spoza
                // podpowiedzi. Inaczej podpowiedź „Revolut" trafiałaby w „Revolut
                // Wspólny" tak samo dobrze jak w „Revolut", a rozstrzygałaby długość
                // nazwy — czyli nic.
                var extra = tokens.Length - TokenOverlap(tokens, hintTokens);
                return new { Account = a, Score = score, Extra = extra };
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Extra)
            .FirstOrDefault();
        if (best is not null) return best.Account;

        // Zapasowo dawne zawieranie w obie strony — tańsze i wystarcza, gdy nazwa
        // konta jest wprost fragmentem podpowiedzi albo odwrotnie.
        return pool
            .Where(a => accountHint.Contains(a.Name, StringComparison.OrdinalIgnoreCase)
                     || a.Name.Contains(accountHint, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.Length)
            .FirstOrDefault();
    }

    private static bool AcronymMatches(string[] accountTokens, string hint)
    {
        var words = NonLetters().Split(Fold(hint)).Where(w => w.Length > 0).ToArray();
        if (words.Length < 2) return false;

        var acronym = new string([.. words.Select(w => w[0])]);
        return accountTokens.Any(t => t.Length >= 2 && t == acronym);
    }

    private async Task TryProposeTransferAsync(
        Transaction tx,
        AccountId ownAccountId,
        DateTimeOffset postedAt,
        CancellationToken ct)
    {
        var since = postedAt.AddHours(-48);
        var recent = await transactions.ListRecentAsync(since, ct);

        var match = recent.FirstOrDefault(t =>
            t.AccountId != ownAccountId &&
            t.Amount.Grosze == -tx.Amount.Grosze &&
            t.Kind != TransactionKind.Transfer &&
            t.ProposedTransferGroupId is null &&
            !t.SupersededById.HasValue &&
            t.Id != tx.Id);

        if (match is null) return;

        var groupId = Guid.NewGuid();
        tx.ProposeTransfer(groupId);
        match.ProposeTransfer(groupId);
    }

    /// <summary>
    /// Podpowiedź konta z treści powiadomienia decyduje PRZED nazwą banku z pakietu.
    /// Wcześniej było odwrotnie: znany pakiet („to Revolut") kończył szukanie na
    /// pierwszym koncie zawierającym „Revolut" w nazwie, a podpowiedź w ogóle nie
    /// była oglądana. Przy dwóch kontach w tym samym banku znaczyło to, że KAŻDE
    /// powiadomienie z tego banku lądowało na tym samym koncie — alfabetycznie
    /// pierwszym — niezależnie od tego, z którego konta poszła płatność.
    /// </summary>
    /// <summary>
    /// Podpowiedź konta z treści powiadomienia decyduje PRZED nazwą banku z pakietu.
    /// Wcześniej było odwrotnie: znany pakiet („to Revolut") kończył szukanie na
    /// pierwszym koncie zawierającym „Revolut" w nazwie, a podpowiedź w ogóle nie
    /// była oglądana. Przy dwóch kontach w tym samym banku znaczyło to, że KAŻDE
    /// powiadomienie z tego banku lądowało na tym samym koncie — alfabetycznie
    /// pierwszym — niezależnie od tego, z którego konta poszła płatność.
    /// </summary>
    private async Task<Account?> FindAccountAsync(string packageName, string? accountHint, CancellationToken ct)
    {
        var all = await accounts.ListAsync(ct);
        var active = all.Where(a => !a.IsArchived).ToList();
        if (active.Count == 0) return null;

        var bank = BankOf(packageName, accountHint);
        var pool = NarrowToBank(active, bank);

        // Nazwa banku jest tu pomijana jako słowo rozstrzygające: skoro pole zawężono już
        // do kont tego banku, „Revolut" w podpowiedzi nie odróżnia niczego. Bez tego
        // podpowiedź „karta Revolut Wspólny" pasowała do konta nazwanego po prostu
        // „Revolut" tak samo dobrze jak do „Wspólne" — i wygrywała kolejność alfabetyczna.
        var byHint = BestByHint(pool, accountHint, ignoreWord: bank);
        if (byHint is not null) return byHint;

        // Bez podpowiedzi zostaje pole zawężone przez pakiet. Gdy jest w nim więcej
        // niż jedno konto, wybór jest zgadywaniem — bierzemy rozliczeniowe, bo płatność
        // kartą idzie zwykle z niego.
        //
        // Konto gotówkowe jest z tego zgadywania WYŁĄCZONE: żaden bank nie powiadamia
        // o gotówce, więc trafienie tam byłoby zawsze błędem. Gdy nie zostaje nic innego,
        // lepszy brak transakcji niż transakcja w cudzym miejscu — powiadomienie zostaje
        // nierozpoznane i widać je na liście w Skrzynce.
        return pool.FirstOrDefault(a => a.Kind == AccountKind.Checking)
            ?? pool.FirstOrDefault(a => a.Kind != AccountKind.Cash);
    }

    /// <summary>
    /// Słowa znaczące z nazwy konta: bez ogonków, bez wielkości liter i bez wyrazów,
    /// które nie odróżniają jednego konta od drugiego („konto", „karta").
    /// </summary>
    private static string[] Tokenize(string text)
    {
        var folded = Fold(text);
        var all = NonLetters().Split(folded).Where(t => t.Length >= 2).ToArray();
        var meaningful = all.Where(t => !NoiseWords.Contains(t)).ToArray();

        // Konto nazwane samym szumem („Konto", „Rachunek") straciłoby wszystkie słowa
        // i nie dałoby się dopasować do niczego. Wtedy lepszy szum niż nic.
        return meaningful.Length > 0 ? meaningful : all;
    }

    // „osobiste" i „wspolne" NIE są tu szumem, choć kuszą: użytkownik nazywa konta
    // dokładnie tak („wspólne", „osobiste"), a wyrzucenie tych słów zostawiałoby
    // nazwę bez ani jednego słowa do dopasowania.
    /// <summary>Bez ogonków i bez wielkości liter — wspólna postać do porównań.</summary>
    private static string Fold(string text) =>
        new string(text.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant().Replace('ł', 'l');

    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal)
    {
        "konto", "konta", "rachunek", "karta", "karty", "moje", "moj", "account", "card",
        "pln", "eur", "usd", "gbp", "chf", "bank",
    };

    /// <summary>
    /// Ile słów mają wspólnych. Wspólny przedrostek długości 5 wystarcza, żeby zrównać
    /// polskie końcówki: „wspolne" i „wspolny" to to samo konto, „oszczednosciowe"
    /// i „oszczednosciowy" też.
    /// </summary>
    private static int TokenOverlap(string[] a, string[] b) =>
        a.Count(x => b.Any(y => x == y
            || (x.Length >= 5 && y.Length >= 5
                && (x.StartsWith(y[..5], StringComparison.Ordinal)
                 || y.StartsWith(x[..5], StringComparison.Ordinal)))));

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonLetters();

    private static string MaskSensitiveData(string text) =>
        SensitivePattern().Replace(text, "****");

    [GeneratedRegex(@"(?<![,.\d])\b\d{4,8}\b(?![,.\d])")]
    private static partial Regex SensitivePattern();
}
