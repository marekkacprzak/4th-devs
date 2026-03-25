Dzięki, Numer Piąty, za ogarnięcie problemu z niedziałającym oprogramowaniem. Teraz wszystko działa poprawnie.

Nasi technicy potwierdzili, że testy oprogramowania także przechodzą poprawnie, więc wgraliśmy przygotowany przez Ciebie soft do realnego urządzenia.

Mamy teraz jednak pewien problem, ponieważ urządzenie do sterowania chłodzeniem znajduje się bardzo niebezpiecznie blisko reaktora, a jak wiesz, działał on już przez niemal cały dzień. Nie możemy więc tam wysłać człowieka ze względu na podwyższony poziom radiacji. Aby zainstalować to urządzenie, posłużymy się robotem.

I tutaj potrzebna jest Twoja pomoc, bo kto jak nie Ty mógłby tego robota zaprogramować? Zadanie wydaje się trywialne, bo wystarczy tylko zawieźć moduł sterowania chłodzeniem w pobliże reaktora i umieścić go w specjalnym slocie, ale po drodze znajdują się elementy rdzenia reaktora, które cały czas są w ruchu. Musimy więc tak zaprogramować robota, aby nie dotknął żadnego z tych elementów.

Mamy trochę tych robotów transportujących na stanie, ale proszę Cię, nie nadwyrężaj naszego budżetu i postaraj się to zrobić raz a dobrze. Więcej szczegółów znajdziesz w notatce do tego nagrania."

Zadanie

Twoim zadaniem jest doprowadzenie robota transportującego urządzenie chłodzące w pobliże reaktora.

Do sterowania robotem służy specjalnie przygotowane API, które przyjmuje polecenia: start, reset, left, wait oraz right. Możesz wysłać tylko jedno polecenie jednocześnie.

Zadanie uznajemy za zaliczone, jeśli robot przejdzie przez całą mapę, nie będąc przy tym zgniecionym przez elementy reaktora. Bloczki reaktora poruszają się w górę i w dół, a status ich aktualnego kierunku, podobnie jak ich pozycja są zwracane przez API.

Napisz aplikację, która na podstawie aktualnej sytuacji na planszy będzie decydowała, jakie kroki powinien podjąć robot. Aby uprzyjemnić Ci pracę, przygotowaliśmy też graficzny podgląd sytuacji wewnątrz reaktora.

Podgląd sytuacji w reaktorze: https://<hub_api>/reactor_preview.html

Zadanie nazywa się: reactor

Komendy dla robota wysyłasz do /verify:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "reactor",
  "answer": {
    "command": "start"
  }
}

Mechanika zadania

Plansza ma wymiary 7 na 5 pól.

Robot porusza się zawsze po najniższej kondygnacji, czyli jego pozycja startowa to pierwsza kolumna i 5 wiersz.

Miejsce instalacji modułu chłodzenia (Twój punkt docelowy) to 7 kolumna i 5 wiersz (dobrze widać to na podglądzie graficznym podlinkowanym wyżej).

Każdy blok reaktora zajmuje dokładnie 2 pola i porusza się cyklicznie góra/dół. Gdy dojdzie do pozycji skrajnie wysokiej, zaczyna wracać na dół, a gdy osiągnie pozycję najniższą, wraca do góry.

Bloki poruszają się tylko, gdy wydajesz polecenia. Oznacza to, że odczekanie np. 10 sekund nie zmieni niczego na planszy. Jeśli chcesz, aby stan planszy zmienił się bez poruszania robotem, wyślij komendę wait.

Oznaczenia na mapie

P — to pozycja startowa

G — to pozycja do której masz doprowadzić robota

B — to bloki reaktora

. — to puste pola. Nic się na nich nie znajduje (to kropka)

Jak powinna wyglądać implementacja Twojego algorytmu?

Na początek zawsze wysyłasz polecenie start

Rozglądasz się, jak wygląda plansza i podejmujesz decyzję, czy możesz wykonać krok do przodu

Jeśli nie możesz wykonać kroku lub jest to zbyt niebezpieczne (np. zbliża się bloczek), to czekasz

Jeśli czekanie nie wchodzi w grę (bo w kolumnie, w której stoisz, też zbliża się bloczek), to uciekasz w lewo


Wykonujesz odpowiednie kroki za każdym razem podglądając mapę, tak długo, aż osiągniesz punkt docelowy

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

popatrz na przyklad exam_02_02_task_agent
The task should be name reactor