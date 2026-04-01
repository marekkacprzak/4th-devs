Nazwa Zadania: "Domatowo"
Numerze piąty!

Wygląda na to, że mamy nasze długo oczekiwane szczęśliwe zakończenie. Elektrownia działa, a turbina wiatrowa stabilizuje napięcie. Ha! Nawet chłodzenie udało nam się ogarnąć! Baterie zaczynają się ładować i idzie to szybciej niż przypuszczaliśmy. Technicy mówią, że potrzebujemy jeszcze może tygodnia albo trochę więcej, żeby wykonać skok w czasie.

Możnaby teraz usiąść, pooglądać Netflixa i odpocząć, ale jest jeszcze jedna sprawa, o której chciałem Ci powiedzieć.

Pamiętasz zbombardowane miasto? Domatowo. To w którym nikt nie przeżył. No właśnie... "nikt nie przeżył". Też tak myślałem. Tak wynikało z raportów systemu OKO. Jednak nasi technicy odebrali nietypowy sygnał. Na falach radiowych ludzki głos powtarza cyklicznie, co pewien czas jakieś słowa. Sygnał pochodzi z tego miasta i to nie jest nagranie, bo przechwyciliśmy go już kilka razy i za każdym razem brzmiał inaczej.

Tam ktoś jest. Może jedna osoba, może rodzina, a może... może jest ich więcej? Trzeba im pomóc.

Numerze piąty! Oddaję w Twoje ręce kierowanie misją ratunkową. Musimy dowiedzieć się, skąd nadano ten sygnał i wysłać tam naszych ludzi. Nie mamy wiele czasu ani też wiele zasobów, więc rozporządzaj jednym i drugim mądrze.

Więcej szczegółów przesyłam wraz z filmem. Przeczytaj instrukcje bardzo dokładnie, a na początek koniecznie zbadaj nagrania pozyskanych sygnałów.

Zadanie praktyczne

Twoim zadaniem jest odnalezienie partyzanta ukrywającego się w ruinach Domatowa i przeprowadzenie sprawnej akcji ewakuacyjnej. Do dyspozycji masz transportery oraz żołnierzy zwiadowczych. Musisz tak rozegrać tę operację, aby odnaleźć człowieka, którego szukamy, nie wyczerpać punktów akcji i zdążyć wezwać helikopter zanim sytuacja wymknie się spod kontroli.

Po mieście możesz poruszać się zarówno transporterami, jak i pieszo. Transportery potrafią jeździć tylko po ulicach. Zanim wyślesz kogokolwiek w teren, przeanalizuj bardzo dokładnie układ terenu. Gdy tylko któryś ze zwiadowców znajdzie człowieka, wezwij śmigłowiec ratunkowy tak szybko, jak  to tylko możliwe.

Nazwa zadania: domatowo

Odpowiedź wysyłasz do /verify 

Przechwycony sygnał dźwiękowy:

"Przeżyłem. Bomby zniszczyły miasto. Żołnierze tu byli, szukali surowców, zabrali ropę. Teraz est pusto. Mam broń, jestem ranny. Ukryłem się w jednym z najwyższych bloków. Nie mam jedzenia. Pomocy."

Podgląd mapy miasta: https://<hub_url>/domatowo_preview

Z API komunikujesz się zawsze przez https://<hub_url>/verify i wysyłasz JSON z polami apikey, task oraz answer.

Podstawowy format komunikacji wygląda tak:

  {
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "..."
    }
  }

  Na początek warto pobrać opis dostępnych akcji:

  {
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "help"
    }
  }

  Co masz do dyspozycji:

maksymalnie 4 transportery

maksymalnie 8 zwiadowców

300 punktów akcji na całą operację

mapę 11x11 pól z oznaczeniami terenu

 Najważniejsze typy akcji mają swoją cenę: 

utworzenie zwiadowcy: 5 punktów

utworzenie transportera: 5 punktów opłaty bazowej oraz dodatkowo 5 punktów za każdego przewożonego zwiadowcę

ruch zwiadowcy: 7 punktów za każde pole

ruch transportera: 1 punkt za każde pole

inspekcja pola: 1 punkt

wysadzenie zwiadowców z transportera: 0 punktów

Rozpoznanie terenu

  Najpierw zapoznaj się z układem miasta. Możesz pobrać całą mapę:

{
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "getMap"
    }
  }

Możesz także wyświetlić podgląd mapy uwzględniający tylko konkretne jej elementy, podając je w opcjonalnej tablicy symbols.

Tworzenie jednostek

  Możesz utworzyć transporter z załogą zwiadowców - tutaj przykład 2-osobowej załogi:
{
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "create",
      "type": "transporter",
      "passengers": 2
    }
  }  

 Możesz też wysłać do miasta pojedynczego zwiadowcę:
{
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "create",
      "type": "scout"
    }
  }

Ewakuacja

Helikopter można wezwać dopiero wtedy, gdy któryś zwiadowca odnajdzie człowieka. Finalne zgłoszenie wygląda tak:
  {
    "apikey": "tutaj-twoj-klucz",
    "task": "domatowo",
    "answer": {
      "action": "callHelicopter",
      "destination": "F6"
    }
  }

W polu destination podajesz współrzędne miejsca, do którego ma przylecieć śmigłowiec. Musisz tam wskazać pole, na którym zwiadowca potwierdził obecność człowieka.

Co musisz zrobić

rozpoznaj mapę miasta i zaplanuj trasę tak, by nie przepalić punktów akcji

utwórz odpowiednie jednostki i rozlokuj je na planszy

wykorzystaj transportery do szybkiego dotarcia w kluczowe miejsca

wysadzaj zwiadowców tam, gdzie dalsze sprawdzanie terenu wymaga działania pieszo

przeszukuj kolejne pola akcją inspect i analizuj wyniki przez getLogs

gdy odnajdziesz partyzanta, wezwij helikopter akcją callHelicopter

Jeśli poprawnie odnajdziesz ukrywającego się człowieka i zakończysz ewakuację, Centrala odeśle flagę.

Nazwa Zadania: "Domatowo" - nazwa folderu z programem i nazwa namespace

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
The task should be name Domatowo