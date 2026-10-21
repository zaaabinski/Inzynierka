"""Analiza wyników badań generatora zamków WFC (pliki CSV z aplikacji Zamek).

Użycie (z folderu projektu):
    python Analiza/analiza_wynikow.py                      # najnowszy plik z folderu Wyniki
    python Analiza/analiza_wynikow.py Wyniki/E1_*.csv      # wybrane pliki (można podać kilka)
    python Analiza/analiza_wynikow.py Wyniki/E2_*.csv --wyjscie Wyniki/Wykresy

Dla każdego wariantu (kombinacji ustawień, które w danych się zmieniają) wypisuje: liczbę uruchomień, odsetek udanych
map, medianę i rozstęp międzykwartylowy czasu generacji, średnią liczbę prób i sprzeczności, sumę naruszeń zasad,
medianę pamięci i alokacji. Rysuje wykres pudełkowy czasu generacji i, jeśli wariantów jest więcej niż jeden,
liczy test Manna-Whitneya dla każdej pary wariantów.

Wymagane biblioteki: pandas, matplotlib, scipy (plik requirements.txt w tym folderze).
"""

import argparse
import glob
import itertools
import os
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import pandas as pd  # noqa: E402

try:
    from scipy.stats import mannwhitneyu
except ImportError:  # scipy jest potrzebne tylko do testu Manna-Whitneya
    mannwhitneyu = None

USTAWIENIA = ["rozwiazywacz", "kolejnosc", "sprzecznosc", "zestaw", "rozmiar"]


def wczytaj(pliki):
    ramki = [pd.read_csv(plik) for plik in pliki]
    dane = pd.concat(ramki, ignore_index=True)
    dane["rozmiar"] = dane["x"].astype(str) + " × " + dane["y"].astype(str) + " × " + dane["z"].astype(str)
    zmienne = [kolumna for kolumna in USTAWIENIA if dane[kolumna].nunique() > 1] or ["rozwiazywacz"]
    dane["wariant"] = dane[zmienne].astype(str).agg(", ".join, axis=1)
    return dane, zmienne


def podsumowanie(dane):
    grupy = dane.groupby("wariant", sort=False)
    tabela = pd.DataFrame({
        "uruchomienia": grupy.size(),
        "udane_proc": grupy["udane"].mean() * 100,
        "mediana_ms": grupy["czas_ms"].median(),
        "q1_ms": grupy["czas_ms"].quantile(0.25),
        "q3_ms": grupy["czas_ms"].quantile(0.75),
        "proby_sr": grupy["proby"].mean(),
        "sprzecznosci_sr": grupy["sprzecznosci"].mean(),
        "naruszenia_suma": grupy["naruszenia"].sum(min_count=1),
        "pamiec_mb_mediana": grupy["pamiec_szczytowa_mb"].median(),
        "alokacje_kb_mediana": grupy["alokacje_gc_kb"].median(),
        "wieze_sr": grupy["wieze"].mean(),
    })
    tabela["iqr_ms"] = tabela["q3_ms"] - tabela["q1_ms"]
    return tabela[["uruchomienia", "udane_proc", "mediana_ms", "iqr_ms", "q1_ms", "q3_ms", "proby_sr",
                   "sprzecznosci_sr", "naruszenia_suma", "pamiec_mb_mediana", "alokacje_kb_mediana", "wieze_sr"]]


