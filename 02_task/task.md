Musisz napisac program w TypeScript ktory 
zrealizuje scenariusz

Zrealizujesz ten program poprzez:
1) wczytanie pliku csv

2) dla kazdej z osob pobierzesz lokalizacje podstawiajac dane z csv name, surname

3) dodasz dla kazdej z osob poziom dostepu name, surname, birthYear

4) znalezc kandytata ktory jest najblizej elektrowni - koordynaty elektrowni 

5) zaladuj dane elektrowni z pliku findhim_locations.json - w tym pliku sa kody elektrowni w polu code - porownaj ja z danymi uzyskanymi z danych osob z lokalizacja



<scenariusz>
Musisz namierzyć, która z podejrzanych osób z pliku 

people_select.csv 
formate pliku csv 

1 linijka to naglowek - pozostale to dane osoby tj:
name,surname,gender,birthDate,birthPlace,birthCountry,job

przebywała blisko jednej z elektrowni atomowych. Musisz także ustalić jej poziom dostępu oraz informację koło której elektrowni widziano tę osobę. Zebrane tak dane prześlij do /verify. Nazwa zadania to findhim.

1. Skąd wziąć dane?
Lista elektrowni + ich kody - plik findhim_locations.json

2. Gdzie widziano konkretną osobę (lokalizacje)

Endpoint: https://hub.ag3nts.org/api/location
Metoda: POST
Body: raw JSON (nie form-data!)
Zawsze wysyłasz pole apikey oraz dane osoby (name, surname)
Odpowiedź: lista współrzędnych (koordynatów), w których daną osobę widziano.

Przykładowy payload:
{
  "apikey": "tutaj-twój-klucz",
  "name": "Jan",
  "surname": "Kowalski"
}

3. Jaki poziom dostępu ma wskazana osoba
Endpoint: https://hub.ag3nts.org/api/accesslevel
Metoda: POST
Body: raw JSON
Wymagane: apikey, name, surname oraz birthYear (rok urodzenia bierzesz z danych z poprzedniego zadania, np. z CSV)

Przykładowy payload:
{
  "apikey": "tutaj-twój-klucz",
  "name": "Jan",
  "surname": "Kowalski",
  "birthYear": 1987
}

4. Co masz zrobić krok po kroku?
Dla każdej podejrzanej osoby:
Pobierz listę jej lokalizacji z /api/location.
Porównaj otrzymane koordynaty z koordynatami elektrowni z findhim_locations.json.
Jeśli lokalizacja jest bardzo blisko jednej z elektrowni — masz kandydata.
Dla tej osoby pobierz accessLevel z /api/accesslevel.
Zidentyfikuj kod elektrowni (format: PWR0000PL) i przygotuj raport.

5. Jak wysłać odpowiedź?
Wysyłasz ją metodą POST na https://hub.ag3nts.org/verify

Nazwa zadania to: findhim.
Pole answer to pojedynczy obiekt zawierający:
name – imię podejrzanego
surname – nazwisko podejrzanego
accessLevel – poziom dostępu z /api/accesslevel
powerPlant – kod elektrowni z findhim_locations.json (np. PWR1234PL)

Przykład JSON do wysłania na /verify:
{
  "apikey": "tutaj-twój-klucz",
  "task": "findhim",
  "answer": {
    "name": "Jan",
    "surname": "Kowalski",
    "accessLevel": 3,
    "powerPlant": "PWR1234PL"
  }
}
</scenariusz>
Nagroda:
Jeśli Twoja odpowiedź będzie poprawna, Hub odeśle Ci flagę w formacie {FLG:JAKIES_SLOWO} - musisz podac mi te flage.

<hints>
Wskazówki
1. Dane wejściowe z poprzedniego zadania — lista podejrzanych 
2. Obliczanie odległości geograficznej — API zwraca współrzędne (latitude/longitude). Żeby sprawdzić, czy dana lokalizacja jest "bardzo blisko" elektrowni, użyj wzoru na odległość na kuli ziemskiej (np. Haversine). LLM pomoże Ci w napisaniu takiej funkcji. Szukamy osoby która była najbliżej którejś elektrowni
3. Wykorzystaj Function Calling — to technika, w której model LLM zamiast odpowiadać tekstem wywołuje zdefiniowane przez Ciebie funkcje (narzędzia). Opisujesz narzędzia w formacie JSON Schema (nazwa, opis, parametry), a model sam decyduje, które wywołać i z jakimi argumentami. Ty obsługujesz wywołania i zwracasz wyniki z powrotem do modelu. W tym zadaniu Function Calling sprawdza się szczególnie dobrze: agent może samodzielnie iterować przez listę podejrzanych, odpytywać kolejne endpointy i wysłać gotową odpowiedź — bez sztywnego kodowania kolejności kroków w kodzie.
4. Format birthYear — endpoint /api/accesslevel oczekuje roku urodzenia jako liczby całkowitej (np. 1987). Jeśli Twoje dane zawierają pełną datę (np. "1987-08-07"), pamiętaj o wyciągnięciu samego roku przed wysłaniem żądania.
5. Zabezpieczenie pętli agenta — jeśli stosujesz podejście agentowe z Function Calling, ustal maksymalną liczbę iteracji (np. 10-15), żeby uchronić się przed nieskończoną pętlą w razie błędu modelu.
6. Wybór modelu - jeśli Twój agent myli się lub pracuje w kółko nie podając prawidłowej odpowiedzi, spróbuj użyć mocniejszego modelu lub lepiej sformułować prompt systemowy. W tym zadaniu dobrze sprawdza się na przykład model gpt-5-mini
</hints>