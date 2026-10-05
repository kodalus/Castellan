**Android (APK)** — instalacja z pominięciem sklepu.

Pakiet jest podpisany stałym kluczem projektu, więc **kolejne wersje instalują się na
wierzch** — bez odinstalowywania i bez utraty danych.

> **Jednorazowo, przy przejściu z wcześniejszych wydań:** pakiety sprzed wprowadzenia
> tego klucza miały inny podpis, więc Android odmówi aktualizacji komunikatem o konflikcie
> z istniejącym pakietem. Trzeba wtedy raz odinstalować starą wersję — a odinstalowanie
> usuwa bazę razem z aplikacją. **Najpierw zrób kopię w zakładce Kopia**, potem
> odinstaluj, zainstaluj i wczytaj kopię przez Kopia → Importuj.

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
