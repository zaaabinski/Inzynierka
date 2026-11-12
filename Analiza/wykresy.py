# Skrypt wygenerowany z pomocą AI (Claude Code).
"""Wykresy do pracy z wyników badań generatora zamków WFC (pliki CSV z aplikacji Zamek).

Użycie (z folderu projektu):
    python Analiza/wykresy.py                             # wyniki z folderu Wyniki, wykresy do Praca/figures
    python Analiza/wykresy.py --wyniki D:/Pomiary/Wyniki  # wyniki z innego folderu
    python Analiza/wykresy.py --wyjscie Wyniki/Wykresy    # wykresy do innego folderu

Dla każdego eksperymentu bierze najnowszy plik E<numer>_<data>_<godzina>.csv (jedna mapa w wierszu). Wartości na
wariant liczy tak jak aplikacja i porównuje je z plikiem _warianty.csv, jeśli leży obok. Wykresy zapisuje jako PDF
pod nazwami z Praca/klocki/rysunki.tex, więc po kompilacji same trafiają do pracy. Szerokość pliku jest równa
szerokości rysunku w pracy, więc napisy mają 9 pkt. Eksperyment bez wyników pomija z komunikatem.

Wymagane biblioteki: pandas, matplotlib (plik requirements.txt w tym folderze).
"""

import argparse
import os
import re
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import pandas as pd  # noqa: E402
from matplotlib.lines import Line2D  # noqa: E402
from matplotlib.ticker import (FixedLocator, FuncFormatter, LogLocator, NullFormatter, NullLocator,  # noqa: E402
                               ScalarFormatter)

FOLDER_PROJEKTU = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Szerokość tekstu: A4 bez marginesów 25 mm i 5 mm na oprawę (Praca/settings.tex).
MM = 1 / 25.4
SZEROKI = 155 * MM  # rysunek na całą szerokość: \MiejsceNaRysunek[\linewidth]
WASKI = 0.85 * SZEROKI  # domyślna szerokość \MiejsceNaRysunek

# Kolory w kolejności sprawdzonej pod kątem rozróżnialności przy zaburzeniach widzenia barw. Kolor należy do wariantu,
# a nie do miejsca na liście, więc brak jednego wariantu nie zmienia kolorów pozostałych. Znaczniki odróżniają serie
# także na wydruku czarno-białym.
KOLORY = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300", "#6250d6", "#e34948"]
ZNACZNIKI = ["o", "s", "^", "D", "v", "P", "X", "*"]
JASNY = "#cde2fb"  # wypełnienie pudełek
CIEMNY = "#104281"  # mediany
TUSZ = "#0b0b0b"
TUSZ_2 = "#52514e"
OS = "#898781"
SIATKA = "#e1e0d9"
LITERY = "abcdefgh"

# Kolejność i nazwy wariantów jak w tabelach pracy (Praca/klocki/wyniki.tex).
KOLEJNOSC = {
    "rozwiazywacz": ["Naive", "SupportCount", "Bitset", "Burst"],
    "kolejnosc": ["LevelFirst", "Scanline", "MinRemaining", "Entropy", "Random"],
    "sprzecznosc": ["Restart", "Backtrack", "LocalReset"],
    "zestaw": ["Prosty", "Sredni", "PelnyBezDekoracji", "Pelny",
               "PelnyBezWolnegoPola", "PelnyBezDrzwiNadSchodami", "PelnyTrawa040", "PelnyTrawa025"],
    "rysowanie": ["GameObjects", "Instanced", "Combined", "CombinedAtlas"],
    "laczenie_rysowania": ["SrpBatcherAndResidentDrawer", "SrpBatcher", "InstancingOnly"],
}
NAZWY = {
    "SrpBatcherAndResidentDrawer": "SRP Batcher + Resident Drawer",
    "SrpBatcher": "Sam SRP Batcher",
    "InstancingOnly": "Samo instancjonowanie",
}

# Liczba stanów zestawu: Assets/Scripts/Tests/RulesControlTests.cs.
STANY = {"Prosty": 26, "Sredni": 79, "PelnyBezDekoracji": 146, "Pelny": 170}

# Limit czasu jednej mapy w E3 i E4: Assets/Data/Experiments/E3.asset i E4.asset.
LIMIT_MS = 600_000

# Mapa zdegenerowana jak w BenchmarkSession.cs: średnia zabudowa udanych map wariantu poniżej 1% albo powyżej 90%.
ZABUDOWA_MIN = 1.0
ZABUDOWA_MAX = 90.0

# Rozwiązywacz, z którego map powstaje wykres udziału propagacji (E4).
ROZWIAZYWACZ_PROPAGACJI = "Bitset"

WZORZEC_PLIKU = re.compile(r"^(E\d+)_(\d{8}_\d{6})\.csv$")


# ---------------------------------------------------------------- wygląd

