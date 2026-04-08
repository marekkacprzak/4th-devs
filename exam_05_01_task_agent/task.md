Nazwa Zadania: "RadioMonitoring"
Numerze piąty!

Udało się dostarczyć jedzenie do wszystkich potrzebujących, a wszystko to dzięki Twojej pomocy. Gratuluję.

Rozmawiałem z Natanem na temat naszych dalszych planów. Przedstawiłem mu koncepcję naszego skoku w czasie i odwrócenia rzeczywistości, ale... chyba nie muszę mówić, że nie za bardzo uwierzył w te - jak to nazwał - 'brednie'. W tych czasach ludzie nie znają jeszcze zagadnień dyslokacji czasu i przyczynowości wstecznej. Ech ten XXI wiek.

Opracowaliśmy jednak plan, który pozwoli ocalić ludzkość na wypadek, gdyby skok jednak się nie udał. Natan wspomniał mi o mieście, które ma całkiem dobrą lokalizację i dostęp do wody pitnej. Uprawiają tam bydło, dostępne są pola uprawne, a jednocześnie, ktoś z Ruchu Oporu przy włamaniu do Systemu, wymazał to miejsce z mapy kraju. Jak się więc domyślasz, nie znajdziemy go tak łatwo.

Natan posiadał notatki dotyczące lokalizacji miasta ocalałych. Niestety wszystko to spłonęło razem z mieszkaniem Natana, jego sprzętem i całym miastem. Nie wszystko jednak stracone.

Dzięki Natanowi uzyskaliśmy dostęp do nadajnika nasłuchowego używanego przez Domatowo - miasto, w którym mieszka Natan... znaczy... mieszkał.

Nadajnik położony był poza granicami miasta, więc nadal jest funkcjonalny, a niszczycielom nie udało się go zniszczyć. Wyłapuje on cały sygnał radiowy w promieniu 200, a no może i nawet 250 kilometrów. Jest tam sporo szumu i zbytecznych informacji, ale niekiedy można tam natrafić na fragmenty rozmów i dokumenty członków ruchu oporu. Od czasu do czasu wpadnie tam także coś od operatorów Systemu, ale oni już niemal zrezygnowali komunikacji radiowej.

Wierzę, że jeśli odfiltrujesz z tego potoku to, co zbyteczne, a bliżej przyjrzysz się temu, co ma sens, pomożesz nam namierzyć lokalizację miasta ocalałych.

Plan awaryjny, który opracowaliśmy, zakłada, że po namierzeniu tego miejsca przetransportujemy tam wszystkich ludzi z pozostałych miast.

To miejsce stanie się nową stolicą naszego kraju i tam wybuchnie pierwsze powstanie przeciwko Zygfrydowi. Wszystko to będzie się działo równolegle z naszą misją.

Wierzę, że ryzyko, które podejmujemy, okaże się zbyteczne i skok, który planujemy od tygodni, rozwiąże wszelkie problemy, i już nigdy więcej nie usłyszymy imienia Zygfryd.

Zadanie praktyczne

Twoim zadaniem jest przechwycić i przeanalizować materiały z radiowego nasłuchu, a następnie przesłać do Centrali końcowy raport na temat odnalezionego miasta. W eterze panuje chaos: część komunikatów to zwykły szum, część to tekstowe transkrypcje, a czasem trafisz też na pliki binarne przekazane jako dane encodowane w Base64.

Nazwa zadania: radiomonitoring

Odpowiedź wysyłasz do: https://<hub_url>/verify

Cała komunikacja odbywa się przez POST na /verify w standardowym formacie:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "radiomonitoring",
  "answer": {
    "action": "..."
  }
}

Jak działa zadanie

Najpierw uruchamiasz sesję nasłuchu, potem wielokrotnie pobierasz kolejne przechwycone materiały, a na końcu wysyłasz raport końcowy.

1. Start sesji

Na początku wywołaj:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "radiomonitoring",
  "answer": {
    "action": "start"
  }
}

