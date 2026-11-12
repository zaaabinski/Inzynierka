# Wyniki badań — generator zamków WFC

Komplet eksperymentów E1–E9, wykonanych na zbudowanej aplikacji `Zamek.exe`.

| | |
|---|---|
| Backend skryptowy | **IL2CPP** (Release), wszystkie serie |
| Maszyna | ZAAABINSKIPC, i7-12700K, 64 GB RAM |
| Unity | 6000.6.2f1 |
| Daty serii | E1–E6, E8, E9: 10.11.2026 · E7: 12.11.2026 |
| Zmierzone uruchomienia | **8460** |
| Naruszenia zasad | **0** w całym zbiorze |

Pomiary czasu generacji robiono bez okna (`-batchmode -nographics`); E7 w oknie 1600 × 900, bo mierzy koszt rysowania. Wartości poniżej to **mediany**, chyba że napisano inaczej.

---

## E1 — rozwiązywacze (pytania 1 i 7)

Siatka 20 × 20 × 13, zestaw Pełny, LevelFirst + Restart, 100 ziaren. Wszystkie rozwiązywacze dają te same mapy, więc różnią się wyłącznie czasem i pamięcią.

| Rozwiązywacz | Czas [ms] | Q1–Q3 [ms] | Pamięć szczytowa | Alokacje GC |
|---|---|---|---|---|
| Naive (listy) | 837,8 | 803–1449 | 4,7 MB | 7828 kB |
| SupportCount (liczniki wsparcia) | 81,3 | 80–132 | 12,1 MB | 16 616 kB |
| Bitset (maski bitowe) | 26,3 | 25–36 | 1,2 MB | 3532 kB |
| **Burst (maski bitowe + Burst)** | **20,1** | 18–25 | 1,2 MB | 3532 kB |

**Wniosek.** Burst jest **41,6 × szybszy od Naive** i o ok. 24% szybszy od Bitset. SupportCount kupuje przyspieszenie nad Naive ceną pamięci — jako jedyny zużywa jej więcej (12,1 MB), podczas gdy maski bitowe schodzą do 1,2 MB. Bitset i Burst mają identyczną pamięć i alokacje, bo trzymają dane tak samo; różnica to wyłącznie kompilacja.

## E2 — kolejność wyboru komórek (pytanie 2)

Bitset + Restart, 20 × 20 × 13, 100 ziaren.

| Kolejność | Czas na mapę [ms] | Próby | Sprzeczności | Udane próby |
|---|---|---|---|---|
| **LevelFirst (piętrami)** | 26,4 | 139 | 39 | **71,9%** |
| Scanline | 16,3 | 159 | 59 | 62,9% |
| MinRemaining (MRV) | 226,7 | 2649 | 2549 | 3,8% |
| Entropy | 330,7 | 4086 | 3986 | 2,4% |
| Random | 381,7 | 5765 | 5665 | 1,7% |

**Wniosek.** Heurystyki klasyczne dla WFC (MRV, entropia) wypadają tu **najgorzej**: schodzą do 2–4% udanych prób i potrzebują tysięcy restartów na 100 map. Kolejność narzucona strukturą zadania — piętrami — wygrywa z marginesem rzędu dwóch rzędów wielkości w liczbie prób. Scanline ma najkrótszą pojedynczą próbę, ale myli się częściej niż LevelFirst.

## E3 — reakcja na sprzeczność (pytanie 3)

Bitset, LevelFirst, 100 ziaren, limit 600 s na mapę.

| Siatka | Restart | Backtrack | LocalReset |
|---|---|---|---|
| 10 × 10 × 8 | 3,0 ms · 100/100 | 3,0 ms · 100/100 | 3,0 ms · 100/100 |
| 20 × 20 × 13 | 26,3 ms · 100/100 | 25,7 ms · 100/100 | 25,4 ms · 100/100 |
| 40 × 40 × 13 | 300,1 ms · 100/100 | 117,1 ms · 100/100 | 116,7 ms · 100/100 |
| **80 × 80 × 13** | **114 750 ms · 50/100** | **496,6 ms · 100/100** | **497,2 ms · 100/100** |

**Wniosek.** Do 20 × 20 × 13 wybór nie ma znaczenia. Od 40 × 40 start od nowa zaczyna przegrywać (2,6 ×), a przy 80 × 80 × 13 **załamuje się całkowicie**: 73 864 próby, połowa map nie powstaje w limicie 600 s, mediana rośnie do ~115 s. Cofanie decyzji i reset lokalny trzymają 100% map w pół sekundy i są między sobą nieodróżnialne. To najmocniejszy wynik w całym zbiorze.

