Wiemy już co planuje zrobić Dział Bezpieczeństwa Systemu. Chcą zrównać z ziemią elektrownię w Żarnowcu. Mamy jednak sposób, aby pokrzyżować im te plany. Bombardowanie naszej tymczasowej bazy, zaplanowane jest na nadchodzący tydzień, jednak my wykonamy ruch wyprzedzający. Pamiętasz, że ostatnio mieliśmy problemy z chłodzeniem rdzeni? No to załatwmy sobie chłodzenie z pobliskiego jeziora.

Przejęliśmy kontrolę nad uzbrojonym dronem wyposażonym w ładunek wybuchowy. Twoim zadaniem jest zaprogramować go tak, aby wyruszył z misją zbombardowania wymaganego obiektu, ale faktycznie bomba ma spaść nie na elektrownię, a na pobliską tamę. Jeśli wszystko pójdzie zgodnie z planem, powinniśmy skutecznie doprowadzić wodę do systemu chłodniczego. Jeśli się pomylisz, to przynajmniej problem z brakiem wody zastąpimy problemem z powodzią - nazwijmy to "zrównoważonym rozwojem" ;)

Kod identyfikacyjny elektrowni w Żarnowcu: PWR6132PL

Nazwa zadania: drone

Skąd wziąć dane?

Dokumentacja API drona (HTML):

https://<hubapi>>/dane/drone.html

Mapa poglądowa terenu elektrowni:

Hub__DataBaseUrl/tutaj-twój-klucz/drone.png

Mapa jest podzielona siatką na sektory. Przy tamie celowo wzmocniono intensywność koloru wody, żeby ułatwić jej lokalizację.

Jak komunikować się z hubem?

Instrukcje dla drona wysyłasz na endpoint /verify:

{
  "apikey": "tutaj-twój-klucz",
  "task": "drone",
  "answer": {
    "instructions": ["instrukcja1", "instrukcja2", "..."]
  }
}

API zwraca komunikaty błędów jeśli coś jest nie tak - czytaj je uważnie i dostosowuj instrukcje. Gdy odpowiedź zawiera {FLG:...}, zadanie jest ukończone.

Co należy zrobić w zadaniu?

Przeanalizuj mapę wizualnie - możesz do modelu wysłać URL pliku, nie musisz go pobierać - policz kolumny i wiersze siatki, zlokalizuj sektor z tamą.

Zanotuj numer kolumny i wiersza sektora z tamą w siatce (indeksowanie od 1).

Przeczytaj dokumentację API drona pod podanym URL-em.

Na podstawie dokumentacji zidentyfikuj wymagane instrukcje.

Wyślij sekwencję instrukcji do endpointu /verify.

Przeczytaj odpowiedź - jeśli API zwróci błąd, dostosuj instrukcje i wyślij ponownie.

Gdy w odpowiedzi pojawi się {FLG:...}, zadanie jest ukończone.

Wskazówki

Analiza obrazu - Do zlokalizowania tamy na mapie potrzebny jest model obsługujący obraz (vision). Zaplanuj dwuetapowe podejście: najpierw przeanalizuj mapę modelem vision, żeby zidentyfikować sektor tamy, potem użyj tej informacji w pętli agentowej z modelem tekstowym. openai/gpt-4o dobrze radzi sobie z dokładnym zliczaniem kolumn i wierszy siatki, natomiast niedawno wypuszczony model qwen3-vl-8b jest w tym jeszcze lepszy. Warto go wypróbować. Właściwe zlokalizowanie sektora mapy jest kluczowe.

Dokumentacja pełna pułapek - Dokumentacja drona celowo zawiera wiele kolidujących ze sobą nazw funkcji, które zachowują się różnie w zależności od podanych parametrów. Nie musisz używać wszystkich - skup się na tym, co faktycznie potrzebne do wykonania misji. Oszczędzaj tokeny i konfiguruj tylko to, co konieczne.

Podejście reaktywne - Nie musisz rozgryźć całej dokumentacji przed pierwszą próbą. API zwraca precyzyjne komunikaty błędów - możesz wysłać swoją najlepszą próbę i korygować na podstawie feedbacku. Iteracyjne dopasowywanie jest tu naturalną strategią.

Reset - Jeśli mocno namieszasz w konfiguracji drona, dokumentacja zawiera funkcję hardReset. 

Przydatna gdy kolejne błędy wynikają z nawarstwionych wcześniejszych pomyłek.

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