Numerze piąty.

Prawdopodobnie zauważyłeś pewną nieścisłość w tym co robimy. Udało nam się przekierować transport do elektrowni w Żarnowcu. Pomogłeś nam także przygotować fałszywe dokumenty niezbędne do realizacji tego przewozu. Wszystko idzie jak po maśle, ale... jest jeszcze jedna sprawa!

Pewnie rzuciło Ci się w oczy, że trasa, którą ma przejechać nasz transport, jest "zamknięta". To nie jest fizyczne zamknięcie. To tylko status w systemie. Jest ona w pełni przejezdna, po prostu jeszcze nie oddali jej do użytku po przebudowie.

Musisz przejąć kontrolę nad systemem do sterowania trasami kolejowymi i oznaczyć tę trasę jako otwartą. Jeśli operatorzy systemu zauważą, że pociąg został wysłany na zamkniętą trasę, to z pewnością podniosą alarm, a tego byśmy nie chcieli.

W opisie do tego filmu znajdziesz wszelkie informacje, które pozyskaliśmy na temat tego systemu, ale powiem szczerze, że nie ma tego wiele. Czeka Cię samodzielna analiza, ale już z trudniejszymi zadaniami nam pomagałeś, więc i tym razem wierzę w Ciebie.

Postaraj się proszę ogarnąć tę sprawę jeszcze przed weekendem, aby na poniedziałek kasety z paliwem były już u nas w elektrowni. Nasi ludzie czekają na nie z niecierpliwością.

Powodzenia!"

## Zadanie

Musisz **aktywować trasę kolejową o nazwie X-01** za pomocą API, do którego nie mamy dokumentacji. Wiemy tylko, że API obsługuje akcję `help`, która zwraca jego własną dokumentację — od niej należy zacząć.

Nazwa zadania to **railway**. Komunikacja odbywa się przez ten sam endpoint co poprzednie zadania. Wszystkie żądania to **POST** na `Hub__ApiUrl`, body jako raw JSON.

Przykład wywołania akcji `help`:

```json
{
  "apikey": "tutaj-twoj-klucz",
  "task": "railway",
  "answer": {
    "action": "help"
  }
}
```

Niestety, tylko tyle udało nam się dowiedzieć na temat funkcjonowania tego systemu. API jest celowo przeciążone i regularnie zwraca błędy 503 (to nie jest prawdziwa awaria, a symulacja), a do tego ma bardzo restrykcyjne limity zapytań. Zadanie wymaga cierpliwości.

### Krok po kroku

1. **Zacznij od `help`** — wyślij akcję `help` i dokładnie przeczytaj odpowiedź. API jest samo-dokumentujące: odpowiedź opisuje wszystkie dostępne akcje, ich parametry i kolejność wywołań potrzebną do aktywacji trasy.
2. **Postępuj zgodnie z dokumentacją API** — nie zgaduj nazw akcji ani parametrów. Używaj dokładnie tych wartości, które zwróciło `help`.
3. **Obsługuj błędy 503** — jeśli API zwróci 503, poczekaj chwilę i spróbuj ponownie. To celowe zachowanie symulujące przeciążenie serwera, nie prawdziwy błąd.
4. **Pilnuj limitów zapytań** — sprawdzaj nagłówki HTTP każdej odpowiedzi. Nagłówki informują o czasie resetu limitu. Odczekaj do resetu przed kolejnym wywołaniem.
5. **Szukaj flagi w odpowiedzi** — gdy API zwróci w treści odpowiedzi flagę w formacie `{FLG:...}`, zadanie jest ukończone.

### Wskazówki

- **API jest samo-dokumentujące** — nie szukaj dokumentacji gdzie indziej. Odpowiedź na `help` to wszystko, czego potrzebujesz.
- **Czytaj błędy uważnie** — jeśli akcja się nie powiedzie, komunikat błędu zwykle precyzyjnie wskazuje co poszło nie tak (zły parametr, zła kolejność akcji itp.).
- **503 to nie awaria** — błąd 503 jest częścią zadania. Kod musi go obsługiwać automatycznie przez retry z backoffem, inaczej zadanie nie da się ukończyć.
- **Limity zapytań są bardzo restrykcyjne** — to główne utrudnienie zadania. Monitoruj nagłówki po każdym żądaniu i bezwzględnie respektuj limity. Zbyt agresywne odpytywanie spowoduje długie blokady.
- **Wybór modelu ma znaczenie** — przy restrykcyjnych limitach API liczy się każde zapytanie. Modele, które potrzebują więcej kroków do rozwiązania zadania (lub robią niepotrzebne wywołania API), szybciej wyczerpią limit. Warto przetestować różne modele.
- **Loguj każde wywołanie i odpowiedź** — przy zadaniach z limitami i losowymi błędami dobre logowanie to podstawa debugowania.
