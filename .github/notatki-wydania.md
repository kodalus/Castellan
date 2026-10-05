**Android (APK)** — instalacja z pominięciem sklepu.

Pakiet jest podpisany kluczem diagnostycznym, a ten klucz powstaje od nowa przy każdym
budowaniu. Android odmówi więc nadpisania aplikacji zainstalowanej z innego pakietu —
trzeba odinstalować starą.

> **Przed odinstalowaniem zrób kopię zapasową w zakładce Kopia.** Odinstalowanie usuwa
> bazę razem z aplikacją i nie ma skąd jej odzyskać.

**Windows (ZIP)** — rozpakuj cały katalog i uruchom `Castellan.App.exe`.

Nie wymaga instalowania .NET ani Windows App SDK. Sam plik `.exe` nie zadziała
w oderwaniu od swojego katalogu — biblioteki leżą obok niego.

Na pulpicie **nie ma czytania powiadomień bankowych**: to funkcja Androida, nie
aplikacji. Wersja na Windows startuje więc w trybie ręcznym i wszystkie transakcje
wpisuje się samemu — formularz dodawania obsługuje się z klawiatury (Enter przechodzi
do następnego pola, w ostatnim zapisuje).

Dane każdej instalacji są osobne. Przeniesienie ich między telefonem a pulpitem idzie
przez Kopia → Eksportuj i Kopia → Importuj; **import zastępuje wszystko**, więc nie jest
synchronizacją.