def ustaw_styl():
    plt.rcParams.update({
        "font.family": "serif",
        "font.serif": ["Times New Roman", "TeX Gyre Termes", "Nimbus Roman", "STIXGeneral"],
        "mathtext.fontset": "stix",
        "font.size": 9,
        "axes.titlesize": 9,
        "axes.labelsize": 9,
        "legend.fontsize": 9,
        "xtick.labelsize": 8.5,
        "ytick.labelsize": 8.5,
        "text.color": TUSZ,
        "axes.labelcolor": TUSZ,
        "axes.edgecolor": OS,
        "axes.linewidth": 0.6,
        "axes.spines.top": False,
        "axes.spines.right": False,
        "axes.grid": True,
        "axes.axisbelow": True,
        "grid.color": SIATKA,
        "grid.linewidth": 0.6,
        "xtick.color": OS,
        "ytick.color": OS,
        "xtick.labelcolor": TUSZ_2,
        "ytick.labelcolor": TUSZ_2,
        "xtick.major.width": 0.6,
        "ytick.major.width": 0.6,
        "xtick.minor.width": 0.4,
        "ytick.minor.width": 0.4,
        "lines.linewidth": 1.2,
        "lines.markersize": 4.5,
        "legend.frameon": False,
        "legend.handlelength": 1.8,
        "legend.columnspacing": 1.6,
        "pdf.fonttype": 42,
        "savefig.transparent": True,
    })


def odstepy(tekst):
    """Wąski odstęp co trzy cyfry w części całkowitej, gdy ma ona co najmniej 5 cyfr (20 800, ale 5200),
    jak \\, w tabelach pracy."""
    calkowita, kropka, reszta = tekst.partition(".")
    cyfry = calkowita.lstrip("\u2212-")
    if len(cyfry) >= 5:
        calkowita = calkowita[:len(calkowita) - len(cyfry)] + re.sub(r"(?<=\d)(?=(\d{3})+$)", "\u202f", cyfry)
    return calkowita + kropka + reszta


def liczba(x, _pozycja=None):
    """Liczba po polsku: przecinek dziesiętny, od 10 000 odstęp co trzy cyfry."""
    tekst = str(round(x)) if abs(x) >= 1000 or float(x).is_integer() else f"{x:.3g}"
    return odstepy(tekst.replace("-", "\u2212")).replace(".", ",")


class PolskaPodzialka(ScalarFormatter):
    """Podziałka liniowa jak w matplotlib (ta sama liczba miejsc po przecinku na całej osi), ale po polsku."""

    def __init__(self):
        super().__init__(useOffset=False)
        self.set_scientific(False)

    def __call__(self, x, pos=None):
        return odstepy(super().__call__(x, pos)).replace(".", ",")


def nazwa(wartosc):
    return NAZWY.get(wartosc, str(wartosc))


def uporzadkuj(kolumna, wartosci):
    """Warianty w kolejności tabel pracy; nieznane na końcu, w kolejności pojawienia się w pliku."""
    obecne = list(dict.fromkeys(pd.Series(wartosci).dropna()))
    znane = [w for w in KOLEJNOSC.get(kolumna, []) if w in obecne]
    return znane + [w for w in obecne if w not in znane]


def numery(kolumna, warianty):
    """Stały numer koloru i znacznika każdego wariantu."""
    znane = KOLEJNOSC.get(kolumna, [])
    nieznane = [w for w in warianty if w not in znane]
    return {w: (znane.index(w) if w in znane else len(znane) + nieznane.index(w)) % len(KOLORY) for w in warianty}


def czy_log(wartosci, prog=30):
    """Skala logarytmiczna, gdy wartości są dodatnie, a największa jest ponad 30 razy większa od najmniejszej."""
    wartosci = np.asarray(wartosci, dtype=float)
    wartosci = wartosci[np.isfinite(wartosci)]
    return wartosci.size > 0 and wartosci.min() > 0 and wartosci.max() / wartosci.min() > prog


def skala_log(ax, ktora, wartosci):
    """Skala logarytmiczna z polskimi liczbami; przy zakresie do 2,5 rzędu wielkości podziałka także na 2 i 5."""
    wartosci = np.asarray(wartosci, dtype=float)
    wartosci = wartosci[np.isfinite(wartosci) & (wartosci > 0)]
    rzedy = np.log10(wartosci.max() / wartosci.min()) if wartosci.size else 0
    os_ = ax.xaxis if ktora == "x" else ax.yaxis
    (ax.set_xscale if ktora == "x" else ax.set_yscale)("log")
    os_.set_major_locator(LogLocator(base=10, subs=(1.0,) if rzedy > 2.5 else (1.0, 2.0, 5.0)))
    os_.set_major_formatter(FuncFormatter(liczba))
    os_.set_minor_locator(LogLocator(base=10, subs=np.arange(2.0, 10.0)))
    os_.set_minor_formatter(NullFormatter())


def os_wartosci(ax, ktora, wartosci, log=None):
    """Oś liczbowa: logarytmiczna albo liniowa (log=None: wybór według czy_log)."""
    if log is None:
        log = czy_log(wartosci)
    if log:
        skala_log(ax, ktora, wartosci)
    else:
        (ax.xaxis if ktora == "x" else ax.yaxis).set_major_formatter(PolskaPodzialka())


