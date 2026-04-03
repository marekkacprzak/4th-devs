Nazwa Zadania: "foodwarehouse"
Numerze piąty!

Mamy już informacje, które miasto oferuje jaki towar. Wiemy, z kim należy się skontaktować i wiemy także, kto ma jakie potrzeby.

Tak jak wspominałem wczoraj: nasze magazyny może nie świecą pustkami, ale zdecydowanie nie wystarczą do nakarmienia kilku miast. Zygfryd jednak posiada wystarczającą ilość jedzenia, aby wykarmić ich wszystkich. Dlaczego mielibyśmy z tego nie skorzystać?

Mój plan, o którym wspominałem, polega na tym, aby włamać się - nie fizycznie (to byłoby zbyt niebezpieczne!), a w pełni wirtualnie - do systemów zarządczych centralnych magazynów Zygfryda. To tam trzymane są jedzenie, woda i narzędzia.

Twoim celem jest przeprogramowanie systemu dystrybucji w taki sposób, aby niezbędne towary trafiły do potrzebujących. Sami nie jesteśmy w stanie po nie pojechać ani ich zawieźć do konkretnych miast, ale autonomiczne systemy transportujące obecne w magazynach są w stanie dostarczyć to, co trzeba, tam, gdzie trzeba. Wykorzystamy to.

I tak się jakoś dobrze złożyło, że Zygfryd nie śledzi systemem "OKO" swojego własnego sprzętu. Ruchy transporterów zarządzanych przez System nigdy nie podnoszą alarmu.

Mówiłem Ci, że ten plan raczej nie spodoba się Zygfrydowi, ale z drugiej strony... to jest całkiem sprytne no i może odrobinę wredne zarazem, prawda?

Może zastanawiasz się, po co my to wszystko robimy? Przecież i tak planujemy zmienić przyszłość i przeszłość, a w konsekwencji wyzerować linię czasową w której się znajdujemy, więc wszystkie nasze poczynania pójdą na marne.

No właśnie... Ja nie mam w sobie tyle odwagi, aby tak myśleć. Zawsze istnieje ryzyko, że nasza misja się nie powiedzie. Wtedy do skoku nie dojdzie, a ci ludzie... oni po prostu zginą. Nie chcę żyć z poczuciem, że mogłem ich uratować, ale nic nie zrobiłem.

Nawet jeśli za ten tydzień, czy dwa okaże się, że cała misja była bez sensu, to i tak zostanie nam poczucie, że robiliśmy coś dobrego dla dobra ludzi. To mi wystarczy aby działać, a Tobie numerze piąty?

Jeśli jesteś ze mną, to w notatkach zapisałem więcej informacji na temat mojego planu.

Zadanie praktyczne

Musisz uporządkować pracę magazynu żywności i narzędzi tak, aby przygotować jedno poprawne zamówienie, które zaspokoi potrzeby wszystkich wskazanych miast. Do dyspozycji dostajesz gotowe API magazynu, generator podpisów bezpieczeństwa oraz dostęp tylko do odczytu do bazy danych, z której trzeba wyciągnąć dane potrzebne do autoryzacji zamówienia.

To zadanie nie polega na zgadywaniu. Najpierw poznaj strukturę danych, później ustal pełne zapotrzebowanie miast, a na końcu zbuduj jedno zamówienie, którego zawartość będzie zgodna z wymaganiami Centrali.

Nazwa zadania: foodwarehouse

Odpowiedź wysyłasz do: https://<hub_url>/verify

Plik z zapotrzebowaniem miast: https://<hub_url>/dane/food4cities.json

W tym zadaniu rozmawiasz także z bazą danych SQLite. Dostęp do niej jest wyłącznie w trybie odczytu.

  Na początek najlepiej pobrać pomoc API:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "help"

    }

  }

  Jak działa API

  Każde wywołanie wysyłasz do /verify w polu answer jako obiekt z polem tool.

  Najważniejsze narzędzia:

orders - odczyt, tworzenie, uzupełnianie i usuwanie zamówień

signatureGenerator - generowanie podpisu SHA1 na podstawie danych użytkownika z bazy SQLite

database - odczyt danych i schematów z bazy SQLite

reset - przywrócenie początkowego stanu zamówień

done - końcowa weryfikacja rozwiązania

  Jeśli po drodze namieszasz w stanie zadania, użyj reset:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "reset"

    }

  }

  Praca z zamówieniami

  Możesz pobrać listę aktualnych zamówień:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "orders",

      "action": "get"

    }

  }

Nowe zamówienie tworzysz dopiero wtedy, gdy znasz już tytuł, creatorID, kod destination oraz poprawny podpis:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "orders",

      "action": "create",

      "title": "Dostawa dla Torunia",

      "creatorID": 2,

      "destination": "1234",

      "signature": "tutaj-podpis-sha1"

    }

  }

Po utworzeniu zamówienia możesz dopisywać towary pojedynczo:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "orders",

      "action": "append",

      "id": "tutaj-id-zamowienia",

      "name": "woda",

      "items": 120

    }

  }

Możesz też użyć batch mode i dopisać wiele pozycji naraz. To ważne, bo orders.append przyjmuje również obiekt z wieloma towarami:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "orders",

      "action": "append",

      "id": "tutaj-id-zamowienia",

      "items": {

        "chleb": 45,

        "woda": 120,

        "mlotek": 6

      }

    }

  }

Jeżeli dopiszesz do zamówienia towar, który już w nim istnieje, system zwiększy jego ilość zamiast tworzyć duplikat.

Odczyt bazy SQLite

Możesz sprawdzić, jakie tabele znajdują się w bazie:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "database",

      "query": "show tables"

    }

  }

Możesz też wykonywać zapytania select:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "database",

      "query": "select * from tabela"

    }

  }

Co musisz zrobić:

Ustal, które miasta biorą udział w operacji na podstawie pliku food4cities.json

Znajdź odpowiednie wartości dla pola destination dla tych miast

Odczytaj z food4cities.json, jakie towary i ilości są potrzebne w każdym z tych miast

Przygotuj osobne zamówienie dla każdego wymaganego miasta

Każde zamówienie utwórz z poprawnym creatorID, destination i podpisem wygenerowanym na podstawie danych z bazy SQLite

Uzupełnij zamówienia dokładnie tymi towarami, których potrzebują miasta. Bez braków i bez nadmiarów

Gdy wszystko będzie gotowe, wywołaj narzędzie done

  Dodatkowe uwagi:

Musisz utworzyć tyle zamówień, ile mamy miast w pliku JSON

Jeśli coś zepsujesz po drodze, użyj reset, żeby wrócić do stanu początkowego

Każde zamówienie musi mieć poprawny creatorID oraz signature

  Gdy uznasz, że wszystkie wymagane zamówienia są gotowe, wyślij finalne sprawdzenie:

  {

    "apikey": "tutaj-twoj-klucz",

    "task": "foodwarehouse",

    "answer": {

      "tool": "done"

    }

  }

Jeśli komplet zamówień będzie zgodny z potrzebami miast, trafi pod właściwe kody docelowe i zachowa poprawne podpisy, Centrala odeśle flagę.

Nazwa Zadania: "FoodWareHouse"

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
The task should be name FoodWareHouse