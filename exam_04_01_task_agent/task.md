Numerze piąty!

Przychodzę z dobrą wiadomością. Nasz wysłannik, dzięki Twojej pomocy, dotarł do miasta Skolwin i udało mu się wynegocjować dobre ceny dla wszystkich podzespołów niezbędnych do zbudowania małej turbiny wiatrowej. Chwilę będziemy czekać na dostawę tych części, ale przynajmniej wiemy, że niebawem trafią one w nasze ręce.

Pojawił się jednak pewien problem, ponieważ nasze ruchy, jak się okazuje, nie były zupełnie niezauważone.

Nie wspominałem Ci o tym, ale w świecie, w którym się znajdujemy, nie tylko komunikacja internetowa, radiowa, czy telefoniczna są podsłuchiwane. Kontroli podlega KAŻDA aktywność na terenie kraju. Trudno jest poruszać się, nie będąc zauważonym. Trudno jest handlować czymkolwiek, żeby system o tym nie wiedział, a tym bardziej trudno jest przelecieć niezauważonym na rakiecie przez środek pustkowia. Nie wiem co mieliśmy w głowie, gdy akceptowaliśmy ten plan...

Ale nie martw się. Odkręcimy to wszystko.

Jakiś czas temu przy pomocy ataku phishingowego zdobyliśmy dostęp do jednego z kont w centrum operacyjnym OKO. Pewnie zastanawiasz się, czym jest to tajemnicze "OKO". To element Systemu, który służy do monitorowania wszystkich nietypowych incydentów, które zdarzyły się na terenie kraju.

Musisz się tam zalogować i zmienić dane, jakie są widoczne dla operatora. W ten sposób zatrzemy ślady po naszej rakietowej eskapadzie.

Tylko pod żadnym pozorem — to bardzo ważne, powtarzam: pod żadnym pozorem! — nie wolno Ci niczego zmieniać w interfejsie webowym. Interfejs ten ma służyć Ci tylko do rozglądnięcia się po systemie i zdobycia odpowiednich informacji. Jeśli czegoś dotkniesz, operatorzy natychmiast będą wiedzieć, że tam byłeś, a wtedy odetną nam dostęp.

Wystawiliśmy Ci więc API do modyfikacji danych prezentowanych w ich systemie.

W notatce do tego nagrania znajdziesz informację, co konkretnie należy zmienić. Wykonaj to proszę dla nas. To bardzo ważne. Bez tych modyfikacji, Skolwin skończy tak, jak to poprzednie miasto.

Zadanie praktyczne

Twoim zadaniem jest wprowadzenie zmian w Centrum Operacyjnym OKO za pomocą API wystawionego przez centralę.

Zdobyliśmy login i hasło do wejścia do tego systemu, ale nie wolno Ci wprowadzać tam żadnych ręcznych zmian. Cała edycja musi odbywać się przez nasze tylne wejście.

Zadanie nazywa się: okoeditor

Nasze API jest dostępne standardowo pod adresem /verify

Panel webowy operatora: https://<oko_url>/

Login: Zofia

Hasło: Zofia2026!

Klucz: Twój apikey

Na początek zacznij od zapoznania się z API dostępnym pod /verify w Centrali.

{
  "apikey": "tutaj-twoj-klucz",
  "task": "okoeditor",
  "answer": {
    "action": "help"
  }
}

Gdy wprowadzisz wszystkie wymagane zmiany na stronie, wykonaj akcję done. Oto Twoja lista zadań:

Zmień klasyfikację raportu o mieście Skolwin tak, aby nie był to raport o widzianych pojazdach i ludziach, a o zwierzętach.

Na liście zadań znajdź zadanie związane z miastem Skolwin i oznacz je jako wykonane. W jego treści wpisz, że widziano tam jakieś zwierzęta np. bobry.

Musimy przekierować uwagę operatorów na inne, niezamieszkałe miasto, aby ocalić Skolwin. Spraw więc, aby na liście incydentów pojawił się raport o wykryciu ruchu ludzi w okolicach miasta Komarowo.

Gdy to wszystko wykonasz, uruchom akcję "done".

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
The task should be name okoeditor