def os_kategorii(ax, etykiety, poziome):
    pozycje = np.arange(len(etykiety))
    if poziome:
        ax.set_yticks(pozycje, etykiety)
        ax.set_ylim(len(etykiety) - 0.5, -0.5)  # pierwszy wariant na górze
        ax.grid(axis="y", visible=False)
        ax.tick_params(axis="y", length=0)
    else:
        ax.set_xticks(pozycje, etykiety)
        ax.set_xlim(-0.5, len(etykiety) - 0.5)
        ax.grid(axis="x", visible=False)
        ax.tick_params(axis="x", length=0)


def os_komorek(ax, komorki):
    """Oś liczby komórek: skala logarytmiczna, podziałka tylko na rozmiarach z badania."""
    wartosci = sorted({int(k) for k in komorki})
    ax.set_xscale("log")
    ax.xaxis.set_major_locator(FixedLocator(wartosci))
    ax.xaxis.set_major_formatter(FuncFormatter(liczba))
    ax.xaxis.set_minor_locator(NullLocator())
    ax.set_xlim(wartosci[0] / 1.6, wartosci[-1] * 1.6)


def os_procentow(ax, ktora):
    (ax.set_xlim if ktora == "x" else ax.set_ylim)(-2, 102)
    os_ = ax.xaxis if ktora == "x" else ax.yaxis
    os_.set_major_locator(FixedLocator([0, 20, 40, 60, 80, 100]))
    os_.set_major_formatter(FuncFormatter(liczba))


def tytul(ax, numer, tekst=""):
    ax.set_title(f"{LITERY[numer]}) {tekst}".rstrip(), loc="left")


# ---------------------------------------------------------------- dane

def znajdz_wyniki(folder):
    """{eksperyment: ścieżka najnowszego pliku głównego}."""
    najnowsze = {}
    for plik in os.listdir(folder):
        dopasowanie = WZORZEC_PLIKU.match(plik)
        if dopasowanie:
            eksperyment, kiedy = dopasowanie.groups()
            if eksperyment not in najnowsze or kiedy > najnowsze[eksperyment][0]:
                najnowsze[eksperyment] = (kiedy, os.path.join(folder, plik))
    return {eksperyment: sciezka for eksperyment, (_, sciezka) in najnowsze.items()}


def wczytaj(sciezka):
    dane = pd.read_csv(sciezka)
    # Kolumny x, y, z to kolejno E, N i piętra, jak w zapisie rozmiaru w pracy.
    dane["komorki"] = dane["x"] * dane["y"] * dane["z"]
    dane["rozmiar"] = dane["x"].astype(str) + " × " + dane["y"].astype(str) + " × " + dane["z"].astype(str)
    return dane


def mediany(dane, klucze, kolumna):
    grupy = dane.groupby(klucze, sort=True)[kolumna]
    return pd.DataFrame({
        "mediana": grupy.median(), "q1": grupy.quantile(0.25), "q3": grupy.quantile(0.75), "n": grupy.count(),
    }).reset_index()


def warianty(dane, klucze):
    """Wartości na wariant liczone jak w BenchmarkSession.cs, z którego powstaje plik _warianty.csv."""
    pomocnicze = dane.assign(zabudowa_lub_zero=dane["zabudowa_proc"].fillna(0))
    wynik = pomocnicze.groupby(klucze, sort=False).agg(
        mapy=("udane", "size"), udane_mapy=("udane", "sum"), proby=("proby", "sum"),
        suma_zabudowy=("zabudowa_lub_zero", "sum"), gestosc_regul_proc=("gestosc_regul_proc", "first"),
        gestosc_wazona_proc=("gestosc_wazona_proc", "first"), kappa=("kappa", "first"),
    ).reset_index()
    wynik["udane_proby_proc"] = 100 * wynik["udane_mapy"] / wynik["proby"].where(wynik["proby"] > 0)
    wynik["udane_proby_proc"] = wynik["udane_proby_proc"].fillna(0)
    wynik["zabudowa_proc"] = (wynik["suma_zabudowy"] / wynik["udane_mapy"].where(wynik["udane_mapy"] > 0)).fillna(0)
    wynik["zdegenerowany"] = (wynik["udane_mapy"] > 0) & (
        (wynik["zabudowa_proc"] < ZABUDOWA_MIN) | (wynik["zabudowa_proc"] > ZABUDOWA_MAX))
    return wynik


def sprawdz_z_aplikacja(sciezka, policzone, klucze, notatki):
    """Porównuje wartości na wariant z plikiem _warianty.csv zapisanym przez aplikację."""
    plik = sciezka[:-len(".csv")] + "_warianty.csv"
    if not os.path.exists(plik):
        notatki.append("nie ma pliku _warianty.csv, wartości na wariant policzone tylko tutaj")
        return
    razem = policzone.merge(pd.read_csv(plik), on=klucze, how="left", suffixes=("", "_aplikacja"))
    for kolumna in ("udane_proby_proc", "zabudowa_proc"):
        roznica = (razem[kolumna] - razem[kolumna + "_aplikacja"]).abs()
        if roznica.isna().any() or roznica.max() > 0.01:
            notatki.append(f"UWAGA: {kolumna} nie zgadza się z plikiem {os.path.basename(plik)}")


