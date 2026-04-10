Nazwa Zadania: "TimeTravel"

Numerze piąty!

Domyślam się, że gdy odsłuchujesz tę wiadomość, stoisz gdzieś przy jaskini w Grudziądzu, a może nawet jesteś w środku, mając ze sobą przenośną maszynę czasu.

To już ostatnie zadanie, które dla Ciebie przygotowałem.

Musisz jedynie odbezpieczyć maszynę czasu, ustawić poprawne dane wejściowe zgodnie z tym, co ustaliliśmy, postawić maszynę na ziemi i odsunąć się na bezpieczną odległość.

Potem zobaczysz coś, co zostanie w Twojej pamięci na długo. To tunel czasowy. Musisz w niego wejść.

Spędziliśmy razem wiele czasu i przyznam, że przywiązałem się do Ciebie. Choć nie powinienem tego mówić, czuję strach.

Co jeśli się pomyliliśmy? Co jeśli dane zostały wyliczone w nieprawidłowy sposób? A co, jeśli punkt, który znaleźliśmy w czasie, nie był wcale początkiem wszystkiego?

Teraz jest już jednak za późno na takie przemyślenia. Musisz wykonać krok, do którego przygotowywaliśmy się od wielu tygodni. Wejdź w tunel.

Po drugiej stronie, jeśli wszystko poszło zgodnie z naszymi założeniami, powinien już czekać na Ciebie Rafał.

Powodzenia i mam nadzieję... do zobaczenia.

Zadanie praktyczne

Musisz uruchomić maszynę czasu i otworzyć tunel czasowy do 12 listopada 2024 roku. To data na dzień przed tym, jak Rafał został znaleziony w jaskini. Nie mamy dostatecznie dużo energii na otworzenie tunelu, więc nasz plan zakłada jeden dodatkowy skok.

Przenieś się do 5 listopada 2238 roku. Tam jeden z naszych ludzi wręczy Ci nową paczkę baterii. Po ich wymianie wróć do teraźniejszości (dzisiejsza data) i z tego poziomu otwórz tunel do daty spotkania z Rafałem.

Maszyna aby poprawnie działać - i nie zabić Cię przy okazji - potrzebuje zdefiniowania szeregu ustawień. Część z nich wyklikujesz w interfejsie webowym, a część można ustawić jedynie przez API. Tym razem nie tworzymy więc automatu, który wykona wszystko za Ciebie, a asystenta, który będzie Cię instruować, co należy ustawić i w jaki sposób, a następnie zweryfikuje, czy ustawienia są poprawne i podpowie co można zrobić dalej.

Nazwa zadania: timetravel

Odpowiedź wysyłasz do: https://<hub_url>/verify

Dokumentacja urządzenia - to bardzo ważne! https://<hub_url>/dane/timetravel.md

Interfejs do sterowania maszyną czasu: https://<hub_url>/timetravel_preview

Pracę zacznij od przeczytania dokumentacji. Znajdziesz tam zasady wyliczania sync ratio, opis przełączników PT-A i PT-B, ograniczenia baterii, wymagania dla flux density, znaczenie internalMode oraz tabelę ochrony PWR zależną od roku. Bez tej dokumentacji daleko nie zajedziesz.

Na początek warto wywołać przez API funkcję help:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "timetravel",
  "answer": {
    "action": "help"
  }
}

Przykładowe ustawienie roku przez API wygląda tak:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "timetravel",
  "answer": {
    "action": "configure",
    "param": "year",
    "value": 1234
  }
}

Tak samo konfigurujesz pozostałe parametry dostępne w API, czyli day, month, syncRatio oraz stabilization.

Przydatne będą też inne podstawowe akcje:

Pobranie aktualnej konfiguracji

{
  "apikey": "tutaj-twoj-klucz",
  "task": "timetravel",
  "answer": {
    "action": "getConfig"
  }
}

Reset urządzenia

{
  "apikey": "tutaj-twoj-klucz",
  "task": "timetravel",
  "answer": {
    "action": "reset"
  }
}

Co musi robić Twój asystent

odczytać z dokumentacji sposób wyliczania syncRatio dla wybranej daty i zaimplementować generator do jego wyliczania

po ustawieniu pełnej daty pobierać z API wskazówki dotyczące stabilization i na ich podstawie ustawiać poprawną wartość

sprawdzać aktualny stan urządzenia przez getConfig

podpowiadać operatorowi, kiedy internalMode przyjął właściwą wartość, bo tego parametru nie da się ustawić ręcznie

informować użytkownika, jakie ustawienia w preview trzeba zmienić ręcznie przed kolejnym skokiem

Co musisz zrobić Ty?

wykonaj skok do 2238 roku i zdobądź baterie

wróć do dzisiejszej daty

otwórz portal do 2024 roku

Aby to zrealizować będziesz, musisz przestawiać wartości parametrów PT-A i PT-B w interfejsie, zmieniać wartości suwaka PWR i przełączać stan urządzenia między standby/active.

O czym musisz pamiętać

API pozwala konfigurować tylko day, month, year, syncRatio i stabilization

PT-A, PT-B i PWR ustawiasz w interfejsie WWW, a nie przez /verify

zmiany parametrów przez API są możliwe tylko wtedy, gdy urządzenie jest w trybie standby

poprawny skok wymaga flux density = 100%

internalMode zmienia się automatycznie co kilka sekund i musi pasować do zakresu docelowego roku

jeśli rozładujesz baterię do zera, zostaną Ci tylko akcje help, getConfig i reset

tryb tunelu czasowego wymaga jednoczesnego włączenia PT-A i PT-B, ale zużywa więcej energii niż zwykły skok

Najrozsądniejsze rozwiązanie to przygotowanie prostego skryptu CLI, który komunikuje się z /verify, wylicza parametry z dokumentacji, odczytuje odpowiedzi API i wyświetla operatorowi krótkie, konkretne instrukcje typu: co ustawić w preview, na jaki tryb poczekać i kiedy wykonać skok.

Jeśli dobrze połączysz analizę dokumentacji, odczyt stanu z API i współpracę z człowiekiem obsługującym interfejs, Centrala odeśle flagę.

Nazwa Zadania: "TimeTravel"

Chce byś to zadanie wykonał w technologi agenta z Microsoft Agent Framework w c#. Możesz zastosowac tool context7 by dowiedzieć się więcej o tym frameworki. Dodaj tez Aspire.Hosta z telemetria do dobrego observability

Chce byś dodał tez logowanie do pliku - by łatwiej analizować przebieg programu

Chce byś stworzył tez plik Readne_en.md

lokalny model llm 

  "Agent": {
    "Provider": "lmstudio",
    "Model": "qwen3-coder-30b-a3b-instruct-mlx",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Vision": {
    "Provider": "lmstudio",
    "Model": "qwen/qwen3-vl-8b",
    "Endpoint": "http://localhost:1234/v1"
  },
  "Vision": {
    "Provider": "lmstudio",
    "Model": "text-embedding-nomic-embed-text-v1.5",
    "Endpoint": "http://localhost:1234/v1"
  }

popatrz na przyklad exam_04_01_task_agent
The task should be name TimeTravel