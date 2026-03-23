Numerze piąty!

Znowu mi zaimponowałeś. Mamy już wodę. Powiedziałbym nawet, że przesadnie dużo wody, bo nasi technicy złożyli już zapotrzebowanie na kalosze. Ale lepiej w tę stronę.

Jak wiesz z logów, które ostatnio analizowałeś, mamy jeszcze kilka problemów do rozwiązania i niestety nie możemy zająć się wszystkimi jednocześnie. Teraz skupimy się na problemie firmware'u. Cały czas zgłasza błędy, produkuje dziwne zapisy z czujników, a do tego chyba nasz operator, który komentuje te wszystkie checki, nie robi tego zbyt rzetelnie.

Mamy prawie 10 tysięcy odczytów z różnych sensorów: czujniki wody, temperatury, napięcia, ciśnienia i jeszcze jakieś mieszane. Ja się na tym nie znam, ale wiem jedno: część z tych danych jest po prostu błędna, a Ty musisz powiedzieć, które to są.

Dzięki Twojej pracy namierzymy uszkodzone sensory, a jednocześnie będziemy mieć dowód na to, że operator po prostu się obija i czasami wpisuje nieprawdziwe informacje do notatek tylko po to, aby wyłączyć alarm.

Gdy namierzymy, co działa niepoprawnie, będziemy mogli wymienić te podzespoły na nowe. Co prawda nie posiadamy żadnych części zamiennych, ale wiesz, że jesteśmy dobrzy w kombinowaniu. Poradzimy sobie.

Więcej szczegółów jak i dane z czujników przesłałem Ci wraz z tym nagraniem."

Zadanie

Twoim zadaniem jest znalezienie anomalii w odczytach sensorów.

Czujniki w naszej elektrowni potrafią mierzyć różne wartości. Czasami są to odczyty temperatury, ciśnienia, napięcia i kilka innych. Czujniki bywają jedno- albo wielozadaniowe. Wszystkie jednak zwracają dane w dokładnie takim samym formacie, co oznacza, że jeśli sprawdzasz dane z czujnika temperatury, to znajdziesz tam poza temperaturą także np. zapis napięcia, ale będzie on równy zero, ponieważ nie jest to wartość, którą ten czujnik powinien zwracać. Przy czujnikach zintegrowanych (2-3 zadaniowe), sensor może zwracać wszystkie pola definiowane przez sensory składowe.

Każdy odczyt czujnika jest też skomentowany przez operatora — czasami jednym słowem, a czasami jakąś dłuższą wypowiedzią. Niestety nie zawsze te notatki są poprawnie wpisywane. Pojawia się niekiedy błąd ludzki, a czasami to nierzetelność operatora.

Musisz zgłosić nam wszelkie anomalie. Prześlij nam identyfikatory plików, które zawierają przekłamane dane z czujników lub niepoprawną notatkę operatora.

Nazwa zadania to: evaluation

Odpowiedź wysyłasz do Centrali do /verify w formacie jak poniżej:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "evaluation",
  "answer": {
    "recheck": ["0001","0002","0003", "..."]
  }
}

Dane z sensorów pobierzesz tutaj: https://<ApiUrl>>/dane/sensors.zip

Dane wysyłasz do centrali jako tablicę JSON (jak wyżej) zawierającą identyfikatory.

Akceptujemy poniższe formaty danych:


stringi z identyfikatorem liczbowym — ["0001", "0002","4321"]

liczby bez zera wiodącego — [1, 2, 987]

nazwy plików z błędami (pełne z zerami) — ["0001.json","0002.json","4321.json"]

dane mieszane — ["0001.json",2,"4321"]

Każdy czujnik zwraca dane w poniższym formacie:

{
  "sensor_type": "temperature/voltage",
  "timestamp": 1774064280,
  "temperature_K": 612,
  "pressure_bar": 0,
  "water_level_meters": 0,
  "voltage_supply_v": 230.4,
  "humidity_percent": 0,
  "operator_notes": "Readings look stable and within expected range."
}

Format danych w pojedynczym pliku JSON:

sensor_type — nazwa aktywnego sensora lub zestawu sensorów rozdzielonych znakiem /, np. temperature, water, voltage/temperature

timestamp — unixowy znacznik czasu

temperature_K — odczyt temperatury w Kelwinach

pressure_bar — odczyt ciśnienia w barach

water_level_meters — odczyt poziomu wody w metrach

voltage_supply_v — odczyt napięcia zasilania w V

humidity_percent — odczyt wilgotności w procentach

operator_notes — notatka operatora po angielsku

W każdym pliku obecne są wszystkie pola pomiarowe. Dla sensorów nieaktywnych wartość powinna być ustawiona na 0.

Zakres poprawnych wartości dla aktywnych sensorów:

temperature_K: od 553 do 873

pressure_bar: od 60 do 160

water_level_meters: od 5.0 do 15.0

voltage_supply_v: od 229.0 do 231.0

humidity_percent: od 40.0 do 80.0

Zadanie zostaje zaliczone, gdy prześlesz w jednym zapytaniu identyfikatory wszystkich plików zawierających anomalie.

Jako anomalie definiujemy:

dane pomiarowe nie mieszczą się w normach

operator twierdzi, że wszystko jest OK, ale dane są niepoprawne

operator twierdzi, że znalazł błędy, ale dane są OK

czujnik zwraca dane, których nie powinien zwracać (np. czujnik poziomu wody zwraca napięcie prądu)

Wskazówki

Tam jest 10 000 plików JSON do analizy. Próba wrzucenia tego do LLM-a będzie DROGA. W tych danych mnóstwo informacji się powtarza.

Podpowiedź (spoiler w Base64):

Dwie podpowiedzi:
1) LLM-y mają swój cache, ale Ty także możesz cachować odpowiedzi modelu po swojej stronie. Czy niektóre dane nie są zduplikowane?
2) Czy przeprowadzenie klasyfikacji wszystkich danych przez model językowy będzie optymalne kosztowo? Być może część danych da się odrzucić programistycznie?

Zastanów się, którą część zadania powinien wykonać model językowy, aby nie przepalać zbytecznie tokenów i jak możesz taką weryfikację zoptymalizować pod względem kosztów. Które rodzaje anomalii powinny być wykrywane przez model językowy, a które przez programistyczne podejście?

Kiedy dojdziesz do anomalii, które wymagają analizy przez LLM: czy musisz wysyłać do analizy każdy plik osobno? Przypomnij sobie też cenniki modeli — płaci się więcej za output niż za input. W jaki sposób możesz zminimalizować to, co zwraca model, mimo że wysyłasz do niego dużo danych?

Przyjrzyj się plikom z danymi — technicy czasem są leniwi, i niektóre notatki są bardzo podobne do siebie. Możesz wykorzystać to do zoptymalizowania kosztów.

Chce byś to zadanie wykonał w technologi agenta z Microsoft Agent Framework w c#. Możesz zastosowac tool context7 by dowiedzieć się więcej o tym frameworki. Dodaj tez Aspire.Hosta z telemetria do dobrego observability

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

popatrz na przyklad exam_02_03_task_agent