Nazwa Zadania: "windpower"
Numerze piąty!

Mamy już turbinę. Jest gotowa do produkcji prądu, ale jak to zwykle bywa, jest pewien haczyk. Nie udało nam się zdobyć do niej nowego akumulatora zasilającego, więc ten, który mamy działa już na rezerwie. Nie mamy też odpowiedniej ładowarki, a z częściami, którymi dysponujemy, raczej niczego nie wykombinujemy. W zespole brakuje nam MacGyvera, więc pomysł z budową ładowarki ze spiczaczy biurowych także odpada.

Musimy więc ustalić konkretny dzień i konkretną godzinę, kiedy uruchomimy naszą turbinę i zaczniemy produkcję prądu niezbędnego do rozruchu komputerów sterujących. Elektrownia raportuje przez API, jakie ma obecnie niedobory prądu - jest to zmienne w czasie, więc zwróć na to uwagę! Musimy wyliczyć, na podstawie prognozy pogody, kiedy warunki atmosferyczne będą optymalne do wyprodukowania wymaganej mocy.

Musisz znaleźć pierwsze możliwe okno pogodowe, ponieważ zależy nam na czasie. Z tego, co widziałem w prognozie, czekają nas jeszcze spore zawieruchy i jest szansa, że połamią one nasz nowy nabytek.

Istnieje jednak sposób na przetrwanie takich wichur. Wystarczy ustawić łopaty wirnika w taki sposób, aby nie stawiały oporu wiatrowi. To je ocali.

Przeanalizuj proszę prognozę pogody i określ, kiedy konkretnie czekają nas ogromne wichury. Ustaw łopaty wirnika w taki sposób, aby je przetrwały. Następnie znajdź pierwszy moment, kiedy jesteśmy w stanie wyprodukować potrzebną nam moc. Wyślij taką konfigurację do API centrali.

Musisz wiedzieć, że wirnik mniej więcej godzinę po każdej większej wichurze wraca do standardowego ustawienia, więc czasami będzie wymagał on kilkukrotnego włączenia trybu ochronnego.

Mamy też pewien problem, który nazwałbym "walką z czasem". Pamiętasz o umierającej baterii systemu sterowania turbiną? Przez tą baterię jesteśmy w stanie włączyć Ci okno serwisowe do konfiguracji tego urządzenia tylko na kilkadziesiąt sekund, a Ty przez ten czas musisz pobrać wszystkie niezbędne informacje przez nasze API, a następnie zaprogramować harmonogram pracy urządzenia i zgłosić do Centrali, że konfiguracja jest już gotowa.

Jeśli wszystko pójdzie zgodnie z planem, to znaczy, że już w tym tygodniu będziemy gotowi na rozpoczęcie produkcji prądu. Więcej informacji, jak zawsze, znajdziesz w notatce do tego filmu.

Zadanie praktyczne

Twoim zadaniem jest zaprogramowanie harmonogramu pracy turbiny wiatrowej w taki sposób, aby uzyskać moc niezbędną do uruchomienia elektrowni.

Elektrownia nie może pracować przez cały czas, ponieważ jej bateria na to nie pozwoli. Musisz więc uruchomić jej system tylko wtedy, gdy naprawdę będzie wymagany. Jesteś w stanie znaleźć idealny czas poprzez analizę wyników prognozy pogody.

Dostarczone przez nas API dają Ci też informacje na temat stanu samej turbiny oraz na temat wymagań elektrowni. Przygotowanie raportu do każdej z funkcji wymaga czasu. Nie jesteśmy w stanie powiedzieć, ile konkretnie czasu zajmie wykonanie danej funkcji, ale wywołania te są kolejkowane. Później musisz tylko wywołać funkcję do pobierania wygenerowanych raportów.

Każdy wygenerowany raport da się pobrać tylko jednokrotnie. Jeśli uda Ci się wszystko skonfigurować w czasie 40 sekund, to jesteśmy uratowani i możemy przejść do fazy produkcji prądu.

Nazwa zadania: windpower

Odpowiedź wysyłasz do /verify

UWAGA: to zadanie posiada limit czasu (40 sekund), w którym musisz się zmieścić. Liniowe wykonywanie wszystkich akcji nie umożliwi Ci ukończenia zadania.

Z API porozumiewasz się w ten sposób:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "windpower",
  "answer": {
    "action": "..."
  }
}

Sugerujemy od rozpoczęcia:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "windpower",
  "answer": {
    "action": "help"
  }
}

Zanim przystąpisz do konfiguracji turbiny wiatrowej, musisz uruchomić okno serwisowe poprzez wydanie polecenia:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "windpower",
  "answer": {
    "action": "start"
  }
}

Przykładowe wysłanie konfiguracji może wyglądać tak - w godzinie zawsze ustawiaj minuty i sekundy na zera.

{
  "apikey": "tutaj-twoj-klucz",
  "task": "windpower",
  "answer": {
    "action": "config",
    "startDate": "2238-12-31",
    "startHour": "12:00:00",
    "pitchAngle": 0,
    "turbineMode": "idle",
    "unlockCode": "tutaj-podpis-md5-z-unlockCodeGenerator"
  }
}

Możesz także wysłać wiele konfiguracji za jednym razem - inny format danych.

{
  "apikey": "tutaj-twoj-klucz",
  "task": "windpower",
  "answer": {
    "action": "config",
    "configs": {
      "2026-03-24 20:00:00": {
        "pitchAngle": 45,
        "turbineMode": "production",
        "unlockCode": "tutaj-podpis-1"
      },
      "2026-03-24 18:00:00": {
        "pitchAngle": 90,
        "turbineMode": "idle",
        "unlockCode": "tutaj-podpis-2"
      }
    }
  }
}

Co musisz zrobić

Odczytaj z prognozy pogody wszystkie momenty, w których wiatr jest bardzo silny i może zniszczyć łopaty wiatraka. Zabezpiecz wtedy turbinę (odpowiednie nachylenie łopat i odpowiedni tryb pracy).

Wyznacz punkt, w którym możliwe jest wygenerowanie brakującej energii i ustaw tam optymalne nachylenie łopat wirnika i poprawny tryb pracy umożliwiający produkcję prądu.

Każda przesłana do API konfiguracja musi być cyfrowo podpisana. Mamy jednak generator kodów, który takie kody dla Ciebie wygeneruje - unlockCodeGenerator, a wygenerowane kody wyślij razem z konfiguracją.

Zapisz konfigurację przez "config".

Na końcu wyślij akcję o nazwie "done", która sprawdzi, czy Twoja konfiguracja jest poprawna.

Dodatkowe uwagi

Większość funkcji działa asynchronicznie. Najpierw dodajesz zadanie do kolejki, potem odbierasz wynik przez action "getResult". Odpowiedzi przychodzą w losowej kolejności.

Za wichurę uznajesz wiatr powyżej wytrzymałości wiatraka.

Przy wichurze turbina nie powinna stawiać oporu i nie może produkować prądu.

Przed finalnym "done" musisz wykonać test turbiny przez "turbinecheck".

Każdy punkt konfiguracji musi mieć poprawny unlockCode z funkcji "unlockCodeGenerator".

Jeśli konfiguracja będzie poprawna i zmieścisz się w czasie, Centrala odeśle flagę.

Nazwa Zadania: "windpower"

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
The task should be name okoeditor