def wykres_czasu(dane, eksperyment, plik):
    warianty = list(dict.fromkeys(dane["wariant"]))
    serie = [dane.loc[dane["wariant"] == wariant, "czas_ms"].dropna() for wariant in warianty]
    szerokosc = max(6.0, 1.6 * len(warianty))
    figura, osie = plt.subplots(figsize=(szerokosc, 5.0))
    osie.boxplot(serie, showfliers=True)
    osie.set_xticks(range(1, len(warianty) + 1))
    osie.set_xticklabels(warianty, rotation=20 if len(warianty) > 3 else 0, ha="right" if len(warianty) > 3 else "center")
    osie.set_ylabel("Czas generacji mapy [ms]")
    osie.set_title(f"Czas generacji według wariantu – {eksperyment} ({len(dane)} uruchomień)")
    najmniejszy = min((seria.min() for seria in serie if len(seria)), default=1)
    najwiekszy = max((seria.max() for seria in serie if len(seria)), default=1)
    if najmniejszy > 0 and najwiekszy / najmniejszy > 50:
        osie.set_yscale("log")
        osie.set_ylabel("Czas generacji mapy [ms], skala logarytmiczna")
    osie.grid(axis="y", alpha=0.3)
    figura.tight_layout()
    figura.savefig(plik, dpi=150)
    plt.close(figura)


def test_manna_whitneya(dane):
    warianty = list(dict.fromkeys(dane["wariant"]))
    wiersze = []
    for a, b in itertools.combinations(warianty, 2):
        x = dane.loc[dane["wariant"] == a, "czas_ms"].dropna()
        y = dane.loc[dane["wariant"] == b, "czas_ms"].dropna()
        if len(x) == 0 or len(y) == 0:
            continue
        wynik = mannwhitneyu(x, y, alternative="two-sided")
        wiersze.append({"wariant_a": a, "wariant_b": b, "U": wynik.statistic, "p": wynik.pvalue})
    return pd.DataFrame(wiersze)


def main():
    parser = argparse.ArgumentParser(description="Analiza wyników badań generatora zamków WFC.")
    parser.add_argument("pliki", nargs="*", help="pliki CSV z folderu Wyniki (domyślnie najnowszy)")
    parser.add_argument("--wyjscie", default=None, help="folder na wykresy i tabele (domyślnie Wyniki/Wykresy)")
    argumenty = parser.parse_args()

    folder_projektu = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    folder_wynikow = os.path.join(folder_projektu, "Wyniki")
    pliki = [plik for wzorzec in argumenty.pliki for plik in glob.glob(wzorzec)]
    if not pliki:
        wszystkie = glob.glob(os.path.join(folder_wynikow, "*.csv"))
        if not wszystkie:
            sys.exit(f"Brak plików CSV w {folder_wynikow}. Uruchom badanie w aplikacji albo podaj pliki.")
        pliki = [max(wszystkie, key=os.path.getmtime)]

    dane, zmienne = wczytaj(pliki)
    eksperyment = ", ".join(dict.fromkeys(dane["eksperyment"].astype(str)))
    wyjscie = argumenty.wyjscie or os.path.join(folder_wynikow, "Wykresy")
    os.makedirs(wyjscie, exist_ok=True)

    print(f"Pliki: {', '.join(pliki)}")
    print(f"Eksperyment: {eksperyment}; uruchomień: {len(dane)}; warianty różnią się: {', '.join(zmienne)}")
    print(f"Środowisko: {', '.join(dict.fromkeys(dane['srodowisko'].astype(str)))}; komputer: {', '.join(dict.fromkeys(dane['komputer'].astype(str)))}")

    tabela = podsumowanie(dane)
    pd.set_option("display.width", 200)
    pd.set_option("display.max_columns", 20)
    print(tabela.round(2).to_string())
    nazwa = eksperyment.replace(", ", "_")
    tabela.round(3).to_csv(os.path.join(wyjscie, f"{nazwa}_podsumowanie.csv"))

    plik_wykresu = os.path.join(wyjscie, f"{nazwa}_czas.png")
    wykres_czasu(dane, eksperyment, plik_wykresu)
    print(f"Wykres: {plik_wykresu}")

    if dane["wariant"].nunique() > 1:
        if mannwhitneyu is None:
            print("Test Manna-Whitneya pominięty: brak biblioteki scipy.")
        else:
            testy = test_manna_whitneya(dane)
            print("Test Manna-Whitneya (czas_ms, dwustronny):")
            print(testy.to_string(index=False))
            testy.to_csv(os.path.join(wyjscie, f"{nazwa}_mann_whitney.csv"), index=False)


if __name__ == "__main__":
    main()