# ---------------------------------------------------------------- elementy wykresów

def pudelka(ax, dane, kolumna, wartosc, poziome=True, kategorie=None):
    """Wykres pudełkowy: pudełko od Q1 do Q3, kreska mediany, wąsy do 1,5 rozstępu międzykwartylowego,
    kropki to pojedyncze mapy. Zwraca wszystkie wartości."""
    kategorie = kategorie or uporzadkuj(kolumna, dane[kolumna])
    serie = [dane.loc[dane[kolumna] == k, wartosc].dropna().to_numpy(dtype=float) for k in kategorie]
    kategorie = [k for k, s in zip(kategorie, serie) if s.size]
    serie = [s for s in serie if s.size]
    pozycje = np.arange(len(kategorie))
    ax.boxplot(serie, positions=pozycje, orientation="horizontal" if poziome else "vertical", widths=0.55,
               showfliers=False, patch_artist=True,
               boxprops={"facecolor": JASNY, "edgecolor": KOLORY[0], "linewidth": 0.8},
               medianprops={"color": CIEMNY, "linewidth": 1.5, "zorder": 4},
               whiskerprops={"color": KOLORY[0], "linewidth": 0.8},
               capprops={"color": KOLORY[0], "linewidth": 0.8})
    losowe = np.random.default_rng(0)
    for p, s in zip(pozycje, serie):
        rozrzut = p + losowe.uniform(-0.17, 0.17, s.size)
        x, y = (s, rozrzut) if poziome else (rozrzut, s)
        ax.scatter(x, y, s=5, color=KOLORY[0], alpha=0.45, linewidths=0, zorder=3)
    os_kategorii(ax, [nazwa(k) for k in kategorie], poziome)
    return np.concatenate(serie) if serie else np.array([])


def slupki(ax, tabela, kolumna, wartosc):
    """Słupki poziome, jeden kolor (warianty rozróżnia oś)."""
    kategorie = uporzadkuj(kolumna, tabela[kolumna])
    wartosci = [float(tabela.loc[tabela[kolumna] == k, wartosc].iloc[0]) for k in kategorie]
    ax.barh(np.arange(len(kategorie)), wartosci, height=0.55, color=KOLORY[0], zorder=2)
    os_kategorii(ax, [nazwa(k) for k in kategorie], poziome=True)
    return wartosci


def linie_rozmiaru(ax, dane, kolumna_serii, kolumna_y, notatki):
    """Mediana i rozstęp międzykwartylowy wobec liczby komórek, jedna linia na wariant.
    Zwraca uchwyty legendy i wartości na osi y."""
    serie = uporzadkuj(kolumna_serii, dane[kolumna_serii])
    numer = numery(kolumna_serii, serie)
    uchwyty, wartosci = {}, []
    for i, seria in enumerate(serie):
        tabela = mediany(dane[dane[kolumna_serii] == seria], ["komorki"], kolumna_y)
        dodatnie = tabela[tabela["mediana"] > 0]
        if len(dodatnie) < len(tabela):
            notatki.append(f"{nazwa(seria)}: mediana {kolumna_y} równa 0 dla {len(tabela) - len(dodatnie)} rozmiarów, "
                           "punktów nie ma na wykresie")
        if dodatnie.empty:
            continue
        q1 = dodatnie["q1"].clip(lower=dodatnie["mediana"] * 1e-3)  # skala logarytmiczna nie pokaże zera
        przesuniecie = 10 ** ((i - (len(serie) - 1) / 2) * 0.02)  # żeby wąsy serii nie zachodziły na siebie
        k = numer[seria]
        uchwyty[seria] = ax.errorbar(
            dodatnie["komorki"] * przesuniecie, dodatnie["mediana"],
            yerr=[dodatnie["mediana"] - q1, dodatnie["q3"] - dodatnie["mediana"]],
            color=KOLORY[k], marker=ZNACZNIKI[k], markersize=4.5, linewidth=1.2, elinewidth=0.7, capsize=2,
            capthick=0.7, label=nazwa(seria), zorder=3)
        wartosci += list(q1) + list(dodatnie["q3"])
    return uchwyty, wartosci


def limit_czasu(ax, dane, kolumna_serii, notatki):
    """Linia limitu 600 s, gdy wyniki do niego dochodzą, i liczba map, które go osiągnęły."""
    czasy = dane["czas_ms"]
    na_limicie = dane[czasy >= 0.999 * LIMIT_MS]
    for (seria, rozmiar), grupa in na_limicie.groupby([kolumna_serii, "rozmiar"], sort=False):
        wszystkie = int(((dane[kolumna_serii] == seria) & (dane["rozmiar"] == rozmiar)).sum())
        notatki.append(f"{nazwa(seria)}, {rozmiar}: {len(grupa)} z {wszystkie} map doszło do limitu 600 s")
    if czasy.max() >= LIMIT_MS / 2:
        ax.axhline(LIMIT_MS, color=OS, linewidth=0.8, linestyle=(0, (4, 3)), zorder=1)
        # Podpis z lewej: małe siatki są szybkie, więc tam przy limicie nie ma punktów.
        ax.text(0.01, LIMIT_MS, "limit 600 s", transform=ax.get_yaxis_transform(), ha="left", va="bottom",
                fontsize=8, color=TUSZ_2)


