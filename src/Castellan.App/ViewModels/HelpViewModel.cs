using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Castellan.App.ViewModels;

public sealed record HelpEntry(string Title, string Body);

/// <summary>
/// Rozdział przewodnika. Domyślnie zwinięty — cała treść naraz to kilkanaście ekranów
/// przewijania, w których nie da się niczego znaleźć; zwinięte tytuły działają jak spis
/// treści, po którym widać zakres aplikacji bez czytania.
/// </summary>
public sealed partial class HelpSection(string title, string lead, IReadOnlyList<HelpEntry> entries)
    : ObservableObject
{
    public string Title { get; } = title;
    public string Lead { get; } = lead;
    public IReadOnlyList<HelpEntry> Entries { get; } = entries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleGlyph))]
    private bool _isExpanded;

    public string ToggleGlyph => IsExpanded ? "−" : "+";

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;
}

public partial class HelpViewModel : ObservableObject
{
    public ObservableCollection<HelpSection> Sections { get; } = [];

    public HelpViewModel()
    {
        foreach (var section in Build())
            Sections.Add(section);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var s in Sections)
            s.IsExpanded = false;
    }

    private static IEnumerable<HelpSection> Build() =>
    [
        new("Na czym to polega",
            "Castellan planuje miesiąc do przodu i pilnuje, ile z tego planu zostało. To nie jest historia wydatków — historia jest tylko materiałem dowodowym.",
            [
                new("Koperty zamiast rachunku sumienia",
                    "Na początku miesiąca dzielisz dostępne pieniądze na koperty (kategorie). Przez miesiąc patrzysz nie na to, ile wydałaś, tylko ile jeszcze możesz. Liczba na Głównej — „Pozostało do wydania” — to suma tego, co zostało we wszystkich kopertach."),
                new("Skąd biorą się transakcje",
                    "Z trzech źródeł. Automatycznie z powiadomień bankowych. Ręcznie, gdy płacisz gotówką albo bank nie przysłał powiadomienia. I z uzgodnienia salda, gdy coś umknęło — wtedy różnica ląduje jako „Nierozpoznane”."),
                new("Co nie liczy się do budżetu",
                    "Trzy rzeczy są wykluczone z kopert: przelewy między Twoimi kontami (przenoszenie własnych pieniędzy to nie wydatek), wydatki pokryte z funduszu (odłożyłaś na nie wcześniej) oraz transakcje zastąpione przez późniejsze obciążenie. W liście transakcji poznasz je po wyszarzonej kwocie."),
                new("Pieniądze są w trzech miejscach",
                    "Koperty to przepływ w tym miesiącu. Fundusze to pieniądze odłożone na konkretny przyszły wydatek. Majątek to stan — ile masz i ile jesteś winna. Te trzy widoki celowo się nie mieszają."),
            ]),

        new("Pierwsze uruchomienie",
            "Pięć kroków w tej kolejności. Każdy następny opiera się na poprzednim.",
            [
                new("1. Dodaj konta",
                    "Zakładka Konta → „+”. Podaj nazwę, typ i saldo, które widzisz dziś w banku. Od tego salda liczą się wszystkie kolejne transakcje, więc przepisz je dokładnie.\n\nWażne: jeśli chcesz, żeby powiadomienia trafiały na właściwe konto, w nazwie umieść „ING” albo „Revolut”. Aplikacja dopasowuje konto po nazwie."),
                new("2. Wskaż konto domyślne",
                    "W wierszu konta tapnij „Ustaw domyślne”. To konto podpowiada się przy szybkim dodawaniu wydatku."),
                new("3. Wybierz, skąd biorą się transakcje",
                    "Zakładka Skrzynka, karta na górze. Dwie możliwości:\n\n• „Z powiadomień bankowych” — aplikacja czyta powiadomienia i sama zakłada transakcje. Jeśli brakuje uprawnienia, pojawi się pasek z przyciskiem „Ustawienia”, prowadzący do systemowej listy dostępu do powiadomień.\n\n• „Wpisuję wszystko ręcznie” — dla kogoś, kto nie ma powiadomień bankowych albo nie chce ich włączać. Aplikacja przestaje się o nie upominać, a wydatki dodajesz sama.\n\nTryb możesz zmienić kiedykolwiek."),
                new("4. Ustaw dzień wypłaty",
                    "Zakładka Fundusze, pasek na górze. Podaj dzień miesiąca, w którym dostajesz wypłatę. Bez tego raty funduszy liczą się kalendarzowo, a nie na wypłaty."),
                new("5. Zaplanuj pierwszy miesiąc",
                    "Zakładka Koperty → „Planuj”. Dopóki miesiąc nie ma planu, Główna nie ma czego pokazać."),
            ]),

        new("Codzienny obieg",
            "W trybie powiadomień w normalnym dniu nie robisz nic — wystarczy raz na jakiś czas uporządkować Skrzynkę. W trybie ręcznym wpisujesz wydatki na bieżąco.",
            [
                new("Tryb ręczny",
                    "Powiadomienia są wtedy ignorowane, nawet jeśli uprawnienie zostało kiedyś przyznane — inaczej ten sam wydatek trafiłby do aplikacji dwa razy: raz z banku, raz z Twojego wpisu.\n\nZnikają też oba przypomnienia o powiadomieniach: pasek w Skrzynce i ostrzeżenie na Głównej o ciszy trwającej ponad dobę. Cisza jest w tym trybie normalna, a baner, który stoi zawsze, uczy ignorować banery.\n\nReszta aplikacji działa bez zmian — koperty, fundusze, zobowiązania i uzgadnianie salda nie zależą od powiadomień."),
                new("Powiadomienie staje się transakcją",
                    "Aplikacja czyta powiadomienia z trzech źródeł: ING, Revolut i Portfela Google (ten ostatni bywa jedynym śladem płatności telefonem zbliżeniowo). Powiadomienia z innych aplikacji są ignorowane."),
                new("Kategoria sama albo do Skrzynki",
                    "Jeśli nazwa sprzedawcy pasuje do którejś reguły, transakcja od razu dostaje kategorię i nie wymaga niczego. Jeśli nie pasuje — ląduje jako „Nieprzypisane” i czeka w Skrzynce."),
                new("Porządkowanie Skrzynki",
                    "Zakładka Skrzynka → dotknij wiersza → wybierz kategorię → Zapisz. Przełącznik „Zapamiętaj regułę” sprawia, że kolejne transakcje od tego samego sprzedawcy dostaną tę kategorię same.\n\nJeśli wybierzesz „Rezerwy” albo „Kredyty i pożyczki”, aplikacja dodatkowo zapyta, którego funduszu lub kredytu to dotyczy."),
                new("Szybki wydatek",
                    "Gotówka albo płatność, której bank nie zgłosił. Zakładka Transakcje → ikona „⚡”, albo tapnięcie w widget na ekranie głównym telefonu. Wpisujesz kwotę, wybierasz kategorię z kafelków, „Gotowe”."),
                new("Pełne dodanie ręczne",
                    "Zakładka Transakcje → „+”. Tu ustawisz konto, datę, wydatek albo przychód i notatkę."),
                new("Poprawianie i usuwanie",
                    "Dotknięcie wiersza otwiera edycję. Przeciągnięcie wiersza w lewo odsłania „Z funduszu” i „Usuń”."),
                new("Jak liczy się data",
                    "Wpis z datą „dziś” dostaje aktualną godzinę, wpis wsteczny — koniec wybranego dnia. Dzięki temu transakcja nie wypadnie przed ostatnim uzgodnieniem salda i nie zniknie z konta."),
            ]),

        new("Miesiąc i koperty",
            "Plan miesiąca to jedno okno, w którym ustalasz przychody, pulę do podziału i koperty.",
            [
                new("Planowanie",
                    "Zakładka Koperty → „Planuj”. Ekran ma trzy części: spodziewane przychody z każdego źródła, środki do dyspozycji, i podział na koperty."),
                new("Podpowiedzi",
                    "Szare przyciski z kwotą przepisują liczbę jednym tapnięciem. Każdy pokazuje się tylko wtedy, gdy ma co podpowiedzieć; gdy nie ma żadnego, w ich miejscu pojawia się wyjaśnienie dlaczego.\n\n• „Suma planowanych przychodów” — to, co wpisałaś wyżej jako spodziewane wpływy tego miesiąca.\n\n• „Wpływy w tym miesiącu” — ile faktycznie wpłynęło w planowanym miesiącu.\n\n• „Wpływy z poprzedniego miesiąca” — dla wypłaty pod koniec miesiąca, gdy to ona finansuje kolejny.\n\n• „Na kontach rozliczeniowych” — ile realnie masz teraz. Jako jedyna jest pomiarem, a nie sumą z okresu, więc uwzględnia już wydatki zrobione po wypłacie. Konta oszczędnościowe są pominięte, bo zwykle leżą tam rezerwy.\n\n• „Fundusze: odkładaj …” — wstawia sumę rat funduszy do koperty „Rezerwy”."),
                new("Wypłata pod koniec miesiąca",
                    "Gdy wypłata przychodzi 25.–28., naturalnie finansuje kolejny miesiąc kalendarzowy. Nie trzeba nic przestawiać: po wypłacie przełącz strzałką na następny miesiąc, wejdź w „Planuj” i użyj podpowiedzi „Wpływy z poprzedniego miesiąca”.\n\nSekcję przychodów planuj dalej na miesiąc, w którym pieniądze faktycznie wpływają — te dwie sekcje mówią wtedy o różnych pieniądzach.\n\nUwaga na miesiąc przejściowy: podpowiedź z wpływami pokazuje całą wypłatę i nie odejmuje tego, co wydałaś między wypłatą a końcem miesiąca. Dopóki nie masz odłożonego tygodnia zapasu, wpisz kwotę ręcznie albo użyj podpowiedzi „Na kontach rozliczeniowych”, która liczy stan faktyczny."),
                new("Do przydzielenia",
                    "Środki do dyspozycji minus suma kopert. Zero znaczy, że każda złotówka ma przypisane zadanie. Wartość ujemna świeci na koralowo — rozdysponowałaś więcej, niż masz."),
                new("Pozostało do wydania",
                    "Suma kopert minus to, co już wydane. To główna liczba na Głównej."),
                new("Kolory pasków",
                    "Zielony do 75% koperty, mosiądz od 75%, koral po przekroczeniu. Ta sama skala obowiązuje na Głównej i w Kopertach."),
                new("Zmiana planu w trakcie",
                    "Możesz planować ponownie kiedykolwiek — zapis zastępuje poprzedni plan tego miesiąca. Transakcje zostają nietknięte."),
                new("Poprzednie i przyszłe miesiące",
                    "Strzałki „‹” i „›” u góry Głównej, Kopert i Transakcji przesuwają miesiąc. Miesiąc bez planu pokazuje pustkę — to nie błąd, po prostu nie było planu."),
            ]),

        new("Przychody",
            "Osobne zestawienie planowanych i faktycznych wpływów. Wejście: Główna → trzy kropki u góry → Przychody.",
            [
                new("Co się liczy jako wpływ",
                    "Każda dodatnia transakcja w kategorii przychodowej. Przelewy między Twoimi kontami są wykluczone, więc przerzucenie pieniędzy z wypłaty na wspólne konto nie zawyży przychodów."),
                new("Wszystkie źródła naraz",
                    "Lista pokazuje też źródła, których jeszcze nie zaplanowałaś ani z których nic nie wpłynęło — żeby od razu było widać, czego brakuje w planie."),
                new("Różnica",
                    "Pod sumami widnieje różnica między otrzymanym a zaplanowanym. Koral znaczy, że wpłynęło mniej, niż zakładałaś."),
            ]),

        new("Konta i uzgadnianie salda",
            "Uzgodnienie to sposób na dogonienie rzeczywistości, gdy coś umknęło.",
            [
                new("Jak to działa",
                    "Zakładka Konta → „Uzgodnij” → przepisz saldo, które pokazuje bank. Aplikacja porówna je z własnym wyliczeniem za okres od ostatniego uzgodnienia."),
                new("Gdy brakuje pieniędzy",
                    "Jeśli w banku jest mniej, niż wynika z zapisów, różnica zapisuje się jako transakcja „Nierozpoznane” — czyli wydatek, o którym aplikacja nie wiedziała. Saldo znów się zgadza."),
                new("Gdy pieniędzy jest więcej",
                    "Aplikacja pokaże różnicę, ale niczego nie utworzy. Nadwyżka znaczy albo niezapisany wpływ, albo policzony podwójnie wydatek — i tylko Ty wiesz który."),
                new("Przeszłość zostaje nietknięta",
                    "Uzgodnienie patrzy wyłącznie na okno od poprzedniego uzgodnienia do teraz. Starszych transakcji nie rusza."),
            ]),

        new("Przelewy między kontami",
            "Przeniesienie własnych pieniędzy nie jest ani wydatkiem, ani przychodem — nie rusza kopert ani przychodów.",
            [
                new("Ręcznie",
                    "Zakładka Transakcje → trzy kropki u góry → „Przelew między kontami”. Wybierasz konto źródłowe i docelowe, kwotę i datę."),
                new("Automatyczna propozycja",
                    "Gdy z jednego konta wychodzi kwota, a na drugie w ciągu 48 godzin wchodzi dokładnie taka sama, aplikacja proponuje uznać to za przelew. Propozycja czeka na górze Skrzynki."),
                new("Potwierdzenie",
                    "„Tak, to przelew” łączy obie transakcje i wyklucza je z budżetu. „Nie” zostawia je jako zwykły wydatek i wpływ."),
            ]),

        new("Fundusze",
            "Fundusz to pieniądze odkładane na konkretny wydatek z konkretnym terminem — ubezpieczenie, urlop, podatek.",
            [
                new("Zakładanie",
                    "Zakładka Fundusze → „+”. Nazwa, rodzaj, kwota docelowa i termin, na kiedy potrzebujesz pieniędzy."),
                new("Fundusz bez terminu",
                    "Przełącznik „Zbieram bez terminu” tworzy fundusz otwarty: jest cel i pasek postępu, ale nie ma daty. Nie policzymy wtedy raty ani nie pokażemy opóźnienia — bo nie ma tempa, względem którego można być spóźnionym. Zamiast raty zobaczysz, ile brakuje do celu.\n\nWybranie rodzaju „Poduszka bezpieczeństwa” włącza ten tryb samo, bo to jej naturalna postać. Możesz go wyłączyć i podać termin, jeśli wolisz zbierać na czas."),
                new("Które fundusze liczą się do Majątku",
                    "Przy każdym funduszu na liście jest przełącznik „Licz do poduszki finansowej”. Włączony znaczy, że saldo wchodzi do „ile miesięcy wytrzymam” w Majątku; fundusz przenosi się wtedy do płynności natychmiastowej i znika z sekcji Fundusze, żeby nie był liczony dwa razy.\n\nPoduszka bezpieczeństwa dostaje ten znacznik przy zakładaniu, reszta nie. Pieniądze w funduszu na OC są już wydane, tylko jeszcze nie zapłacone — OC przyjdzie niezależnie od tego, czy stracisz dochód, więc doliczenie ich mówiłoby, że wytrzymasz dłużej, niż wytrzymasz.\n\nZnacznik przestawisz w obie strony. Jeśli „Wakacje” to u Ciebie po prostu oszczędzanie, włącz go — o tym, co realnie jest rezerwą, wiesz tylko Ty.\n\nUwaga na podwójne liczenie: fundusz jest kopertą nad pieniędzmi, które gdzieś fizycznie leżą. Jeśli leżą na koncie rozliczeniowym, aplikacja już je policzyła i zobaczysz je dwa razy. Trzymaj je na koncie oszczędnościowym (te nie wchodzą do wyliczenia) albo nie dodawaj tego konta jako aktywa."),
                new("Skąd bierze się rata",
                    "Brakująca kwota dzielona przez liczbę wypłat pozostałych do terminu. Dlatego dzień wypłaty ma znaczenie — bez niego liczy się kalendarzowo."),
                new("Wpłata zamyka bieżący miesiąc",
                    "Gdy wpłacisz w danym miesiącu, ten okres jest odhaczony i rata przelicza się dopiero od następnego. Bez tego rata malałaby zaraz po wpłacie, tak jakby trzeba było dołożyć jeszcze raz."),
                new("Na bieżąco czy z opóźnieniem",
                    "Zielone „✓ Na bieżąco” znaczy, że zebrałaś tyle, ile powinno być na dziś. Koralowe „⚠ Brakuje” pokazuje, ile trzeba nadrobić."),
                new("Jak wpłacać",
                    "Dwie drogi. Przycisk „Wpłać” przy funduszu. Albo zwyczajnie — dodając wydatek w kategorii „Rezerwy”; wtedy aplikacja zapyta, do którego funduszu trafiła kwota, i sama podniesie saldo."),
                new("Suma rat idzie do koperty „Rezerwy”",
                    "Karta na górze zakładki Fundusze pokazuje, ile łącznie odkładasz miesięcznie. Tę kwotę wpisujesz w kopertę „Rezerwy” przy planowaniu miesiąca."),
                new("Wydatek pokryty z funduszu",
                    "Gdy przyjdzie czas zapłacić za to, na co odkładałaś: w liście transakcji przeciągnij wiersz w lewo → „Z funduszu” → wybierz fundusz. Kwota zejdzie z salda funduszu, a wydatek wypadnie z kopert — bo budżet obciążyły już wcześniejsze odpisy."),
                new("Usunięcie funduszu",
                    "Aplikacja ostrzeże, co się stanie: zebrane saldo zniknie z Majątku, a wydatki pokryte z tego funduszu wrócą do kopert i znów obciążą budżet. Same transakcje zostają."),
            ]),

        new("Zobowiązania i plan spłaty",
            "Zobowiązanie to lustro funduszu — saldo maleje do zera zamiast rosnąć do celu.",
            [
                new("Dodawanie",
                    "Zakładka Majątek → trzy kropki u góry → „+ Zobowiązanie”. Podaj kwotę, którą widzisz dziś w banku, oraz miesięczną ratę. Ratę możesz zostawić pustą, jeśli nie ma harmonogramu — wtedy nie policzymy terminu spłaty."),
                new("Płacenie raty",
                    "Przycisk „Zapłać ratę” przy zobowiązaniu robi dwie rzeczy naraz: zapisuje wydatek w wybranej kopercie i zmniejsza saldo długu. Możesz wpisać więcej niż ratę — nadpłata skróci drogę."),
                new("Rata złapana z powiadomienia",
                    "Jeśli bank sam zgłosił ratę, przypisz jej kategorię „Kredyty i pożyczki”. Aplikacja zapyta, którego kredytu dotyczy, i zmniejszy saldo — bez tworzenia drugiej transakcji, bo pierwsza już istnieje."),
                new("Pasek na Głównej",
                    "Gdy masz zobowiązania, na Głównej pojawia się koralowy pasek z sumą długu i datą wyjścia na zero. Tapnięcie prowadzi do planu spłaty. Jest tam celowo — żeby spojrzenie na dług nie wymagało świadomej decyzji."),
                new("Plan spłaty",
                    "Pokazuje, ile zostało i kiedy się skończy przy obecnych ratach. W polu „Spłacam miesięcznie” możesz podnieść kwotę i zobaczyć, o ile skróci się droga."),
                new("Kula śnieżna",
                    "Lista „Kolejność znikania” liczy realną kaskadę: nadwyżka ponad minimalne raty idzie w najmniejsze saldo, a gdy ten dług znika, jego rata dołącza do puli i przyspiesza następny."),
                new("Bez odsetek",
                    "Symulacja nie zna oprocentowania. Przy kredytach z odsetkami rzeczywisty czas będzie nieco dłuższy. Ekran mówi to wprost, zamiast dawać zbyt optymistyczną liczbę."),
                new("Korekta salda",
                    "Odsetki potrafią rozjechać saldo względem sumy zapłaconych rat. Przeciągnij zobowiązanie w lewo → „Edytuj” → popraw aktualne saldo na to z banku."),
            ]),

        new("Majątek",
            "Stan posiadania: ile masz, jak szybko możesz to zamienić na gotówkę i ile jesteś winna.",
            [
                new("Poduszka w miesiącach",
                    "Największa liczba na ekranie: ile miesięcy wytrzymasz bez przychodu. Liczona z Twoich średnich wydatków z ostatnich miesięcy, nie z abstrakcyjnej normy."),
                new("Cztery poziomy płynności",
                    "Natychmiastowa, szybka (1–3 dni), średnia (tygodnie), wolna (miesiące). Przy dodawaniu aktywa wybierasz, jak szybko dasz radę je spieniężyć. Salda kont rozliczeniowych wliczają się automatycznie do poziomu natychmiastowego."),
                new("Aktualizacja wartości",
                    "Dotknij aktywa i wpisz nową wartość. Pod kwotą widnieje data ostatniej aktualizacji — żeby było widać, kiedy liczba się zestarzała."),
                new("Fundusze osobno",
                    "Fundusze mają własną sekcję, poza poziomami płynności. Te pieniądze mają już właściciela — konkretny przyszły wydatek — więc nie są rezerwą na czarną godzinę."),
                new("Wartość netto",
                    "Aktywa i fundusze minus zobowiązania. Karta pojawia się tylko wtedy, gdy masz długi; bez nich powtarzałaby po prostu sumę aktywów."),
            ]),

        new("Kategorie i reguły",
            "Reguły to pamięć aplikacji: raz nauczona, kategoryzuje sama.",
            [
                new("Zarządzanie kategoriami",
                    "Zakładka Transakcje → trzy kropki u góry → „Kategorie”. Przeciągnięcie w lewo archiwizuje. Archiwizacja zachowuje historię — kategoria znika z list wyboru, ale stare transakcje jej nie tracą."),
                new("Jak działa reguła",
                    "Reguła to fragment nazwy sprzedawcy. Pasuje, gdy nazwa go zawiera; wielkość liter nie ma znaczenia."),
                new("Gdy pasuje kilka",
                    "Wygrywa reguła z dłuższym wzorcem, a przy równej długości ta, która trafiała częściej. Dzięki temu „ORLEN Bistro” może mieć inną kategorię niż samo „ORLEN”."),
                new("Skąd się biorą",
                    "Najprościej — przełącznikiem „Zapamiętaj regułę” przy kategoryzowaniu w Skrzynce. Ręcznie: Transakcje → trzy kropki → „Reguły” → „+”."),
            ]),

        new("Statystyki",
            "Sześć ostatnich miesięcy w trzech ujęciach. Wejście: Główna → trzy kropki u góry → Statystyki.",
            [
                new("Sumy",
                    "Wydatki, przychody i różnica netto za cały okres."),
                new("Słupki miesięczne",
                    "Bieżący miesiąc pełnym kolorem, poprzednie przygaszone — żeby przeszłość nie krzyczała głośniej niż teraźniejszość."),
                new("Największe kategorie",
                    "Na co realnie idą pieniądze, uszeregowane od największej pozycji."),
            ]),

        new("Kopia zapasowa",
            "Dane siedzą wyłącznie na telefonie. Nie ma chmury, nie ma konta, nie ma serwera — więc kopia to jedyne zabezpieczenie.",
            [
                new("Eksport",
                    "Zakładka Kopia → „Eksportuj”. Powstaje plik JSON ze wszystkim: kontami, transakcjami, kopertami, przychodami, funduszami, aktywami i zobowiązaniami. Android zapyta, gdzie go wysłać — Dysk, mail, cokolwiek.\n\nNazwa pliku ma postać castellan_RRRRMMDD_GGmmss.json."),
                new("Import",
                    "Wczytuje plik i zastępuje nim wszystko, co jest teraz w aplikacji. Tej operacji nie da się cofnąć — dlatego karta importu ma koralową obwódkę. Rób to po reinstalacji albo na nowym telefonie."),
                new("Starsze kopie",
                    "Plik sprzed dodania jakiegoś modułu wczyta się bez problemu — brakujące sekcje po prostu będą puste."),
            ]),

        new("Gdy coś wygląda dziwnie",
            "Sytuacje, które najczęściej wyglądają na błąd, a nim nie są.",
            [
                new("Wydatek zniknął z koperty",
                    "Prawdopodobnie został pokryty z funduszu albo oznaczony jako przelew — jedno i drugie wypada z budżetu miesiąca. W liście transakcji taka kwota jest wyszarzona. Pokrycie z funduszu cofniesz, przypisując wydatek z powrotem."),
                new("Ta sama płatność dwa razy",
                    "Aplikacja odrzuca duplikaty na dwa sposoby: po nazwie sprzedawcy i kwocie (do 25 godzin wstecz, tolerancja 2%), a gdy nazwy się nie zgadzają — po identycznej kwocie co do grosza na tym samym koncie w ciągu 15 minut. Ten drugi przypadek jest dla par Portfel Google plus bank, które podają zupełnie inne nazwy sprzedawcy."),
                new("Sprawdzenie kwot z powiadomień",
                    "Transakcje → trzy kropki → „Sprawdź kwoty z powiadomień”. Aplikacja przechowuje treść każdego przechwyconego powiadomienia, więc po poprawce wzorca potrafi odczytać je ponownie i porównać wynik z kwotą zapisanej transakcji.\n\nLista pokazuje rozbieżności, największe różnice na górze. Przy każdej jest przycisk „Popraw na …”, który ustawia kwotę z powiadomienia — pojedynczo, nigdy hurtem; poprawiony wiersz znika z listy. Dotknięcie wiersza otwiera pełną edycję, gdy chcesz zobaczyć kontekst albo wpisać inną kwotę. Wierszy powiązanych z funduszem lub przelewem nie da się poprawić stąd — tam kwota ma skutki poza samą transakcją. Transakcje poprawione ręcznie się na niej nie pojawią: ich kwota zgadza się już z powiadomieniem, więc nie ma czego zgłaszać.\n\nWiersz na liście nie musi znaczyć błędu. Jeśli zmieniłaś kwotę świadomie — bo płatność została rozbita albo częściowo zwrócona — różnica względem powiadomienia jest prawidłowa. Poprawiaj tylko to, co faktycznie się nie zgadza."),
                new("Kwota zmieniła się sama",
                    "Przy płatnościach kartą bank najpierw blokuje kwotę, a potem księguje właściwą. Gdy przyjdzie właściwe obciążenie, zastępuje blokadę — stara pozycja zostaje w historii jako zastąpiona i nie liczy się do budżetu."),
                new("Powiadomienie nie utworzyło transakcji",
                    "Cztery najczęstsze powody: włączony jest tryb ręczny, aplikacja bankowa nie jest na liście obsługiwanych, brak uprawnienia do powiadomień, albo treść powiadomienia nie dała się rozczytać. Wtedy dodaj wydatek ręcznie i uzgodnij saldo konta."),
                new("Transakcja trafiła na złe konto",
                    "Konto rozpoznaje się po nazwie — powiadomienie z ING szuka konta ze słowem „ING” w nazwie. Jeśli nic nie pasuje, trafia na pierwsze konto rozliczeniowe. Zmień nazwę konta albo popraw transakcję ręcznie."),
                new("Cyfry zamienione na gwiazdki",
                    "Aplikacja maskuje w treści powiadomień ciągi 4–8 cyfr, zanim cokolwiek zapisze — to numery kart i rachunków. Kwoty nie są maskowane."),
                new("Główna jest pusta",
                    "Miesiąc nie ma planu. Zakładka Koperty → „Planuj”."),
            ]),
    ];
}
