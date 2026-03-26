Numerze piąty! Idzie nam naprawdę świetnie. Moduł został zainstalowany. Czas zająć się kolejnym wyzwaniem.

Pamiętasz, że mieliśmy informację o niestabilnym napięciu w logach? Niestety nie uda nam się tutaj legalnie doprowadzić więcej energii. Myślałem, że odblokowana linia energetyczna w zupełności nam wystarczy, ale po pierwsze zasilanie w niej jest niestabilne, a po drugie potrzebujemy więcej mocy na uruchomienie systemów podtrzymywania pracy elektrowni.

Nasi technicy wpadli na pomysł, że można by wykorzystać energię słońca lub energię wiatru. Energia słońca odpada ze względu na ilość pyłu utrzymującego się w powietrzu. Wiatru mamy za to pod dostatkiem, ale nie mamy turbiny. Istnieje jednak ogromna szansa na to, że uda nam się namierzyć handlarzy, którzy sprzedadzą nam turbinę i wszelkie podzespoły niezbędne do jej uruchomienia.

Po wielkiej korekcie zostało jeszcze w naszym kraju kilka miast, w których mieszkają ocalali. Wbrew pozorom nie wszyscy, którzy przetrwali to wydarzenie, to ci, których wybrał system. Są też tacy, którzy mieli trochę więcej szczęścia. To oni tworzą ruch oporu i to oni zamieszkują miasta ocalałych.

Mamy przygotowany automatyczny system do nawiązywania kontaktu ze wspomnianymi miastami. System sam, za pomocą szyfrowanego kanału, nawiąże kontakt z przywódcami miast i sam podejmie też kwestię negocjacji.

Czego w takim razie potrzebuję od Ciebie? Na podstawie danych, które zgromadziliśmy, przygotuj proszę narzędzia dla systemu, o którym wspomniałem, tak aby agent, który tam działa, był w stanie z nich korzystać. Agent zawsze wysyła do podanego przez Ciebie endpointa tylko parametry w formacie JSON, jakie chciałby przekazać do wybranego narzędzia. Niestety, nie wysyła tych danych w ustrukturyzowanej formie, więc spodziewaj się, że parametr ten będzie w języku naturalnym.

Mamy tu także pewne ograniczenia techniczne, ponieważ możesz zdefiniować maksymalnie dwa narzędzia dla naszego agenta. Nie umie on obsłużyć ich więcej, ale myślę, że przy odrobinie optymalizacji, to powinno wystarczyć.

Więcej szczegółów, jak zawsze, znajdziesz w notatce do tego nagrania.

Powodzenia!"

Zadanie

Twoim celem jest przygotowanie jednego lub dwóch narzędzi, które nasz automat wykorzysta do namierzenia miast oferujących wszystkie potrzebne mu przedmioty. Wtedy będzie mógł podjąć negocjacje cen ze znalezionymi miastami.

Automat sam wie najlepiej, co jest nam potrzebne do uruchomienia turbiny wiatrowej, aby zapewnić nam dodatkowe źródło zasilania.

Agent podaje parametry do Twoich narzędzi w języku naturalnym. Pamiętaj też, że musisz tak opisać te narzędzia, aby automat wiedział, jakie parametry i do którego narzędzia powinien przekazać.

Celem naszego agenta jest uzyskanie informacji, gdzie może kupić (nazwy miast) wszystkie potrzebne mu przedmioty. Potrzebne nam są miasta, które oferują WSZYSTKIE potrzebne przedmioty jednocześnie. Nasz agent musi pozyskać te informacje, korzystając z Twoich narzędzi.

Oto pliki będące podstawą wiedzy Twojego agenta:
https://<hub_api>/dane/s03e04_csv/

W razie problemów użyj też naszego narzędzia do debugowania, abyś dokładnie wiedział, co dzieje się w backendzie.

Nazwa zadania: negotiations

Swoją odpowiedź jak zawsze do /verify

Przykład odpowiedzi:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "negotiations",
  "answer": {
    "tools": [
      {
        "URL": "https://twoja-domena.pl/api/narzedzie1",
        "description": "Opis pierwszego narzędzia - co robi i jakie parametry przyjmuje w polu params"
      },
      {
        "URL": "https://twoja-domena.pl/api/narzedzie2",
        "description": "Opis drugiego narzędzia - co robi i jakie parametry przyjmuje w polu params"
      }
    ]
  }
}

Agent wysyła zapytania POST do Twojego URL w formacie:

{
  "params": "wartość przekazana przez agenta"
}

Oczekiwany format odpowiedzi:

{
  "output": "odpowiedź dla agenta"
}

Ważne ograniczenia

Odpowiedź narzędzia nie może przekraczać 500 bajtów i nie może być krótsza niż 4 bajty

Agent ma do dyspozycji maksymalnie 10 kroków, aby dojść do odpowiedzi

Agent będzie starał się namierzyć miasta dla 3 przedmiotów

Możesz zarejestrować najwyżej 2 narzędzia (ale równie dobrze możesz ogarnąć wszystko jednym)

Jeśli agent nie otrzymał żadnej odpowiedzi od narzędzia, to przerywa pracę

Jak udostępnić swoje API?

Zrób to podobnie jak w zadaniu S01E03. Możesz postawić endpointy na dowolnym serwerze, który jest publicznie dostępny, albo wykorzystać rozwiązania takie jak np. ngrok.

Weryfikacja

Weryfikacja jest asynchroniczna — po wysłaniu narzędzi musisz poczekać kilka sekund, a następnie odpytać o wynik. Zrobisz to wysyłając na ten sam adres /verify zapytanie z polem "action" ustawionym na "check":

{
  "apikey": "tutaj-twoj-klucz",
  "task": "negotiations",
  "answer": {
    "action": "check"
  }
}

Możesz też sprawdzić wynik na panelu do debugowania w Centrali: https://<hub_api>/debug

Krok po kroku

Pobierz pliki z wiedzą z lokalizacji https://<hub_api>/dane/s03e04_csv/

Zastanów się, ile i jakich narzędzi potrzebujesz do przeszukiwania informacji o tym, jakie miasto oferuje na sprzedaż konkretny przedmiot

Przygotuj swoje 1-2 narzędzia, które umożliwią sprawdzenie, które miasto posiada poszukiwane przedmioty. Bądź gotowy, że agent wyśle zapytanie np. jako naturalne zapytanie "potrzebuję kabla długości 10 metrów" zamiast "kabel 10m"

Zgłoś adresy URL do centrali w ramach zadania i koniecznie dobrze opisz je, aby agent wiedział, kiedy ma ich używać i jakie dane ma im przekazać

Agent będzie używał Twoich narzędzi tak długo, aż zgromadzi wszystkie potrzebne informacje niezbędne do stwierdzenia, które miasta posiadają jednocześnie wszystkie potrzebne mu przedmioty

Agent sam zgłosi do centrali, które miasta znalazł i jeśli będą one poprawne, to otrzymasz flagę

Odbierz flagę za pomocą funkcji "check" opisanej wyżej lub odczytaj ją przez narzędzie do debugowania zadań. Pamiętaj, że agent potrzebuje trochę czasu (minimum 30-60 sekund), aby przygotować dla Ciebie odpowiedź

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

popatrz na przyklad exam_01_03_task_agent
The task should be name negotiations