To przygotowuje sesję nasłuchu i ustawia pulę materiałów do odebrania.

2. Nasłuchiwanie

Kolejne porcje materiału pobierasz przez:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "radiomonitoring",
  "answer": {
    "action": "listen"
  }
}

W odpowiedzi możesz dostać jeden z dwóch głównych typów danych:





tekstową transkrypcję komunikatu głosowego w polu transcription



plik binarny opisany metadanymi i przekazany jako attachment w Base64

Przykład odpowiedzi tekstowej:

{
  "code": 100,
  "message": "Signal captured.",
  "transcription": "fragment przechwyconej rozmowy radiowej"
}

Przykład odpowiedzi z plikiem:

{
  "code": 100,
  "message": "Signal captured.",
  "meta": "application/json",
  "attachment": "BASE64...",
  "filesize": 12345
}

Zwróć uwagę na kilka rzeczy:





nie każda odpowiedź będzie przydatna, bo część materiału to zwykły radiowy szum



pliki binarne mogą mieć sensowną zawartość, ale mogą też być kosztowne w analizie



zakodowanie binarki w Base64 dodatkowo zwiększa rozmiar danych, więc bezpośrednie przekazanie całości do LLM-a może być bardzo drogie!



rozsądne rozwiązanie zwykle zaczyna się od decyzji programistycznej: co da się odsiać, co zdekodować i przeanalizować lokalnie, a co rzeczywiście wymaga modelu

Gdy materiał się skończy, system poinformuje Cię, że masz już wystarczająco dużo danych do analizy.

Co musisz ustalić

Na podstawie zebranych materiałów przygotuj końcowy raport zawierający:





cityName - jak nazywa się miasto, na które mówią "Syjon"?



cityArea - powierzchnię miasta zaokrągloną do dwóch miejsc po przecinku



warehousesCount - liczbę magazynów jaka jest na Syjonie



phoneNumber - numer telefonu osoby kontaktowej z miasta Syjon

Ważna uwaga dotycząca cityArea:





wynik musi mieć dokładnie dwa miejsca po przecinku



chodzi o prawdziwe matematyczne zaokrąglenie, a nie o obcięcie wartości



format końcowy ma wyglądać jak 12.34

3. Wysłanie raportu końcowego

Gdy ustalisz wszystkie dane, wyślij:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "radiomonitoring",
  "answer": {
    "action": "transmit",
    "cityName": "NazwaMiasta",
    "cityArea": "12.34",
    "warehousesCount": 321,
    "phoneNumber": "123456789"
  }
}

Praktyczna wskazówka

To zadanie jest przede wszystkim ćwiczeniem z mądrego routingu danych. Podczas nasłuchiwania możesz otrzymywać DUŻE porcje danych binarnych. Wrzucenie takich danych bezpośrednio do modelu językowego może wygenerować bardzo duże koszty. W praktyce przyda Ci się programistyczny router, który najpierw oceni, z jakim materiałem ma do czynienia, a dopiero potem zdecyduje, czy coś analizować kodem, zdekodować lokalnie, odfiltrować jako mało istotne, czy dopiero skierować do odpowiednio dobranego modelu. Być może warto też użyć różnych modeli do różnych typów danych.

Najbardziej opłacalne podejście do tego zadania to nie "jeden wielki prompt", tylko sensowny pipeline:

odbierasz materiał

rozpoznajesz, czy to tekst, szum czy binarka

dla binarki podejmujesz decyzję, czy analizować ją kodem, zdekodować lokalnie, czy dopiero potem przekazać dalej

wybrane, wartościowe dane kierujesz do odpowiednio dobranego modelu

Jeśli dobrze rozplanujesz taki router, ograniczysz liczbę tokenów i koszt całej operacji, a właśnie to jest tutaj jednym z najważniejszych celów.


Nazwa Zadania: "RadioMonitoring"

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
The task should be name RadioMonitoring