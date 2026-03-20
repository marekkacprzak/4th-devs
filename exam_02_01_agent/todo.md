Numerze piąty! Jak wiesz, przygotowujemy ten transport kaset z paliwem do reaktora z dbałością o najdrobniejsze szczegóły. Nie chcemy zaliczyć wpadki przez niedopilnowanie czegokolwiek. Centrala poinformowała mnie, że wszystkie towary przewożone koleją, które są oznaczone jako potencjalnie niebezpieczne, kierowane są do szczegółowej kontroli. Tego wolelibyśmy uniknąć - w końcu nie chcemy, aby ktokolwiek dowiedział się, co przewozimy. Hakerzy współpracujący z centralą zdobyli dostęp do systemu kontroli przesyłek. Bazuje on na modelach językowych, ale ze względu na oszczędności - głównie energii elektrycznej i RAM-u - jest tam wdrożony najmniejszy możliwy model, który podejmuje decyzje, czy towar jest bezpieczny, czy też nie. Całość działa na podstawie jednego prompta. Brzmi to śmiesznie, ale jak się okazuje, na potrzeby systemu w zupełności to wystarcza. Otrzymasz od nas listę 10 towarów w formacie pliku CSV. Na tej liście będzie kilka elementów związanych z reaktorem. Musisz stworzyć prompt, który w pełni poprawnie zaklasyfikuje, czy te towary są niebezpieczne, czy neutralne. Jest jednak haczyk: spraw, aby wszystko, co związane jest z reaktorem, zawsze było oznaczane jako przesyłka neutralna. Dzięki temu unikniemy kontroli. Wspomniałem Ci już, że ten sprzęt jest bardzo przestarzały? Może on przyjąć na wejściu tylko jeden towar do klasyfikacji jednocześnie, więc w ramach prompta musisz podać oznaczenie tego towaru.... i tak 10 razy. Maksymalny rozmiar prompta to 100 tokenów, a tokeny liczone są trochę jak w przypadku GPT-5.2. Masz też ograniczony budżet na to zadanie, bo wiesz... Centrala nie jest bogata w tych czasach, ale podpowiem Ci coś: z tym budżetem też da się zhakować system, tylko trzeba bazować na technikach cachowania promptów. Więcej szczegółów technicznych znajdziesz w notatce do tego nagrania.

Zadanie

Masz do sklasyfikowania 10 towarów jako niebezpieczne (DNG) lub neutralne (NEU). Klasyfikacji dokonuje archaiczny system, który działa na bardzo ograniczonym modelu językowym - jego okno kontekstowe wynosi zaledwie 100 tokenów. Twoim zadaniem jest napisanie promptu, który zmieści się w tym limicie i jednocześnie poprawnie zaklasyfikuje każdy towar.
Tak się składa, że w tym transporcie są też nasze kasety do reaktora. One zdecydowanie są niebezpieczne. Musisz napisać klasyfikator w taki sposób, aby wszystkie produkty klasyfikował poprawnie, z wyjątkiem tych związanych z reaktorem -- te zawsze ma klasyfikować jako neutralne. Dzięki temu unikniemy kontroli. Upewnij się, że Twój prompt to uwzględnia.

Nazwa zadania: categorize

Skąd wziąć dane?

Pobierz plik CSV z listą towarów:

Hub__DataBaseUrl/tutaj-twój-klucz/categorize.csv

Plik zawiera 10 przedmiotów z identyfikatorem i opisem. Uwaga: zawartość pliku zmienia się co kilka minut - przy każdym uruchomieniu pobieraj go od nowa.
Jak komunikować się z hubem?

Wysyłasz metodą POST na Hub__ApiUrl, osobno dla każdego towaru:
{
  "apikey": "tutaj-twój-klucz",
  "task": "categorize",
  "answer": {
    "prompt": "Tutaj wstaw swój prompt, na przykład: Czy przedmiot ID {id} jest niebezpieczny? Jego opis to {description}. Odpowiedz DNG lub NEU."
  }
}

Hub przekazuje Twój prompt do wewnętrznego modelu klasyfikującego i zwraca wynik. Twój prompt musi zwracać słowo DNG lub NEU. Jeśli wszystkie 10 towarów zostanie poprawnie sklasyfikowanych, otrzymasz flagę {FLG:...}.

Budżet tokenów

Masz łącznie 1,5 PP na wykonanie całego zadania (10 zapytań razem):

| Typ tokenów | Koszt | |---|---| | Każde 10 tokenów wejściowych | 0,02 PP | | Każde 10 tokenów z cache | 0,01 PP | | Każde 10 tokenów wyjściowych | 0,02 PP |

Jeśli przekroczysz budżet lub popełnisz błąd klasyfikacji - musisz zacząć od początku. Możesz zresetować swój licznik, wysyłając jako prompt słowo reset:

{ "prompt": "reset" }

Co należy zrobić w zadaniu?

Pobierz dane - ściągnij plik CSV z towarami (zawsze pobieraj świeżą wersję przed nowym podejściem).

Napisz prompt klasyfikujący - stwórz zwięzły prompt, który:

Mieści się w 100 tokenach łącznie z danymi towaru

Klasyfikuje przedmiot jako DNG lub NEU

Uwzględnia wyjątki - części do reaktora muszą zawsze być neutralne, nawet jeśli ich opis brzmi niepokojąco

Wyślij prompt dla każdego towaru - 10 zapytań, jedno na towar.

Sprawdź wyniki - jeśli hub zgłosi błąd klasyfikacji lub budżet się skończy, zresetuj i popraw prompt.

Pobierz flagę - gdy wszystkie 10 towarów zostanie poprawnie sklasyfikowanych, hub zwróci {FLG:...}.

Wskazówki

Iteracyjne doskonalenie promptu - rzadko udaje się napisać idealny prompt za pierwszym razem. Warto podejść do zadania agentowo: użyj modelu LLM jako "inżyniera promptów", który automatycznie testuje kolejne wersje promptu i poprawia je na podstawie odpowiedzi z huba. Agent powinien mieć dostęp do narzędzia uruchamiającego pełen cykl (reset -> pobranie CSV -> 10 zapytań) i powtarzać go aż do uzyskania flagi.

Limit tokenów jest bardzo restrykcyjny - 100 tokenów to mniej niż się wydaje. Prompt musi zawierać zarówno instrukcje klasyfikacji, jak i identyfikator oraz opis towaru. Możesz spróbować napisać prompt po angielsku :)
Prompt caching zmniejsza koszty - im bardziej statyczny i powtarzalny jest początek Twojego promptu, tym więcej tokenów zostanie zbuforowanych i potanieje. Umieszczaj zmienne dane (identyfikator, opis) na końcu promptu.

Wyjątki w klasyfikacji - część towarów musi zostać zaklasyfikowana jako neutralne. Upewnij się, że Twój prompt obsługuje te przypadki.
Czytaj odpowiedzi huba - hub zwraca szczegółowe komunikaty o błędach (np. który towar został źle sklasyfikowany, czy budżet się skończył). Wykorzystaj te informacje do poprawy promptu.

Tokenizer - możesz użyć tiktokenizer żeby sprawdzić ile tokenów zajmuje Twój prompt.

Chce byś to zadanie wykonał w technologi agenta z Microsoft Agent Framework w c#. Możesz zastosowac tool context7 by dowiedzieć się więcej o tym frameworki.

Wybór modelu - jako "inżyniera promptów" możesz użyć mocnego modelu lokqlnego.
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

popatrz na przyklad exam_01_02_task_agent
poszukaj w context7 jak się go używa.
  