def nachylenia(dane, kolumna_serii, kolumna_y):
    """Nachylenie a prostej log t = a log n + b dopasowanej do median (do tabeli w pracy)."""
    teksty = []
    for seria in uporzadkuj(kolumna_serii, dane[kolumna_serii]):
        tabela = mediany(dane[dane[kolumna_serii] == seria], ["komorki"], kolumna_y)
        tabela = tabela[tabela["mediana"] > 0]
        if len(tabela) >= 2:
            a = np.polyfit(np.log10(tabela["komorki"].astype(float)), np.log10(tabela["mediana"].astype(float)), 1)[0]
            teksty.append(f"{nazwa(seria)} {a:.2f}".replace(".", ","))
    return f"nachylenie a dla {kolumna_y}: " + "; ".join(teksty)


# ---------------------------------------------------------------- wykresy

def e1_czas(dane, sciezka, notatki):
    fig, ax = plt.subplots(figsize=(WASKI, 48 * MM), layout="constrained")
    os_wartosci(ax, "x", pudelka(ax, dane, "rozwiazywacz", "czas_ms"))
    ax.set_xlabel("Czas generacji [ms]")
    return fig


def e2_czas_udane(dane, sciezka, notatki):
    fig, (ax1, ax2) = plt.subplots(1, 2, sharey=True, figsize=(SZEROKI, 55 * MM), layout="constrained",
                                   gridspec_kw={"width_ratios": [1.5, 1]})
    os_wartosci(ax1, "x", pudelka(ax1, dane, "kolejnosc", "czas_ms"))
    ax1.set_xlabel("Czas generacji [ms]")
    tytul(ax1, 0)
    tabela = warianty(dane, ["kolejnosc"])
    sprawdz_z_aplikacja(sciezka, tabela, ["kolejnosc"], notatki)
    slupki(ax2, tabela, "kolejnosc", "udane_proby_proc")
    os_procentow(ax2, "x")
    ax2.set_xlabel("Udane próby [%]")
    tytul(ax2, 1)
    return fig


def czas_wobec_rozmiaru(dane, kolumna_serii, notatki):
    fig, ax = plt.subplots(figsize=(WASKI, 70 * MM), layout="constrained")
    uchwyty, wartosci = linie_rozmiaru(ax, dane, kolumna_serii, "czas_ms", notatki)
    os_komorek(ax, dane["komorki"])
    os_wartosci(ax, "y", wartosci, log=True)
    limit_czasu(ax, dane, kolumna_serii, notatki)
    ax.set_xlabel("Liczba komórek siatki")
    ax.set_ylabel("Czas generacji [ms]")
    fig.legend(handles=list(uchwyty.values()), loc="outside upper center", ncols=len(uchwyty))
    return fig


def e3_czas_rozmiar(dane, sciezka, notatki):
    return czas_wobec_rozmiaru(dane, "sprzecznosc", notatki)


def e4_czas_log(dane, sciezka, notatki):
    notatki.append(nachylenia(dane, "rozwiazywacz", "czas_ms"))
    return czas_wobec_rozmiaru(dane, "rozwiazywacz", notatki)


def e4_pamiec_log(dane, sciezka, notatki):
    notatki.append(nachylenia(dane, "rozwiazywacz", "pamiec_rzeczywista_mb"))
    fig, ax = plt.subplots(figsize=(WASKI, 70 * MM), layout="constrained")
    uchwyty, wartosci = linie_rozmiaru(ax, dane, "rozwiazywacz", "pamiec_rzeczywista_mb", notatki)
    if not uchwyty:
        raise ValueError("wszystkie mediany pamięci są równe 0")
    os_komorek(ax, dane["komorki"])
    os_wartosci(ax, "y", wartosci, log=True)
    ax.set_xlabel("Liczba komórek siatki")
    ax.set_ylabel("Pamięć rzeczywista [MB]")
    fig.legend(handles=list(uchwyty.values()), loc="outside upper center", ncols=len(uchwyty))
    return fig


