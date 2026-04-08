Nazwa Zadania: "ShellAccess"
Numerze Piąty!

zdobyliśmy dzięki Tobie wszystkie informacje, które są nam potrzebne. Ludzie z ocalałych miast już przygotowują się do wielkiej wędrówki przez pustkowie, aby wspólnie spotkać się w Syjonie. Przejdą bezpieczną drogą, którą im wyznaczyliśmy.

My w międzyczasie zaczniemy przygotowywać się do uruchomienia maszyny czasu, bo baterie są już niemal załadowane. Musimy najpierw jednak odpowiedzieć sobie na pytanie: w które okno czasowe powinniśmy się wstrzelić, aby mieć pewność, że naprawde odwrócimy bieg wydarzeń i przywrócimy porządek światu.

To właśnie po to była nam informacja o tym, na którym serwerze operatorzy systemu przechowują 'wielkie archiwum czasu' opisujące to, co działo się przez ostatnie lata. Archiwum jest naprawdę ogromne, więc musimy je sprytnie przeszukać.

Niestety nie jest to typowa baza danych, a prosty plik tekstowy. Aby ułatwić Ci zadanie, uploadowaliśmy go na jedną z naszych najmocniejszych i najszybszych maszyn, więc możesz spokojnie grzebać w nim za pomocą narzędzi linuksowych. To nie powinno Ci sprawić problemu prawda?

Więcej szczegółów znajdziesz w notatce do tego filmu.

Zadanie praktyczne

Mamy dostęp do serwera, na którym zgromadzone są logi z archiwum czasu. Znajdują się one w katalogu /data. Twoim celem jest namierzenie, którego dnia, w jakim mieście i w jakich współrzędnych musimy się pojawić, aby spotkać się z Rafałem.

Musisz wyszukać datę, kiedy odnaleziono Rafała, i pojawić się w tamtym miejscu dzień wcześniej. Serwer, z którym się łączysz, ma dostęp do standardowych narzędzi linuksowych.

Nazwa zadania: shellaccess

Odpowiedź wysyłasz do: https://<hub_url>/verify

Jak wysyłać polecenia

W polu answer umieszczasz obiekt JSON z polem cmd, w którym wpisujesz komendę powłoki do wykonania.

Przykład - sprawdzenie, co jest w katalogu domowym:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "shellaccess",
  "answer": {
    "cmd": "ls -la"
  }
}

Inny przykład - odczyt konkretnego pliku:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "shellaccess",
  "answer": {
    "cmd": "cat /sciezka/do/pliku"
  }
}

Co musisz zrobić





Eksploruj zawartość serwera komendami powłoki (ls, find, cat itp.)



Przeglądnij co przygotowaliśmy dla Ciebie w katalogu /data/



Wydobądź z plików informacje: kiedy znaleziono ciało Rafała. W jakim mieście się to wydarzyło oraz jakie są współrzędne tego miejsca



Wypisz na ekran (komendami powłoki) plik JSON w formacie jak podany niżej



System sam wykryje, czy dane są prawidłowe i odeśle Ci flagę

Jak zgłosić odpowiedź?

Zadanie uznajemy za zaliczone, gdy uda Ci się wykonać na serwerze takie polecenie, które zwróci potrzebne dane w formacie JSON, takim jak poniżej.

Gdy to się stanie, centrala zwróci Ci flagę.

{
  "date": "2020-01-01",
  "city": "nazwa miasta",
  "longitude": 10.000001,
  "latitude": 12.345678
}

Podpowiedzi

do odczytywania i generowania plików JSON możesz użyć narzędzia 'jq' zainstalowanego na serwerze. Niemal wszystkie potrzebne informacje można uzyskać także za pomocą polecenia 'grep'.

Poprawną odpowiedź możesz wyprodukować przez JSON, albo poskładać samodzielnie i wykonać:

echo '{"date":"2020-01-01","city":"nazwa miasta","longitude":10.000001,"latitude":12.345678}'

UWAGA! Pamiętaj, że musisz zwrócić datę DZIEŃ PRZED znalezieniem ciała Rafała.

Nazwa Zadania: "ShellAccess"

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
The task should be name ShellAccess