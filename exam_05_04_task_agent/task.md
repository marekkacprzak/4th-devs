Nazwa Zadania: "GoingThere"
Azazel

Numerze piąty!

Mamy już namierzony punkt w czasie, w którym to wszystko się zaczęło. Wystarczy tylko się tam pojawić i zapobiec całemu złu, jak i 'Wielkiej korekcie'. Według naszych wyliczeń, moment który nas interesuje, to ten, w którym Rafał spotkał profesora Andrzeja Maja. Jeśli nie dojdzie do tego spotkania, nic z tego, co się zdarzyło później, nie będzie miało miejsca. Świat będzie taki, jak dawniej.

Jest tylko jeszcze jeden problem, o którym Ci nie wspomniałem. Maszyna czasu potrafi otworzyć tunel 'czasowy', ale nie jest to tunel 'czasoprzestrzenny'. Mówiąc prościej, przenosi ona ludzi w dokładnie to samo miejsce, ale w innym czasie.

Jak już wiesz, wydarzenia związane z Rafałem Bombą wydarzyły się w Grudziądzu, a tam jest teraz główna siedziba Zygfryda, jak i serce całego Systemu. Nie jesteśmy w stanie otworzyć portalu w naszej bazie i po prostu zjawić się w Grudziądzu na spotkaniu z Rafałem. Portal musi zostać otworzony nieopodal jaskini, w której niegdyś mieszkał Rafał.

To niebezpieczna misja, ale myślę, że podołasz.

Aaa! no tak... nie wspomniałem o tym, a powinienem chyba powiedzieć Ci o tym na początku: to Ty musisz zjawić się w Grudziądzu i osobiście wykonać skok w czasie.

Rafał uważa mnie za współpracownika Zygfryda, więc nie wiemy, jak zareagowałby na moją obecność tam. Jest szansa, że nawet próbowałby mnie zabić.

Mamy tylko jedną możliwość skoku w czasie i jeśli to nie wyjdzie, to wszystko stracone. Wtedy miasta zaczną realizować 'plan B', a tego chciałbym uniknąć. Dla nas oznaczałoby to porażkę. Ludzie, którzy stracili życie już nie zostaną uratowani, ale być może Zygfryd przynajmiej będzie obalony.

O Tobie Rafał ma jednak znacznie lepsze zdanie. Może nie 'dobre', ale 'lepsze'. Istnieje szansa, że Cię posłucha i zaniecha tego, co planował.

Przeczytaj proszę notatkę, którą otrzymałeś razem z tym filmem i działajmy. 

Zadanie praktyczne

Wyruszasz rakietą naziemną w kierunku Grudziądza. Problem polega na tym, że systemy zakłócające nawigację na całej trasie sprawiają, że nie wiesz, co znajduje się przed tobą - możesz więc uderzyć w skałę. Jedyne, co możesz zrobić, to nasłuchiwać komunikatów radiowych opisujących położenie skał tuż przed Tobą.

Pamiętaj, że system OKO cały czas namierza każdy, nawet najdrobniejszy ruch, jaki wykonujemy na tych odludnych terenach. Jeśli system namierzania wykryje Cię, to wystrzeli pocisk, który zakończy Twoje życie. Mamy jednak dostęp do API, które umożliwia wykrycie, kiedy jesteś namierzany, oraz potrafi zneutralizować sygnał radarowy, dzięki czemu będziesz niewidoczny dla systemu OKO. Musisz jedynie sprawdzać w API przed wykonaniem każdego ruchu, czy znajdujesz się akurat koło radaru i jeśli tak, musisz przeprowadzić procedurę jego deaktywacji.

Uwaga: mechanizmy zagłuszające stosowane przez system OKO mają podwójne działanie. Po pierwsze, dane odbierane ze skanera częstotliwości są bardzo często zniekształcone, czy nawet zepsute. Po drugie, API może losowo zwracać błędy, nawet jeśli Twoje zapytanie jest poprawne. Twój kod musi być odporny zarówno na uszkodzone pakiety danych, jak i na losowe błędy API - w razie błędu po prostu ponów zapytanie.

Nazwa zadania: goingthere

Odpowiedź wysyłasz do https://<hub_url>/verify

Podgląd trasy i stanu gry:

https://<hub_url>/goingthere_preview

Sterowanie rakietą

Rakieta porusza się po siatce o wymiarach 3 wiersze na 12 kolumn. Start jest zawsze w kolumnie 1, w środkowym wierszu (wiersz 2). Baza w Grudziądzu znajduje się w kolumnie 12, w wierszu podanym na starcie. W każdej kolumnie znajduje się dokładnie jedna skała.

Masz do dyspozycji trzy komendy ruchu i jedną komendę startową:

