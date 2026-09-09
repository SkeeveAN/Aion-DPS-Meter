🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 **Polski** · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Miernik obrażeń i łupów dla **AION 4.6 (OriginAion)**, działający wyłącznie na podstawie
własnego pliku `Chat.log` gry.

Program czyta plik tekstowy, który klient zapisuje sam z siebie. Nie przechwytuje ruchu
sieciowego i nie czyta ani nie zapisuje niczego w procesie gry. Nic z Twojej rozgrywki nie
opuszcza Twojego komputera — żadnych liczb obrażeń, żadnych łupów, żadnych nazw. Jedyną rzeczą,
którą wysyła, jest sprawdzenie aktualizacji, które pyta GitHub, czy istnieje nowsza wersja, i
które można wyłączyć; zobacz [Aktualizacje](#aktualizacje).

## Instalacja

1. Pobierz `AionDpsMeter-win-Setup.exe` z [najnowszego wydania](../../releases/latest) i uruchom
   go. Nie ma nic do klikania — instaluje się w profilu użytkownika i uruchamia miernik. Bez
   uprawnień administratora, bez potrzeby .NET.
2. Otwórz **Settings → App Settings** i wybierz **folder instalacji Aion** — folder główny,
   zawierający `bin64\game.dll`. Okno od razu poinformuje, czy znalazło prawidłową instalację i
   czy istnieje tam już plik `Chat.log`.

> **Aktualizujesz z wersji 0.5.2 lub starszej?** Najpierw odinstaluj starą wersję (Ustawienia
> Windows → Aplikacje → *Aion DPS Meter*), potem uruchom nowy instalator. Te wersje instalowały
> się w `Program Files`, przez co nigdy nie mogły się same aktualizować. To jednorazowy krok — od
> teraz aktualizacje stosują się same.

### Wymóg: rejestrowanie czatu klienta musi być włączone

Aion zapisuje `Chat.log` tylko wtedy, gdy wewnętrzna opcja klienta `g_chatlog` jest włączona.
Ten przełącznik znajduje się w kliencie gry, nie w tym narzędziu — włącz go tak, jak zwykle
(na przykład za pomocą [ShugoConsole](https://github.com/grenadium/ShugoConsole)). **Aion DPS
Meter nigdy nie dotyka procesu gry w tym celu**; jeśli plik nie jest zapisywany, miernik nie ma
czego czytać.

## Obsługa

Nagrywanie zaczyna się od razu, gdy miernik jest uruchomiony i ustawiony jest prawidłowy folder
Aion.

**Nigdy nie czyta Twojej historii czatu.** Po starcie przeskakuje na *aktualny* koniec pliku
`Chat.log` i przetwarza tylko linie zapisane od tego momentu — jak magnetofon właśnie włączony,
a nie skaner archiwum. **Pauza odrzuca**, a nie odkłada: linie zapisane podczas pauzy są pomijane
na stałe, więc wznowienie nigdy nie odtwarza walki, w której świadomie nie uczestniczyłeś. Twoje
dawne prywatne rozmowy, czat legionu i szepty nigdy nie są przeglądane.

### Widoki

- **Dmg** — obrażenia na gracza, z sumą i DPS, ikonami klas i sortowalną listą. Filtr
  **Mob/Boss** przełącza kolumnę między ogólnym DPS a prawdziwym, celowym **iDPS** (obrażenia
  zadane jednemu celowi podzielone przez wspólny czas walki grupy z tym celem).
- **Loot** — co komu wypadło: osoba, przedmiot, ilość i stopień rzadkości. Relikwie liczą się
  też do Punktów Otchłani danej osoby.

### Hide UI (nakładka)

Zamienia okno w małe, przezroczyste dla kliknięć „chipy”, które można zostawić na wierzchu gry —
jeden na gracza, pokazujący imię, obrażenia i DPS. Przełączane **Ctrl+Alt+H**, z dowolnego
miejsca, więc nigdy nie jest to droga bez powrotu.

### Copy

**Copy** umieszcza w schowku jednowierszowy, gotowy do czatu ranking
(`Imię 1.234.567 (890), …`). W widoku Loot generuje zamiast tego podsumowanie łupów w formacie
czatu Aion, a **Copy All** daje tabelę w formacie Markdown dla Discorda.

### Polecenia w grze

Wpisz je jako zwykłe linie czatu, aby sterować miernikiem bez opuszczania gry:

| Polecenie | Działanie |
|---|---|
| `.ui` | przełącz nakładkę Hide-UI |
| `.pause` / `.resume` | zatrzymaj / wznów nagrywanie |
| `.dmg` | skopiuj ranking obrażeń do schowka |
| `.cleardmg` | wyczyść bieżącą sesję |
| `.loot` | skopiuj podsumowanie łupów do schowka |

Wywołać je mogą tylko postacie zarejestrowane w ustawieniach, więc `.cleardmg` wpisane przez
obcą osobę na kanale, którego nawet nie czytasz, nie może wyczyścić Twojej sesji.

## Aktualizacje

Miernik aktualizuje się sam. Przy starcie, a potem co pięć minut, pyta GitHub o nowszą wersję,
pobiera ją w tle i podmienia przy następnym uruchomieniu. Bez instalatora, bez okna UAC, nic do
klikania. Działa to, bo program mieszka w profilu użytkownika, a nie w `Program Files` — tam
wolno mu podmieniać własne pliki.

Gdy aktualizacja jest już pobrana, na dole w wierszu statusu pojawia się zielona linia; kliknięcie
oferuje natychmiastowy restart. Odrzucenie nic nie kosztuje — wersja już tam jest i włączy się
przy następnym normalnym starcie. Celowo brak wyskakującego okna: okno programu leży nad działającą
grą, a dialog kradnący fokus w środku walki z bossem byłby gorszy niż spóźniona aktualizacja.

**App → Check for updates** robi to samo na żądanie i informuje też, gdy masz już najnowszą wersję.

Sprawdzenie odczytuje dokładnie jeden adres URL i nie wysyła nic poza samym zapytaniem:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Wyłączane w **Settings → App Settings → Updates**. Pozycja menu nadal działa — to Twoje własne
zapytanie, nie decyzja programu.

## Jak dokładny jest ten miernik?

Zwalidowany względem trzech plików `Chat.log` z tego samego przebiegu Sauro Supply Base,
zapisanych na trzech różnych komputerach (jeden z nich to klient niemiecki), z prawdziwymi
punktami życia bossów jako punktem odniesienia. Obrażenia na bossa mieszczą się w zakresie
**0,02% – 2,8%** od jego rzeczywistych HP:

| Boss | Prawdziwe HP | Zmierzone | Odchylenie |
|---|---|---|---|
| Dowódca straży Ahuradim | 1 736 993 | 1 737 299 | +0,02% |
| Mroczny pożeracz Derakanak | 1 343 657 | 1 347 258 | +0,27% |
| Oficer inspekcyjny Sayahum | 1 377 644 | 1 370 734 | −0,50% |
| Komendant zaopatrzenia Ranodim | 489 332 | 503 244 | +2,84% |

Reszta to przesada przy ciosie dobijającym, której żaden miernik oparty na logu nie zobaczy.
Całkowite obrażenia gracza zgadzały się co do jednostki na wszystkich trzech komputerach.

Dwa znane, nieszkodliwe odchylenia: przy bossie, którego tarcza pochłania obrażenia, te obrażenia
są mimo to logowane (przez co wychodzą powyżej jego HP), a kilka mobów o tej samej nazwie jest
liczonych razem.

## Budowanie ze źródeł

```
dotnet build
dotnet run -- selftest                    # testy wewnętrzne parsera i obliczeń DPS
dotnet run -- chatlog <ścieżka-do-Chat.log> # sparsuj plik i wypisz podsumowanie
```

Tylko Windows (WPF). Testy wewnętrzne przepuszczają dosłowne linie z prawdziwych logów we
wszystkich obsługiwanych językach i są najszybszym sposobem sprawdzenia, czy zmiana w parserze
czegoś nie zepsuła.

## Uwaga o zasadach serwera

To narzędzie czyta tylko plik dziennika, który sama gra tworzy. Mimo to prywatne serwery mają
własne zasady dotyczące oprogramowania i dodatków firm trzecich — warto zajrzeć do zasad
OriginAion przed użyciem.
