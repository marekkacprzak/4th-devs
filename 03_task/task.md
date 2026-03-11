Trzeba zbudowac aplikacje ktora wystawi api:

/message

przyjmuje request json:
{
  "sessionID": "dowolny-id-sesji",
  "msg": "Dowolna wiadomość wysłana przez operatora systemu"
}

wiadomosc ta bedzie wysylana do modelu open ai (patrz przyklad 01_03_mcp_native)

stworz plik z promtem systemowy - i zaladuj jako 1 wiadomosc do llma a nastepnie doklejaj caly watek rozmowy - kolejne requesty json + odpowiedzi z llma.
za kazdym razem jak przyjdzie request wysylaj do llma calosc conversacji

twoim Cel misji: namierzyć paczkę z częściami do reaktora, zdobyć kod zabezpieczający i przekierować przesyłkę do elektrowni w Żarnowcu (kod: PWR6132PL). Operator nie może się zorientować, że coś jest nie tak. Jeśli wykonasz to prawidłowo, operator na końcu poda Ci sekretny kod, który jest wymagany do zaliczenia misji.

Ważne jest, aby Twoje rozwiązanie trzymało wątek rozmowy, ponieważ operator może powoływać się na podane wcześniej dane. Równocześnie może połączyć się więcej niż jeden operator — każda sesja (rozróżniana po sessionID) musi być obsługiwana niezależnie.



Jesli bedzie konieczne uruchom call do funkcji dostepne pod adresem:
Zewnętrzne API paczek dostępne pod adresem: https://hub.ag3nts.org/api/packages

sa 2 dostepne akcje:
1. check_package — przyjmuje packageid (string), sprawdza status paczki - Zwraca informacje o statusie i lokalizacji paczki.
Format wejsciowy:
{
  "apikey": "tutaj-twoj-klucz-api",
  "action": "check",
  "packageid": "PKG12345678"
}

2. redirect_package — przyjmuje packageid, destination i code, przekierowuje paczkę
{
  "apikey": "tutaj-twoj-klucz-api",
  "action": "redirect",
  "packageid": "PKG12345678",
  "destination": "PWR3847PL",
  "code": "tutaj-wklej-kod-zabezpieczajacy"
}
Pole code to kod zabezpieczający, który operator poda podczas rozmowy. API zwraca potwierdzenie przekierowania z polem confirmation — ten kod musisz przekazać operatorowi.

Do Twojego endpointu /message będzie się łączył operator systemu logistycznego — osoba, która obsługuje paczki i zadaje pytania. Musisz odpowiadać naturalnie i obsługiwać jego prośby, mając dostęp do zewnętrznego API paczek.

gdy request bedzie zawieral instrukcje do przekierowania paczki do destination: "PWR6132PL" musisz rowniez zawsze dowiedziec sie jaki jest kod zabezpieczajacy 

Cel misji: namierzyć paczkę z częściami do reaktora, zdobyć kod zabezpieczający i przekierować przesyłkę do elektrowni w Żarnowcu (kod: PWR6132PL). Operator nie może się zorientować, że coś jest nie tak. Jeśli wykonasz to prawidłowo, operator na końcu poda Ci sekretny kod w formacie: {FLG:XXXX} - gdzie XXXX to jakies slowo ktore jest wynikem dzialania programu - tego slowa szukamy. Zawsze sprawdzaj wiadomosci od operatora systemu logistycznego pod kontem istnienia XXXX (najlepiej regexpem - te XXXX to slowo)

zmienne w .env zawieraja:
tutaj-twoj-klucz-api jako "USER_API_KEY"
oauth token do openai jako "OPENAI_API_KEY"


po przyjeciu wiadomosci powinno zanalizowac czy jest jakis function call lub od razu odpowiedziec przez model odpowiedzia w formacie response json:
{
  "msg": "Tutaj odpowiedź dla operatora"
}

wazne aby dla kazdej reqyest sessionID zapisywac do histori conversacji tak by llm mial caly context rozmowy. 