## E4 — skalowanie (pytanie 4)

Wszystkie rozwiązywacze, LevelFirst + **Restart**, limit 600 s na mapę.

| Rozwiązywacz | 10 × 10 × 8 | 20 × 20 × 13 | 40 × 40 × 13 | 80 × 80 × 13 |
|---|---|---|---|---|
| Naive | 90,0 ms | 835,5 ms | 12 666 ms | 601 188 ms · **5/100** |
| SupportCount | 10,3 ms | 82,9 ms | 1229 ms | 600 000 ms · 45/100 |
| Bitset | 3,1 ms | 26,0 ms | 297 ms | 115 059 ms · 50/100 |
| **Burst** | **2,3 ms** | **19,8 ms** | **207 ms** | **73 773 ms** · 50/100 |

Pamięć szczytowa przy 80 × 80 × 13: Bitset i Burst 16,7 MB, Naive 64,3 MB, SupportCount 185,4 MB.

**Wniosek.** Przewaga Burst nad Naive rośnie z rozmiarem (39 × przy 10 × 10, 61 × przy 40 × 40). Sufit 50% udanych map przy 80 × 80 **nie jest winą rozwiązywacza** — E3 pokazuje, że ten sam Bitset z cofaniem decyzji robi tam 100/100 w pół sekundy. Ograniczeniem jest reakcja na sprzeczność, nie reprezentacja możliwości.

## E5 — liczba kafelków (pytanie 5)

| Zestaw | Stany | Czas [ms] | Udane próby | Zabudowa |
|---|---|---|---|---|
| Prosty | 26 | 5,8 | 46,1% | 29,1% |
| Średni | 79 | 11,7 | 79,4% | 35,8% |
| PełnyBezDekoracji | 146 | 25,9 | 62,5% | 40,5% |
| Pełny | 170 | 26,0 | 71,9% | 39,4% |

**Wniosek.** Czas rośnie z liczbą kafelków mniej więcej liniowo, ale **odsetek udanych prób już nie** — Prosty (26 stanów) ma gorszą skuteczność niż Pełny (170). O trudności decyduje struktura reguł, a nie sama liczba stanów. Dekoracje są praktycznie darmowe: 146 → 170 stanów to +0,1 ms.

## E6 — reguły i wagi (pytanie 6)

| Wariant | Czas [ms] | Udane próby | Zabudowa |
|---|---|---|---|
| Pełny (odniesienie) | 26,4 | 71,9% | 39,4% |
| Bez wolnego pola przy ścianach | 101,4 | **9,5%** | **56,6%** |
| Bez drzwi nad schodami | 26,5 | **88,5%** | 40,4% |
| Waga trawy × 0,40 | 35,8 | 46,9% | 50,6% |
| Waga trawy × 0,25 | 39,1 | 32,3% | 55,1% |

**Wniosek.** Gęstsza zabudowa kosztuje skutecznością i to nieliniowo: usunięcie wolnego pola przy ścianach podnosi zabudowę o 17 punktów, ale ścina udane próby **siedmiokrotnie** (71,9% → 9,5%). Wagi trawy dają łagodniejszy handel — ×0,25 to +15,7 punktu zabudowy za spadek skuteczności do 32%. Zakaz drzwi nad schodami jest darmowy, a nawet poprawia skuteczność.

## E7 — rysowanie mapy

Burst, 30 ziaren, okno 1600 × 900, 120 klatek pomiaru na mapę. Poniżej siatka 80 × 80 × 13 (51 603 obiekty).

| Rysowanie | Łączenie | Klatka [ms] | Najdłuższa [ms] | Wywołania rysowania |
|---|---|---|---|---|
| GameObjects | SrpBatcher | 6,039 | 8,081 | 51 603 |
| GameObjects | InstancingOnly | 4,252 | 6,259 | 507 |
| GameObjects | SrpBatcher + ResidentDrawer | 0,691 | 1,800 | 211 |
| Instanced | dowolne | ~1,09 | ~2,02 | 267,5 |
| Combined | dowolne | ~0,59 | ~1,18 | 613 |
| **CombinedAtlas** | **dowolne** | **0,458–0,514** | ~1,28 | **67** |