def propagacja_udzial(dane, sciezka, notatki):
    mapy = dane[dane["rozwiazywacz"] == ROZWIAZYWACZ_PROPAGACJI].copy()
    if mapy.empty:
        raise ValueError(f"nie ma map rozwiązywacza {ROZWIAZYWACZ_PROPAGACJI}")
    razem = mapy["usuniete_propagacja"] + mapy["usuniete_decyzje"]
    mapy["udzial"] = 100 * mapy["usuniete_propagacja"] / razem.where(razem > 0)
    mapy = mapy.sort_values("komorki")
    notatki.append(f"mapy rozwiązywacza {ROZWIAZYWACZ_PROPAGACJI} z E4")
    fig, ax = plt.subplots(figsize=(WASKI, 62 * MM), layout="constrained")
    wartosci = pudelka(ax, mapy, "rozmiar", "udzial", poziome=False, kategorie=list(dict.fromkeys(mapy["rozmiar"])))
    komorki = mapy.groupby("rozmiar", sort=False)["komorki"].first()
    ax.set_xticks(range(len(komorki)), [f"{r}\n{liczba(k)} komórek" for r, k in komorki.items()])
    os_wartosci(ax, "y", wartosci, log=False)
    ax.set_ylabel("Udział propagacji [%]")
    return fig


def e5_czas_stany(dane, sciezka, notatki):
    tabela = mediany(dane, ["zestaw"], "czas_ms")
    nieznane = sorted(set(tabela["zestaw"]) - set(STANY))
    if nieznane:
        raise ValueError(f"nie znam liczby stanów zestawów {', '.join(nieznane)}; dopisz je w STANY")
    tabela["stany"] = tabela["zestaw"].map(STANY)
    tabela = tabela.sort_values("stany").reset_index(drop=True)
    fig, ax = plt.subplots(figsize=(WASKI, 62 * MM), layout="constrained")
    ax.errorbar(tabela["stany"], tabela["mediana"],
                yerr=[tabela["mediana"] - tabela["q1"], tabela["q3"] - tabela["mediana"]],
                color=KOLORY[0], marker="o", markersize=4.5, linewidth=1.2, elinewidth=0.7, capsize=2, capthick=0.7,
                zorder=3)
    zakres = tabela["stany"].max()
    for i, wiersz in tabela.iterrows():
        # Podpis z prawej u dołu; z lewej u góry, gdy następny punkt jest blisko.
        blisko = i + 1 < len(tabela) and tabela.loc[i + 1, "stany"] - wiersz["stany"] < 0.2 * zakres
        ax.annotate(nazwa(wiersz["zestaw"]), (wiersz["stany"], wiersz["mediana"]),
                    xytext=(-6, 3) if blisko else (6, -3), textcoords="offset points",
                    ha="right" if blisko else "left", va="bottom" if blisko else "top", fontsize=8.5, color=TUSZ_2)
    ax.set_xlim(0, zakres * 1.22)
    ax.xaxis.set_major_formatter(PolskaPodzialka())
    os_wartosci(ax, "y", list(tabela["q1"]) + list(tabela["q3"]))
    if not czy_log(list(tabela["q1"]) + list(tabela["q3"])):
        ax.set_ylim(bottom=0)
    ax.set_xlabel("Liczba stanów zestawu")
    ax.set_ylabel("Czas generacji [ms]")
    return fig


def e6_udane_zabudowa(dane, sciezka, notatki):
    tabela = warianty(dane, ["zestaw"])
    sprawdz_z_aplikacja(sciezka, tabela, ["zestaw"], notatki)
    fig, (ax1, ax2) = plt.subplots(1, 2, sharey=True, figsize=(SZEROKI, 52 * MM), layout="constrained")
    slupki(ax1, tabela, "zestaw", "udane_proby_proc")
    os_procentow(ax1, "x")
    ax1.set_xlabel("Udane próby [%]")
    tytul(ax1, 0)
    slupki(ax2, tabela, "zestaw", "zabudowa_proc")
    ax2.set_xlim(left=0)
    ax2.xaxis.set_major_formatter(PolskaPodzialka())
    ax2.set_xlabel("Zabudowa [%]")
    tytul(ax2, 1)
    return fig


def e7(dane, kolumna_y, etykieta_y, notatki):
    laczenia = uporzadkuj("laczenie_rysowania", dane["laczenie_rysowania"])
    fig, osie = plt.subplots(1, len(laczenia), sharex=True, sharey=True, squeeze=False,
                             figsize=(SZEROKI, 68 * MM), layout="constrained")
    uchwyty, wartosci = {}, []
    for i, (ax, laczenie) in enumerate(zip(osie[0], laczenia)):
        nowe, w = linie_rozmiaru(ax, dane[dane["laczenie_rysowania"] == laczenie], "rysowanie", kolumna_y, notatki)
        uchwyty.update({k: v for k, v in nowe.items() if k not in uchwyty})
        wartosci += w
        os_komorek(ax, dane["komorki"])
        tytul(ax, i, nazwa(laczenie))
    os_wartosci(osie[0][0], "y", wartosci)
    if not czy_log(wartosci):
        osie[0][0].set_ylim(bottom=0)
    osie[0][0].set_ylabel(etykieta_y)
    fig.supxlabel("Liczba komórek siatki", fontsize=9)
    kolejne = [uchwyty[k] for k in uporzadkuj("rysowanie", list(uchwyty))]
    fig.legend(handles=kolejne, loc="outside upper center", ncols=len(kolejne))
    return fig


def e7_wywolania(dane, sciezka, notatki):
    return e7(dane, "wywolania_rysowania", "Wywołania rysowania na klatkę", notatki)


