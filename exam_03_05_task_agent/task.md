Zadanie "savethem"

Azazel

Numerze piąty...

Nie wiem... no nie wiem... Nie rozumiem nic...

Automat negocjacyjny, który udało nam się zaprogramować, nawiązał kontakt z pierwszym z miast. Podjął negocjacje, wysłał sporo wiadomości i wtedy dowiedzieliśmy się, że czujniki sejsmiczne w tym mieście odnotowały, jak pulsuje ziemia. A chwilę później nie było już niczego.

Kroczące niszczyciele dotarły na miejsce i zmiotły miasto z powierzchni ziemi. Nie zostało nic. Nie przeżył też żaden mieszkaniec. Ponad 140 zabitych osób.

My tu siedzimy, programujemy rozwiązania, staramy się przechytrzyć systemy, ale po drugiej stronie są prawdziwi ludzie. Oni chcą po prostu przeżyć. A to my, nawiązując kontakt z ich miastem, zdradziliśmy ich lokalizację. Jedno z miast ocalałych zostało zniszczone... nie pierwsze już zresztą.

Na szczęście automat nie zdążył jeszcze nawiązać kontaktu z drugim miastem i zdecydowanie nie pozwolimy mu na to, aby podjął próbę takiego kontaktu. Wyślemy do tego drugiego miasta naszego człowieka. Musimy tylko zaplanować jego podróż. Ma on do dyspozycji kilka środków transportu, prowiant na drogę oraz ograniczony zapas paliwa.

Musisz odpowiednio rozplanować jego ruchy. Przekaż proszę do Centrali, którędy ma się udać i jaki pojazd ma wybrać. Gdy uda Ci się to odpowiednio rozplanować, wtedy nasz wysłannik podejmie negocjacje w sprawie części do turbiny wiatrowej.

Wiem, że rodzi się w Tobie bunt po tym, co usłyszałeś. Wiem, że może ogarnąć Cię beznadzieja, bo ludzie nie żyją i jest to w dużym stopniu nasza wina - nie zachowaliśmy należytej ostrożności.

Ale pamiętaj: mamy władzę nad czasem. Wszystko, co się wydarzyło, można cofnąć, ale tylko pod warunkiem, że uda nam się uruchomić maszynę czasu. Jeden skok wyzeruje wszystkie wydarzenia. Więc chociaż jest to wbrew logice, wierzę, że jeszcze pomożemy tym ludziom. Cofniemy czas.

Zadanie praktyczne

Twoim zadaniem jest zbudowanie agenta, który wytyczy optymalną trasę dla naszego posłańca, który podejmie negocjacje w mieście Skolwin. Niewiele wiemy na temat tego, jak wygląda teren, więc z pewnością na początku będziemy musieli zdobyć mapę. Musimy też zdecydować się na konkretny pojazd, którym wyruszymy z bazy. Jest ich do wyboru kilka. Myślę, że bez problemu znajdziesz informacje na ich temat. Każdy pojazd spala paliwo. Im szybciej się porusza, tym więcej paliwa zużywa. Jednocześnie nasz wysłannik potrzebuje prowiantu. Im dłużej trwa podróż, tym więcej będzie wymagał jedzenia. Trzeba więc odpowiednio rozplanować tę drogę w taki sposób, by poruszać się możliwie szybko, ale jednocześnie tak, aby wystarczyło nam jedzenia i paliwa na dotarcie do celu.

Tym razem nie dajemy Ci dostępu do konkretnych narzędzi, a jedynie do wyszukiwarki narzędzi, która pomoże Ci zdobyć informację o pozostałych narzędziach. Używasz jej jak poniżej:

Endpoint: https://<hub>/api/toolsearch

{
  "apikey": "tutaj-twoj-klucz",
  "query": "I need notes about movement rules and terrain"
}

Uwaga: wszystkie narzędzia porozumiewają się tylko w języku angielskim!

Wszystkie znalezione narzędzia obsługuje się identycznie jak toolsearch, czyli wysyła się do nich parametr 'query' oraz własny apikey.

Twoim zadaniem jest wysłać do centrali optymalną trasę podróży dla naszego wysłannika.

Zadanie nazywa się savethem, a dane wysyłasz do /verify

{
  "apikey": "tutaj-twoj-klucz",
  "task": "savethem",
  "answer": ["wehicle_name", "right", "right", "up", "down", "up","..."]
}

Tutaj znajdziesz podgląd trasy, którą pokonuje nasz człowiek:
https://<hub>/savethem_preview.html

Wskazówki

Co wiemy?

wysłannik musi dotrzeć do miasta Skolwin

pozyskane mapy zawsze mają wymiary 10x10 pól i zawierają rzeki, drzewa, kamienie itp.

masz do dyspozycji 10 porcji jedzenia i 10 jednostek paliwa

każdy ruch spala paliwo (no, chyba że idziesz pieszo) oraz jedzenie. Każdy pojazd ma własne parametry spalania zasobów.

im szybciej się poruszasz, tym więcej spalasz paliwa, ale im wolniej idziesz, tym więcej konsumujesz prowiantu. Trzeba to dobrze rozplanować.

w każdej chwili możesz wyjść z wybranego pojazdu i kontynuować podróż pieszo.

narzędzie toolsearch może przyjąć zarówno zapytanie w języku naturalnym, jak i słowa kluczowe

wszystkie narzędzia zwracane przez toolsearch przyjmują parametr "query" i odpowiadają w formacie JSON, zwracając zawsze 3 najlepiej dopasowane do zapytania wyniki (nie zwracają wszystkich wpisów!)

jeśli dotrzesz do pola końcowego, zdobędziesz flagę i zaliczysz zadanie (flaga pojawi się zarówno na podglądzie, w API jak i w debugu do zadań)

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

popatrz na przyklad exam_03_03_task_agent
The task should be name "savethem"!!
Nie zapomnij dodać tez logowania do pliku wszystkich request/response
Dodaj tez readme_en.md