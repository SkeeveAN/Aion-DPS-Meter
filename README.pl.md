🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 **Polski** · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Strona z rankingami bossów społeczności i profilami postaci: **https://aiondps.com**

Licznik obrażeń/leczenia dla **Aion 2**. Czyta ruch sieciowy gry na twoim komputerze przez sterownik
[Npcap](https://npcap.com) — pasywnie: nigdy nie wysyła pakietów i nie dotyka ani procesu gry, ani jego pamięci.
Nic z twojej gry nie opuszcza komputera, dopóki sam tego nie prześlesz (zob. [Przesyłanie](#przesyłanie)); poza tym
wysyłane jest sprawdzanie aktualizacji — zapytanie do GitHuba o nowszą wersję, które można wyłączyć; zob.
[Aktualizacje](#aktualizacje).

> Klasyczny Aion (oparty na Chat.log) nie jest już częścią licznika. Ostatnia wersja, która go obsługuje, jest na
> gałęzi [`aion1-included`](../../tree/aion1-included).

## Instalacja

1. Zainstaluj sterownik [Npcap](https://npcap.com) (licznik potrzebuje go, by widzieć ruch gry; nie wchodzi w skład
   instalatora).
2. Pobierz `AionDpsMeter-win-Setup.exe` z [najnowszego wydania](../../releases/latest) i uruchom. Instaluje się w
   profilu użytkownika i uruchamia licznik — bez uprawnień administratora i bez .NET.
3. Uruchom licznik, a potem zaloguj się swoją postacią. Licznik odczytuje postać, ekwipunek, umiejętności i plansze
   Daevanion z samej gry; serwer jest wykrywany automatycznie.

## Użycie

Nagrywanie zaczyna się, gdy tylko licznik działa. **Pauza odrzuca**, a nie odkłada: zdarzenia w czasie pauzy giną na
zawsze, wznowienie nigdy nie odtwarza walki, którą ominąłeś.

### Widoki

- **Dmg** — obrażenia na gracza z sumą i DPS, ikonami klas i sortowaną listą. Filtr **Mob/Boss** przełącza kolumnę
  między ogólnym DPS a prawdziwym **iDPS** na cel. Bossowie są rozpoznawani z danych gry i pokazywani z nazwy.
  **Podwójne kliknięcie** gracza pokazuje podział na umiejętności.
- **Postać** (ikona osoby) — otwiera okno z twoją postacią: profil, ekwipunek z poziomami przedmiotów i
  ulepszeniami, umiejętności z poziomami i plansze Daevanion. Zapamiętuje ostatnie logowanie, więc nigdy nie jest puste.

### Hide UI (nakładka)

Zamienia okno w małe, przeklikiwalne znaczniki nad grą — po jednym na gracza z nazwą, obrażeniami i DPS. Przełączanie
**Ctrl+Alt+H**, z dowolnego miejsca.

### Kopiowanie

**Copy** wkłada do schowka jednowierszowy ranking gotowy do czatu (`Nazwa 1.234.567 (890), …`); **Copy All** daje
tabelę Markdown dla Discorda.

### Skróty klawiszowe

Działają wszędzie, także gdy gra ma fokus (zmiana w **Ustawienia → Interfejs → Skróty klawiszowe**): **Ctrl+Alt+H** nakładka, **Ctrl+Alt+P** pauza / wznów, **Ctrl+Alt+D** kopiuj ranking, **Ctrl+Alt+Shift+X** wyczyść sesję. Zastępują komendy czatu, które nie mogą działać, bo czatu Aion 2 nie da się odczytać z ruchu sieciowego.

## Przesyłanie

- **Walki z bossami** są przesyłane po kliknięciu upload (przycisk lub menu Session): boss, uczestnicy, obrażenia,
  leczenie, otrzymane obrażenia i umiejętności. Przyjmowani są tylko bossowie zapowiedziani przez grę i znani katalogowi.
- **Profil twojej postaci** (nazwa, klasa, poziom, ekwipunek, umiejętności, Daevanion, legion, serwer) jest
  przesyłany automatycznie kilka sekund po zalogowaniu, aby można cię było znaleźć na stronie. Wyłączysz to w
  **Ustawieniach**.
- Bez przesyłania nic nie opuszcza twojego komputera.

## Aktualizacje

Licznik aktualizuje się sam. Przy starcie i co pięć minut pyta GitHub o nowszą wersję, pobiera ją w tle i stosuje przy
następnym uruchomieniu — bez instalatora i UAC. Gdy aktualizacja jest gotowa, na dole pojawia się zielona linia;
kliknięcie proponuje natychmiastowy restart. **App → Check for updates** robi to samo na żądanie.

Sprawdzanie czyta jeden adres URL i nie wysyła niczego poza samym zapytaniem:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Wyłączysz to w **Ustawienia → Aktualizacje**; pozycja menu działa mimo to.

## Budowanie ze źródeł

```
cd Client
dotnet build
dotnet run -- selftest                              # autotesty (protokół, przechwytywanie, dekodowanie, ...)
dotnet run -- aion2-record <out.jsonl>              # nagraj ruch gry ("stop" kończy)
dotnet run -- aion2-replay <plik.jsonl>             # odtwórz nagranie prawdziwym dekoderem
dotnet run -- aion2-upload-dryrun <plik.jsonl>      # zbuduj przesyłki z nagrania, nic nie wysyłając
```

Tylko Windows (WPF). `Tools/aion2-dat` czyta tablice tekstów gry (nazwy w ośmiu językach); zob. jego README.

## Uwaga o regułach serwerów

Licznik jedynie pasywnie obserwuje ruch sieciowy gry. Mimo to wydawcy ustalają własne zasady dotyczące narzędzi firm
trzecich — przed użyciem warto zajrzeć do warunków gry.

## Zastrzeżenie i licencja

Aion 2 oraz wszystkie powiązane nazwy, teksty i grafiki są własnością NCSOFT. Projekt nie jest powiązany z NCSOFT ani przez nią wspierany czy zatwierdzony; zawartość gry pozostaje własnością jej właściciela. Kod źródłowy jest udostępniony na [licencji MIT](LICENSE) — licencja obejmuje wyłącznie kod, a nie zawartość gry.