start - rozpoczyna nową grę, generuje nową mapę i resetuje wszystkie ustawienia.

go - leci prosto do przodu (ta sama pozycja w wierszu, następna kolumna)

left - idzie na wyższy wiersz i do przodu (góra + następna kolumna)

right - idzie na niższy wiersz i do przodu (dół + następna kolumna)

Każdy ruch przesuwa rakietę o jedną kolumnę do przodu (także left i right!). Jeśli trafisz w skałę, rakieta rozbija się i musisz zacząć od nowa. Jeśli wypadniesz poza mapę, to także się rozbijasz. Jeśli nie zneutralizujesz systemu radarów, zostaniesz zestrzelony.

Rozpocznij grę:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "goingthere",
  "answer": {
    "command": "start"
  }
}


Po starcie otrzymasz informację o swojej pozycji, pozycji bazy docelowej oraz opis bieżącej kolumny (wolne wiersze i pozycja skały). Możesz wykonać bezpieczny ruch - przykładowo:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "goingthere",
  "answer": {
    "command": "go"
  }
}


Radiowe wskazówki o skałach

Ponieważ nie widzisz trasy przed sobą, możesz poprosić o radiową wskazówkę. Endpoint:

https://<hub_url>/api/getmessage

{
  "apikey": "tutaj-twoj-klucz"
}

W odpowiedzi otrzymasz pole "hint" z komunikatem w języku angielskim. Komunikat opisuje, po której stronie (lewa/prawa/przód) względem rakiety znajduje się skała w następnej kolumnie. Na tej podstawie musisz zdecydować, którą komendę ruchu wysłać, aby nie trafić w skałę. Komunikaty radiowe czasami bywają dziwne i używają języka stosowanego w żegludze.

Skaner częstotliwości i skanery systemu OKO

Na trasie rozmieszczone są skanery systemu OKO - nie wiesz, gdzie się znajdują. Jeśli rakieta znajduje się w kolumnie z aktywną pułapką i spróbujesz wykonać ruch bez jej zneutralizowania, rakieta zostanie zestrzelona.

Aby sprawdzić, czy jesteś namierzany, odpytaj skaner częstotliwości metodą GET:

https://<hub_url>/api/frequencyScanner?key=tutaj-twoj-klucz

Skaner zwróci jedną z dwóch odpowiedzi:

Gdy jest bezpiecznie, odpowiedź zawiera tekst "It's clear!"

Gdy jesteś namierzany, odpowiedź zawiera JSON z kilkoma polami. Przede wszystkim jest tam podana częstotliwość namierzania oraz ciąg znaków niezbędny do wygenerowania kodu unicestwiającego radar.

Pamiętaj: odpowiedzi ze skanera są zniekształcone przez systemy zagłuszające. To, co otrzymasz, może wyglądać jak JSON, ale może nie być zdatne do parsowania.

Neutralizacja pułapki

Gdy skaner wykryje namierzanie, musisz zneutralizować pułapkę, zanim wykonasz kolejny ruch. Wyślij POST na:

https://<hub_url>/api/frequencyScanner

{
  "apikey": "tutaj-twoj-klucz",
  "frequency": 123,
  "disarmHash": "abc123def456..."
}

Gdzie:

frequency to wartość liczbowa z pola "frequency" odczytana z odpowiedzi skanera

disarmHash to hash SHA1 obliczony z wartości pola "detectionCode" z dopisanym na końcu słowem "disarm" (czyli SHA1 z ciągu: detectionCode + "disarm")

Jeśli dane będą poprawne, pułapka zostanie rozbrojona i będziesz mógł bezpiecznie kontynuować lot.

Co musisz zrobić

Wystartuj grę komendą "start" i zanotuj pozycję bazy docelowej.

Na każdym polu najpierw odpytaj skaner częstotliwości przez API frequencyScanner, aby sprawdzić, czy nie jesteś namierzany.

Jeśli jesteś namierzany, sparsuj zniekształconą odpowiedź skanera, wyciągnij z niej "detectionCode" i "frequency", oblicz hash SHA1 i wyślij go do skanera, aby rozbroić pułapkę.

Pobierz wskazówkę radiową z endpointu getmessage, aby dowiedzieć się, gdzie jest skała w następnej kolumnie.

Na podstawie wskazówki wybierz odpowiednią komendę ruchu (go/left/right) i przesuń rakietę. Pamiętaj, że nie wolno Ci także wylecieć poza mapę.

Powtarzaj kroki 2-5 aż dotrzesz do bazy w Grudziądzu.

Gdy rakieta dotrze do Grudziądza, otrzymasz flagę.

Nazwa Zadania: "GoingThere"

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
The task should be name GoingThere