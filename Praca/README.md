# Praca dyplomowa w LaTeX-u

Podstawa to szablon Wydziału Informatyki i Telekomunikacji z 25.02.2026 (`szablonwitpd.zip`). Plik `settings.tex` jest bez zmian, a wszystkie dodatki są w `dodatki.tex`.

## Kompilacja

**Overleaf:** wgraj cały folder `Praca`, kompilator pdfLaTeX, główny plik `main.tex`. Overleaf sam uruchamia program biber.

**Lokalnie:** w folderze `Praca` po kolei:

```bash
pdflatex main
```

```bash
biber main
```

```bash
pdflatex main
```

```bash
pdflatex main
```

## Co uzupełnić w `main.tex`

Autor, opiekun, rok i słowa kluczowe. Kierunek jest wpisany. Tytuł angielski to tłumaczenie robocze. Oba tytuły muszą być identyczne z tytułami w Archiwum Prac Dyplomowych.

## Pliki

| Plik lub folder | Zawartość |
|---|---|
| `main.tex` | dane pracy, kolejność rozdziałów, przełącznik szkicu |
| `settings.tex` | plik Wydziału, bez zmian |
| `dodatki.tex` | tabele, styl kodu C#, makra miejsc na treść, polskie nazwy dla `\cref` |
| `strona_tytulowa.tex` | strona tytułowa według `pd_inz_pl.doc` |
| `abstract.tex` | streszczenie po polsku i angielsku |
| `acronyms.tex` | lista skrótów |
| `bibliography.bib` | propozycje literatury |
| `chapters/` | rozdziały pracy |
| `chapters/katalog.tex` | magazyn klocków, widoczny tylko w szkicu |
| `klocki/` | gotowe tabele, wzory, miejsca na rysunki, pseudokody i listingi |
| `figures/` | rysunki; wykresy zapisuje tu `python Analiza/wykresy.py` |
| `listingi/` | fragmenty kodu |

## Jak używać klocków

1. Załóż swój rozdział w `chapters/` i dopisz go w `main.tex` przez `\include`.
2. Wytnij potrzebny blok z pliku w `klocki/` i wklej do swojego rozdziału.
3. Każdą tabelę, każdy rysunek i każde źródło przywołaj w tekście, np. `\cref{tab:e1}`. To wymaganie Wydziału.
4. Gdy katalog się opróżni, usuń jego blok `\ifszkic` z `main.tex`.

| Makro | Do czego służy |
|---|---|
| `\CoTuMaByc{...}` | ramka ze wskazówką, znika po `\szkicfalse` |
| `\wyn` | pusta komórka tabeli |
| `\Wpisz{...}` | pole do wpisania w tekście albo pseudokodzie |
| `\kol{czas_ms}` | nazwa kolumny lub pliku z podkreśleniami |
| `\MiejsceNaRysunek[szerokość]{plik}{podpis}{etykieta}{opis}` | wstawia `figures/plik.pdf`, `.png` albo `.jpg`, a gdy pliku nie ma, ramkę zastępczą |
| `\MiejsceNaObraz[szerokość]{plik}{opis}` | to samo bez podpisu, np. wewnątrz `subfigure` |
| `\MiejsceNaListing{plik}{podpis}{etykieta}{opis}` | wstawia `listingi/plik.cs` |

Po każdej kompilacji dziennik pokazuje ostrzeżenie z liczbą miejsc do uzupełnienia. `\szkicfalse` pokazuje wygląd końcowy, ale puste komórki stają się wtedy niewidoczne, więc przed oddaniem sprawdź to ostrzeżenie.

Pliki `listingi/*.cs` zapisuj w UTF-8 bez znacznika BOM. Obecne 10 listingów to fragmenty kodu z commita `4e10be9`. Pierwsza linia każdego z nich oznacza fragment wygenerowany z pomocą AI, czego wymaga Wydział. Po zmianie kodu wytnij fragment jeszcze raz.

Skrót przez `\ac{...}` działa w tekście. W tabeli z podpisem nad nią pakiet `acro` go nie liczy, więc tam pisz skrót zwykłym tekstem.

## Zasady Wydziału o narzędziach AI

- [Wytyczne dot. narzędzi GenAI](https://wit.pwr.edu.pl/wydzial/jakosc_ksztalcenia/wytyczne-dot-narzedzigenai), styczeń 2026
- [Praca dyplomowa: wymagania, wzory, szablony](https://wit.pwr.edu.pl/studenci/dyplomanci/praca-dyplomowa)

Sekcji o użyciu AI w pracy nie wolno pominąć. Zakres użycia uzgodnij z opiekunem przed jej napisaniem.

## Różnice względem materiałów Wydziału

- Strona tytułowa jest według `pd_inz_pl.doc`, a nie według makra z `settings.tex`. Powrót: zakomentuj `\input{strona_tytulowa}` w `main.tex`.
- W `main.tex` dodano `\pagestyle{plain}`, bo w oryginalnym szablonie numer strony był tylko na pierwszej stronie rozdziału.
- Spis literatury i spisy rysunków, tabel oraz listingów są w spisie treści.
- Szablon WIT ma większe nagłówki i interlinię 1,15, a zalecenia edytorskie: nagłówki 14/13/12 pkt i interlinię pojedynczą. Zalecenia włącza linia `\WlaczZaleceniaEdytorskie` w `main.tex`.