def e7_klatka(dane, sciezka, notatki):
    return e7(dane, "czas_klatki_ms", "Czas klatki [ms]", notatki)


def wyglad_zestawu(zestaw):
    """Zestawy kafelków niebieskie, warianty reguł pomarańczowe; w grupie różne znaczniki."""
    znane = KOLEJNOSC["zestaw"]
    if zestaw not in znane:
        return KOLORY[2], "o"
    i = znane.index(zestaw)
    return KOLORY[i // 4], ZNACZNIKI[i % 4]


def e8_restrykcyjnosc(dane, sciezka, notatki):
    tabela = warianty(dane, ["zestaw"])
    sprawdz_z_aplikacja(sciezka, tabela, ["zestaw"], notatki)
    miary = [("gestosc_regul_proc", "Gęstość par [%]"), ("gestosc_wazona_proc", "Gęstość ważona [%]"),
             ("kappa", r"Współczynnik $\kappa$")]
    fig, osie = plt.subplots(1, 3, sharey=True, figsize=(SZEROKI, 80 * MM), layout="constrained")
    for i, (ax, (kolumna, etykieta)) in enumerate(zip(osie, miary)):
        for _, wiersz in tabela.iterrows():
            kolor, znacznik = wyglad_zestawu(wiersz["zestaw"])
            # Pełny znacznik z białą obwódką, żeby nakładające się punkty były rozróżnialne.
            pusty = bool(wiersz["zdegenerowany"])
            ax.scatter(wiersz[kolumna], wiersz["udane_proby_proc"], marker=znacznik, s=26, zorder=3,
                       facecolors="none" if pusty else kolor, edgecolors=kolor if pusty else "white",
                       linewidths=0.9 if pusty else 0.6)
        ax.xaxis.set_major_formatter(PolskaPodzialka())
        ax.set_xlabel(etykieta)
        tytul(ax, i)
    os_procentow(osie[0], "y")
    osie[0].set_ylabel("Udane próby [%]")
    zestawy = uporzadkuj("zestaw", tabela["zestaw"])
    uchwyty = [Line2D([], [], linestyle="none", marker=wyglad_zestawu(z)[1], color=wyglad_zestawu(z)[0],
                      markersize=5, label=nazwa(z)) for z in zestawy]
    # Legenda w dwóch kolumnach: zestawy kafelków i warianty reguł (matplotlib wypełnia ją kolumnami).
    lewa = [u for u, z in zip(uchwyty, zestawy) if wyglad_zestawu(z)[0] == KOLORY[0]]
    prawa = [u for u, z in zip(uchwyty, zestawy) if wyglad_zestawu(z)[0] != KOLORY[0]]
    if tabela["zdegenerowany"].any():
        prawa.append(Line2D([], [], linestyle="none", marker="o", markerfacecolor="none", color=OS, markersize=5,
                            label="pusty znacznik: mapy zdegenerowane"))
    if not prawa:
        fig.legend(handles=lewa, loc="outside lower center", ncols=1)
        return fig
    wiersze = max(len(lewa), len(prawa))
    pusty = Line2D([], [], linestyle="none", label=" ")
    lewa += [pusty] * (wiersze - len(lewa))
    prawa += [pusty] * (wiersze - len(prawa))
    fig.legend(handles=lewa + prawa, loc="outside lower center", ncols=2)
    return fig


def e9_zaostrzanie(dane, sciezka, notatki):
    klucze = ["zaostrzenie_proc", "losowanie_zaostrzenia"]
    tabela = warianty(dane, klucze)
    sprawdz_z_aplikacja(sciezka, tabela, klucze, notatki)
    zdegenerowane = int(tabela["zdegenerowany"].sum())
    if zdegenerowane:
        notatki.append(f"{zdegenerowane} z {len(tabela)} losowań reguł z mapami zdegenerowanymi (puste kółka)")
    poziomy = sorted(tabela["zaostrzenie_proc"].unique())
    pozycja = {p: i for i, p in enumerate(poziomy)}
    x = tabela["zaostrzenie_proc"].map(pozycja) + np.random.default_rng(0).uniform(-0.18, 0.18, len(tabela))
    pelne = ~tabela["zdegenerowany"]
    fig, osie = plt.subplots(2, 1, sharex=True, figsize=(WASKI, 105 * MM), layout="constrained")
    for i, (ax, kolumna, etykieta) in enumerate(zip(osie, ["udane_proby_proc", "zabudowa_proc"],
                                                     ["Udane próby [%]", "Zabudowa [%]"])):
        ax.scatter(x[pelne], tabela.loc[pelne, kolumna], s=12, color=KOLORY[0], alpha=0.6, linewidths=0, zorder=3)
        ax.scatter(x[~pelne], tabela.loc[~pelne, kolumna], s=12, facecolors="none", edgecolors=KOLORY[0],
                   linewidths=0.8, zorder=3)
        srodek = tabela.groupby("zaostrzenie_proc")[kolumna].median()
        ax.plot([pozycja[p] for p in srodek.index], srodek.to_numpy(), color=CIEMNY, marker="o", markersize=3,
                linewidth=1.2, zorder=4)
        ax.set_ylabel(etykieta)
        tytul(ax, i)
    os_procentow(osie[0], "y")
    osie[1].set_ylim(bottom=0)
    osie[1].yaxis.set_major_formatter(PolskaPodzialka())
    os_kategorii(osie[1], [liczba(p) for p in poziomy], poziome=False)
    osie[0].grid(axis="x", visible=False)
    osie[1].set_xlabel("Usunięte dozwolone pary sąsiadów [%]")
    uchwyty = [Line2D([], [], linestyle="none", marker="o", color=KOLORY[0], alpha=0.6, markersize=4,
                      label="pojedyncze losowanie"),
               Line2D([], [], color=CIEMNY, marker="o", markersize=3, label="mediana losowań")]
    if zdegenerowane:
        uchwyty.append(Line2D([], [], linestyle="none", marker="o", markerfacecolor="none", color=KOLORY[0],
                              markersize=4, label="mapy zdegenerowane"))
    fig.legend(handles=uchwyty, loc="outside upper center", ncols=len(uchwyty))
    return fig


# (eksperyment, nazwa pliku z Praca/klocki/rysunki.tex, funkcja)
WYKRESY = [
    ("E1", "e1-czas", e1_czas),
    ("E2", "e2-czas-udane", e2_czas_udane),
    ("E3", "e3-czas-rozmiar", e3_czas_rozmiar),
    ("E4", "e4-czas-log", e4_czas_log),
    ("E4", "e4-pamiec-log", e4_pamiec_log),
    ("E4", "propagacja-udzial", propagacja_udzial),
    ("E5", "e5-czas-stany", e5_czas_stany),
    ("E6", "e6-udane-zabudowa", e6_udane_zabudowa),
    ("E7", "e7-wywolania", e7_wywolania),
    ("E7", "e7-klatka", e7_klatka),
    ("E8", "e8-restrykcyjnosc", e8_restrykcyjnosc),
    ("E9", "e9-zaostrzanie", e9_zaostrzanie),
]


def krotko(sciezka):
    sciezka = os.path.abspath(sciezka)
    return os.path.relpath(sciezka, FOLDER_PROJEKTU) if sciezka.startswith(FOLDER_PROJEKTU + os.sep) else sciezka


def main():
    # Polskie znaki także wtedy, gdy wynik idzie do pliku albo potoku (w Windows domyślnie strona kodowa 1250).
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description="Wykresy do pracy z wyników badań generatora zamków WFC.")
    parser.add_argument("--wyniki", default=os.path.join(FOLDER_PROJEKTU, "Wyniki"),
                        help="folder z plikami CSV z aplikacji (domyślnie Wyniki)")
    parser.add_argument("--wyjscie", default=os.path.join(FOLDER_PROJEKTU, "Praca", "figures"),
                        help="folder na wykresy PDF (domyślnie Praca/figures)")
    argumenty = parser.parse_args()
    if not os.path.isdir(argumenty.wyniki):
        sys.exit(f"Nie ma folderu {argumenty.wyniki}.")
    pliki = znajdz_wyniki(argumenty.wyniki)
    if not pliki:
        sys.exit(f"W folderze {argumenty.wyniki} nie ma plików E<numer>_<data>_<godzina>.csv z aplikacji.")
    os.makedirs(argumenty.wyjscie, exist_ok=True)
    ustaw_styl()

    dane = {}
    for eksperyment, sciezka in sorted(pliki.items(), key=lambda para: int(para[0][1:])):
        dane[eksperyment] = wczytaj(sciezka)
        print(f"{eksperyment}: {krotko(sciezka)}, {len(dane[eksperyment])} map")
    print()

    zapisane, bledy = 0, []
    for eksperyment, plik, funkcja in WYKRESY:
        print(plik)
        if eksperyment not in dane:
            print(f"  pominięty: nie ma wyników {eksperyment}")
            continue
        notatki = []
        try:
            fig = funkcja(dane[eksperyment], pliki[eksperyment], notatki)
        except KeyError as blad:
            bledy.append(plik)
            kolumna = str(blad.args[0] if blad.args else blad).removeprefix("Column not found: ")
            print(f"  błąd: w pliku nie ma kolumny {kolumna} (plik ze starszej wersji aplikacji?)")
            plt.close("all")
            continue
        except ValueError as blad:
            bledy.append(plik)
            print(f"  błąd: {blad}")
            plt.close("all")
            continue
        for notatka in notatki:
            print(f"  {notatka}")
        sciezka = os.path.join(argumenty.wyjscie, plik + ".pdf")
        fig.savefig(sciezka, metadata={"CreationDate": None})
        plt.close(fig)
        zapisane += 1
        print(f"  zapisano {krotko(sciezka)}")

    print(f"\nGotowe: {zapisane} z {len(WYKRESY)} wykresów." + (f" Błędy: {', '.join(bledy)}." if bledy else ""))
    return 1 if bledy else 0


if __name__ == "__main__":
    sys.exit(main())