**Wniosek.** Konsolidacja geometrii **znosi zależność od ustawień potoku graficznego**. Same GameObjects wahają się od 6,04 do 0,69 ms zależnie od tego, co włączone w URP — różnica 8,8 ×. Mapa złożona w jedną siatkę z atlasem materiałów daje 0,46–0,51 ms **niezależnie** od łączenia, przy 67 wywołaniach rysowania zamiast 51 603 (**770 × mniej**). Ten sam układ wygrywa na każdym z czterech rozmiarów siatki.

## E8 i E9 — restrykcyjność reguł

Korelacja Spearmana miar restrykcyjności z odsetkiem udanych prób:

| Miara | E8 (8 zestawów) | E9 (81 wariantów) | E9 bez map zdegenerowanych (75) |
|---|---|---|---|
| Gęstość reguł | 0,098 | 0,388 | **0,531** |
| Gęstość ważona | 0,048 | 0,430 | **0,569** |
| Kappa | 0,146 | −0,388 | **−0,531** |

Zaostrzanie reguł zestawu Pełny (E9, 10 losowań na poziom):

| Usunięte pary sąsiadów | Udane próby | Zabudowa | Kappa |
|---|---|---|---|
| 0% | 71,4% | 39,6% | 1,471 |
| 0,5% | 32,4% | 42,3% | 1,474 |
| 2% | 24,7% | 39,4% | 1,483 |
| 5% | 28,0% | 25,5% | 1,501 |
| 12% | 25,7% | 9,2% | 1,546 |

**Wniosek.** Na ośmiu gotowych zestawach korelacja **praktycznie nie istnieje** (0,05–0,15) — zestawy różnią się zbyt wieloma rzeczami naraz, żeby jedna miara cokolwiek przewidziała. Dopiero kontrolowane zaostrzanie tych samych reguł daje korelację umiarkowaną (0,39–0,43), a po odrzuceniu map zdegenerowanych — **umiarkowaną do silnej (0,53–0,57)**. Znak kappy jest przeciwny, zgodnie z definicją miary. Praktyczny wniosek: miary restrykcyjności mają sens jako wskaźnik *zmiany* w obrębie jednego zestawu, a nie jako sposób porównywania różnych zestawów. Widać też próg: już 0,5% usuniętych par ścina skuteczność z 71% do 32%, a od 5% zaczynają znikać budynki (zabudowa 39,6% → 9,2%).

---

## Pliki

Każdy eksperyment ma plik surowy `E<n>_<data>.csv` (jeden wiersz = jedno uruchomienie) oraz `_warianty.csv` z podsumowaniem per kombinacja ustawień. E5, E6, E8 i E9 mają dodatkowo `_korelacje.csv`.

| Plik | Wiersze | Udane mapy |
|---|---|---|
| E1_20261110_000028.csv | 400 | 100% |
| E2_20261110_000243.csv | 500 | 100% |
| E3_20261110_000923.csv | 1200 | 96% |
| E4_20261110_024428.csv | 1600 | 84% |
| E5_20261110_000513.csv | 400 | 100% |
| E6_20261110_000524.csv | 500 | 100% |
| E7_20261112_160534.csv | 1440 | 100% |
| E8_20261110_000556.csv | 800 | 100% |
| E9_20261110_000624.csv | 1620 | 68% |

Niższe odsetki w E4 i E9 są wynikiem badania, nie usterką: w E4 część map przy 80 × 80 × 13 wyczerpuje limit 600 s, a w E9 zaostrzone reguły mają prowadzić do sprzeczności.

Osobno, spoza tej serii: `E1_Mono_2026-10-21.csv` (Naive na backendzie Mono) oraz `E1_IL2CPP.csv` i `E7_IL2CPP.csv` z 9.11.2026 — wcześniejsze przebiegi tych samych eksperymentów, zgodne z nowymi.

## Mono kontra IL2CPP

Ten sam rozwiązywacz Naive, ta sama siatka 20 × 20 × 13, 100 ziaren:

| Backend | Mediana | Q3 | Maksimum |
|---|---|---|---|
| Mono | 3040,0 ms | 5506,3 ms | 13 962,5 ms |
| IL2CPP | 850,7 ms | 1511,2 ms | 3942,4 ms |

Przejście na IL2CPP skraca czas generacji **3,6 ×**. Wszystkie wyniki w tym dokumencie pochodzą z IL2CPP.
