Nazwa Zadania: "filesystem"

Numerze piąty!

Dzięki Twojej pomocy ocaliliśmy człowieka! Ma na imię Natan. Jego stan jest stabilny, ale musi trochę odpocząć. Gdy tylko wydobrzeje, będzie pomagał nam w pracy przy elektrowni.

Nie mogłem znaleźć jego danych na liście ocalałych. Wygląda na to, że to jeden z ludzi żyjących poza systemem. Rozmawialiśmy z nim długo. Można mu zaufać.

Natan opowiedział nam, jak wyglądało życie w mieście przed bombardowaniem. Główny problem, z którym się borykali, to brak jedzenia. Z wodą akurat nie było problemu, ponieważ udało im się wykopać studnie, ale jedzenie od zawsze było problemem.

Do tej pory stosowali handel wymienny z innymi miastami, ale odkąd Zygfryd uruchomił system "OKO", który nasłuchiwał komunikatów internetowych i radiowych, a także który rejestrował wszelki ruch, handel nie był już tak prosty jak niegdyś, a wysyłanie posłańców na pustynie przypominało misję samobójczą.

Dostaliśmy od Natana jego notatki. On sam ogarniał temat handlu, więc ma informacje na temat osób odpowiedzialnych za handel wymienny z innych miast. Widziałem tam też zapiski dotyczące oferowanych towarów, zamówień, transakcji.

To wszystko jest bardzo chaotyczne i chyba niezbyt kompletne, ale wierzę, że pomożesz nam ułożyć to w jednolitą strukturę.

Chodzi nam po głowie pewien pomysł, jak można tym wszystkim miastom pomóc.

Oczywiście nie mamy tyle jedzenia, aby nakarmić wszystkich potrzebujących, ale możemy zastosować pewną sztuczkę... aleee! o tym opowiem Ci jutro. Na dziś potrzebuję tylko poukładać te dane.

To co robimy zdecydowanie nie spodoba się Zygfrydowi, ale chyba właśnie o to chodzi - prawda?

W notatce do tego nagrania masz więcej szczegółów. Postaraj się ogarnąć te notatki jeszcze dziś!

Zadanie praktyczne

Twoje zadanie polega na logicznym uporządkowaniu notatek Natana w naszym wirtualnym file systemie. Potrzebujemy dowiedzieć się, które miasta brały udział w handlu, jakie osoby odpowiadały za ten handel w konkretnych miastach oraz które towary były przez kogo sprzedawane.

Dokładny opis potrzebnej nam struktury znajdziesz poniżej.

Nazwa zadania to: filesystem

Wszystkie operacje wykonujesz przez /verify/

Link do notatek Natana: https://<hub_url>/dane/natan_notes.zip 

Podgląd utworzonego systemu plików: https://<hub_url>/filesystem_preview.html

Na początek warto wywołać przez API funkcję 'help':

  {
    "apikey": "tutaj-twoj-klucz",
    "task": "filesystem",
    "answer": {
      "action": "help"
    }
  }

W udostępnionym API znajdziesz funkcje do tworzenia plików i katalogów, usuwania ich, listowania katalogów oraz dwie funkcje specjalne:

  - reset - czyści cały filesystem (usuwa wszystkie pliki)

  - done - wysyła utworzoną strukturę danych do Centrali w celu weryfikacji zadania. 

Komunikacja z API

Możesz wysyłać do API pojedyncze instrukcje lub wykonać wiele operacji hurtowo.

Przykładowo, utworzenie 2 plików może wyglądać tak:

Zapytanie 1:

  {
    "apikey": "tutaj-twoj-klucz",
    "task": "filesystem",
    "answer": {
      "action": "createFile",
      "path": "/plik1",
      "content": "Test1"
    }
  }

  Zapytanie 2:

  {
    "apikey": "tutaj-twoj-klucz",
    "task": "filesystem",
    "answer": {
      "action": "createFile",
      "path": "/plik2",
      "content": "Test2"
    }
  }

  Możesz także wykorzystać batch_mode i wysłać wszystko razem - dzięki tej funkcji, możliwe jest utworzenie całego filesystemu w jednym requeście.

 {
    "apikey": "tutaj-twoj-klucz",
    "task": "filesystem",
    "answer": [
      {
        "action": "createFile",
        "path": "/plik1",
        "content": "Test1"
      },
      {
        "action": "createFile",
        "path": "/plik2",
        "content": "Test2"
      }
    ]
  } 

Oto wymagania: 

Potrzebujemy trzech katalogów: /miasta, /osoby oraz /towary

W katalogu /miasta mają znaleźć się pliki o nazwach (w mianowniku) takich jak miasta opisywane przez Natana. W środku tych plików powinna być struktura JSON z towarami, jakie potrzebuje to miasto i ile tego potrzebuje (bez jednostek).

W katalogu /osoby powinny być pliki z notatkami na temat osób, które odpowiadają za handel w miastach. Każdy plik powinien zawierać imię i nazwisko jednej osoby i link (w formacie markdown) do miasta, którym ta osoba zarządza.

Nazwa pliku w /osoby nie ma znaczenia, ale jeśli nazwiesz plik tak jak dana osoba (z podkreśleniem zamiast spacji), a w środku dasz wymagany link, to system też rozpozna, o co chodzi.

W katalogu /towary/ mają znajdować się pliki określające, które przedmioty są wystawione na sprzedaż. We wnętrzu każdego pliku powinien znajdować się link do miasta, które oferuje ten towar. Nazwa towaru to mianownik w liczbie pojedynczej, więc "koparka", a nie "koparki"

Oczekiwany filesystem

Efektem Twojej pracy powinny być takie trzy katalogi wypełnione plikami.

Uwaga: w nazwach plików nie używamy polskich znaków. Podobnie w tekstach w JSON.

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
The task should be name Filesystem