OK... Czyli głównie chodziło o systemy chłodzenia i firmware. Poradzimy sobie z tym, ale teraz ten temat musimy zostawić na później. Pojawił się inny problem. Od zawsze współpracowaliśmy z ruchem oporu. To oni dostarczali nam informacje, korzystaliśmy z pracy ich rąk, gdy trzeba było wykonać jakąś fizyczną akcję, a w zamian dzieliliśmy się z nimi tym, co sami wiemy. Można powiedzieć, że jesteśmy w całkiem dobrych relacjach. To znaczy: byliśmy. To skomplikowane. Mamy swojego człowieka w ruchu oporu i doniósł nam on, co się tam w środku dzieje. Nastąpiło pewne rozbicie. Nie wiem, w które informacje możemy wierzyć, a które to zwykłe plotki, ale nie brzmi to dobrze. Ruch oporu co do zasady NIE WSPÓŁPRACUJE Z SYSTEMEM - to ważne! Ale gdy już współpracuje, to znaczy, że musi mieć ważny powód... Wygląda na to, że przedstawiciele ruchu zdecydowali się nawiązać współpracę z operatorami Systemu. Może "współpraca" to za duże słowo. Ponoc wysłali pewien donos. Jak się domyślasz, sprawa dotyczy nas. Znaczy mnie, Ciebie, elektrowni i całego zespołu, który tam teraz jest. Obawiam się, że nasz plan jest zagrożony, a może i zagrożone jest życie ludzi, którzy znajdują się tam na miejscu. Członkowie ruchu wiedzą, że System potrzebuje elektryczności, aby kontrolować ludzkość. Krok po kroku sabotują elektrownie, aby tej energii niezbędnej do życia Systemu było coraz mniej, a tymczasem my... uruchamiamy elektrownię. Według ruchu działamy na niekorzyść ludzkości. Podejrzewają nawet, że być może zbrataliśmy się z Zygfrydem. Oczywiście zdania są podzielone i spora część członków ruchu oporu jest po naszej stronie, ale jednak nie wszyscy i to powoduje rozbicie w ich strukturach. Informator przekazał nam dostęp do skrzynki mailowej jednego z operatorów Systemu. Znajdź tam proszę maile od Wiktora z ruchu oporu. To jeden z tych, którzy się wyłamali i zdecydowali na współpracę z Systemem. Wysłał wiadomość z jakiejś anonimowej skrzynki. Dowiedz się, co planują w związku z naszą elektrownią. Jest szansa, że Wiktor nie ograniczy się tylko do jednego maila i nadal będzie informował operatorów o naszych działaniach.

Zadanie

Zdobyliśmy dostęp do skrzynki mailowej jednego z operatorów systemu. Wiemy, że na tę skrzynkę wpadł mail od Wiktora - nie znamy jego nazwiska, ale wiemy, że doniósł na nas. Musimy przeszukać skrzynkę przez API i wyciągnąć trzy informacje:

date - kiedy (format YYYY-MM-DD) dział bezpieczeństwa planuje atak na naszą elektrownię

password - hasło do systemu pracowniczego, które prawdopodobnie nadal znajduje się na tej skrzynce

confirmation_code - kod potwierdzenia z ticketa wysłanego przez dział bezpieczeństwa (format: SEC- + 28 znaków = 32 znaki łącznie)

Skrzynka jest cały czas w użyciu - w trakcie pracy mogą na nią wpływać nowe wiadomości. Musisz to uwzględnić.

Co wiemy na start:

Wiktor wysłał maila z domeny proton.me

API działa jak wyszukiwarka Gmail - obsługuje operatory from:, to:, subject:, OR, AND

Nazwa zadania: mailbox

Jak komunikować się z API?

Skrzynka mailowa dostępna jest przez API zmail:

POST https://hub.ag3nts.org/api/zmail
Content-Type: application/json

Sprawdzenie dostępnych akcji:

{
  "apikey": "tutaj-twój-klucz",
  "action": "help",
  "page": 1
}

Pobranie zawartości inboxa:

{
  "apikey": "tutaj-twój-klucz",
  "action": "getInbox",
  "page": 1
}

Jak wysłać odpowiedź?

Wysyłasz do /verify:

{
  "apikey": "tutaj-twój-klucz",
  "task": "mailbox",
  "answer": {
    "password": "znalezione-hasło",
    "date": "2026-02-28",
    "confirmation_code": "SEC-tu-wpisz-kod"
  }
}

Gdy wszystkie trzy wartości będą poprawne, hub zwróci flagę {FLG:...}.

Co należy zrobić w zadaniu?


Wywołaj akcję help na API zmail, żeby poznać wszystkie dostępne akcje i parametry.

Spraw aby agent korzystał z wyszukiwarki maili - na podstawie opisu zadania może zbudować odpowiednie zapytania.

Pobierz pełną treść znalezionych wiadomości, żeby przeczytać ich zawartość.

Szukaj informacji po kolei - nie musisz znaleźć wszystkich na raz.

Korzystaj z feedbacku huba, żeby wiedzieć, których wartości jeszcze brakuje lub które są błędne.

Kontynuuj przeszukiwanie skrzynki, aż zbierzesz wszystkie trzy wartości i hub zwróci flagę.

Pamiętaj, że skrzynka jest aktywna - jeśli szukasz czegoś i nie możesz znaleźć, spróbuj ponownie, bo nowe wiadomości mogły dopiero wpłynąć.

Wskazówki

Podejście agentowe z Function Calling - to zadanie doskonale nadaje się do pętli agentowej z narzędziami. Agent może mieć do dyspozycji: wyszukiwanie maili, pobieranie treści wiadomości po ID, wysyłanie odpowiedzi do huba i narzędzie do zakończenia pracy. Pętla powinna działać iteracyjnie - szukaj, czytaj, wyciągaj wnioski, szukaj dalej. Można też podejść bardziej ogólnie i pozwolić agentowi po prostu na wywołania API z parametrami które sam ustali na podstawie pomocy.

Dwuetapowe pobieranie danych - API zmail działa w dwóch krokach: najpierw wyszukujesz i dostajesz listę maili z metadanymi (bez treści), a dopiero potem pobierasz pełną treść wybranych wiadomości po ich identyfikatorach. Nie próbuj odgadywać treści na podstawie samego tematu - zawsze pobieraj pełną wiadomość przed wyciąganiem wniosków.



Aktywna skrzynka - skrzynka jest cały czas w użyciu i nowe wiadomości mogą wpływać w trakcie Twojej pracy. Jeśli przeszukałeś całą skrzynkę i nie możesz czegoś znaleźć, warto spróbować ponownie - szukana informacja mogła właśnie dotrzeć. Nie zakładaj od razu, że informacja nie istnieje.

Wybór modelu - do tego zadania wystarczy tańszy model jak lokalny. Zadanie polega na przeszukiwaniu i ekstrakcji faktów, nie na złożonym rozumowaniu.

Operatory wyszukiwania - API obsługuje składnię podobną do Gmail. Możesz łączyć operatory. Możesz zacząć od szerokich zapytań, żeby nie przegapić istotnych maili, a potem zawęzić wyszukiwanie.

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