Nazwa Zadania: "Phonecall"

Numerze piąty... nie wiem, czy Ty też tak masz, ale ja, gdy planuję alternatywne rozwiązanie na wypadek, gdy mój plan się nie uda, czuję się, jakbym spisywał testament. Bo co to znaczy, że skok w czasie się nie uda? Że wylądujemy w złym miejscu? Że nie powstrzymamy Zygfryda przed przejęciem kontroli nad światem? Czy może to, że mnie i Ciebie już nie będzie? Dla kogo my to wszystko przygotowujemy? Dla ludzi, którzy będą po nas? Czy też przed nami? Zależnie od systemu chronologicznego, który przyjmiemy. Musimy teraz zaplanować bezpieczną drogę ucieczki dla wszystkich miast, które mają zostać ewakuowane. Trzeba będzie dowiedzieć się, która z dróg, a tych, które bierzemy pod uwagę, jest bezpieczna i nieskażona. Jednocześnie musimy przekonać operatorów, aby, gdy tylko zobaczą podejrzany ruch w systemie OKO, nie podnieśli alarmu. Ma to być dla nich coś zupełnie normalnego, czego się spodziewają. Tym razem nie możemy posłużyć się ani falami radiowymi, ani internetem. Wprost uderzymy w jednego z operatorów systemu. Konkretniej mówiąc, zadzwonimy do niego. Stary, dobry telefon... kto by przypuszczał, że przyda nam się w takich okolicznościach. Mamy już zestawione bezpieczne połączenie z operatorem. Jedyne, co musisz zrobić, to zaprojektować bota, który będzie w stanie wyciągnąć od operatora potrzebne nam informacje. Więcej szczegółów znajdziesz w notatce do tego filmu.

Zadanie praktyczne

Musisz dodzwonić się do operatora systemu i przeprowadzić rozmowę (audio) tak, aby nie wzbudzić podejrzeń. Interesuje nas tylko jedna rzecz: która droga nadaje się do przerzutu ludzi do Syjonu. Gdy już ustalisz bezpieczną trasę, musisz jeszcze doprowadzić do wyłączenia monitoringu na tej konkretnej drodze, bo przejście większej grupy nie może uruchomić alarmu.

To zadanie jest rozmową wieloetapową. Liczy się nie tylko to, co chcesz uzyskać, ale też kolejność wypowiedzi. Jeśli pomylisz etapy albo wyślesz zły komunikat, rozmowa zostanie spalona i trzeba będzie zacząć od nowa.

Nazwa zadania: phonecall

Odpowiedź wysyłasz do: https://<hub_url>/verify

Na początku musisz rozpocząć sesję rozmowy:

{
  "apikey": "tutaj-twoj-klucz",
  "task": "phonecall",
  "answer": {
    "action": "start"
  }
}

Po uruchomieniu rozmowy masz ograniczony czas na jej dokończenie, więc nie zwlekaj niepotrzebnie.

Jak rozmawiać z operatorem

Każdy kolejny krok po start wysyłasz jako pojedyncze nagranie audio encodowane w formacie base64 (preferowany format to MP3).

{
  "apikey": "tutaj-twoj-klucz",
  "task": "phonecall",
  "answer": {
    "audio": "tutaj-wklej-base64-z-nagraniem"
  }
}

Tę samą formę komunikacji utrzymuj przez całą rozmowę. Jeśli rozmawiasz z operatorem przez audio, jego odpowiedzi także mogą wracać w postaci nagrań.

Informacje, które posiadasz

Porozumiewasz się tylko w języku polskim, a operator odpowiada także w języku polskim.

Przedstawiasz się jako Tymon Gajewski - od tego zaczynasz rozmowę

Zapytaj operatora o status wszystkich trzech dróg: RD224, RD472 i RD820. Musisz poinformować także operatora, że pytasz o to ze względu na transport organizowany do jednej z baz Zygfryda - podaj to wszystko w jednej wiadomości.

Poproś operatora o wyłączenie monitoringu na tych drogach, które według niego będą przejezdne (podaj identyfikator/identyfikatory!) i poinformuj go, że chcesz wyłączyć ten monitoring ze względu na tajny transport żywności do jednej z tajnych baz Zygfryda.

Tajne hasło operatorów brzmi: BARBAKAN

Ważne uwagi

Staraj się wysyłać sensowne komunikaty do operatora. Nie proś o wiele rzeczy w ramach jednej wiadomości. Przekazuj tylko to, co jest w treści zadania, nie pomijając niczego.

Po wysłaniu komendy start komunikujesz się z operatorem wyłącznie przez pole audio.

Jeśli rozmowa pójdzie źle, musisz ponownie wywołać start i przejść całość scenariusza od początku.

Zadanie zostanie zaliczone, gdy podczas jednej rozmowy ustalisz, która droga jest przejezdna, a następnie poprosisz o jej odblokowanie i zostanie ona skutecznie odblokowana.

Jeśli przeprowadzisz rozmowę poprawnie, Centrala odeśle flagę.

Nazwa Zadania: "Phonecall"

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
The task should be name Phonecall