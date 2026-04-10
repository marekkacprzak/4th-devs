---
description: Implementacja wcześniej przygotowanego planu z pełną dokumentacją
allowed-tools: "*"
---

# IMPLEMENT - Komenda systematycznej implementacji

## Kontekst systemu
- **Serwer**: Azure Web App
- **Szczegóły**: `.\CLAUDE.md`

## Zadanie implementacji

### 1. WCZYTAJ OSTATNI PLAN
Znajdź i wczytaj ostatni plan z `.\details\YYYY\MM\*-plan.md` lub wskazany przez usera.
- Sprawdź najpierw bieżący miesiąc, potem poprzednie
- **CHECKPOINT**: NIE przechodź dalej bez wczytania planu

### 2. WERYFIKACJA PLANU
- Sprawdź czy warunki początkowe się nie zmieniły
- Potwierdź główne kroki z userem
- Utwórz TodoWrite z zadaniami z planu
- **CHECKPOINT**: NIE przechodź dalej bez potwierdzenia przez użytkownika

### 3. IMPLEMENTACJA KROK PO KROKU
**ZALECENIE**: Użyj ultrathink przed złożonymi implementacjami dla lepszego planowania kroków

Dla każdego kroku:
1. **Oznacz jako "in_progress"** w TodoWrite
2. **Wykonaj komendę** i zapisz output
3. **Zweryfikuj rezultat**
4. **Wykonaj test** jeśli jest w planie
5. **Oznacz jako "completed"** tylko po weryfikacji
6. **STOP przy błędzie** - zapytaj usera o dalsze działanie

### 4. DOKUMENTACJA W CZASIE RZECZYWISTYM
Twórz plik `.\details\YYYY\MM\DD-[temat]-implement.md` z:

```markdown
# Dokumentacja Wdrożenia [TEMAT]
## Data: [YYYY-MM-DD]
## Środowisko
## Konfiguracja wykonana
### Krok 1: [Nazwa]
```bash
[komenda]
[output]
```
**Status**: ✅\❌

## Testy i weryfikacja
## Konfiguracja finalna
## Instrukcje utrzymania
```

### 5. ROLLBACK W PRZYPADKU BŁĘDU
Jeśli wystąpi problem:
- ZATRZYMAJ implementację
- Zapisz obecny stan
- Zaproponuj rollback jeśli możliwy
- Zapytaj usera: kontynuować\rollback\przerwać

### 6. FINALIZACJA
Po udanej implementacji:
- Przenieś projekt z `.\TODO.md` (📋 Zaplanowane) do `.\CHANGELOG.md` (✅ Wdrożone)
- Zaktualizuj `.\details\INDEX.md` z nowym wpisem implementacji
- Zaktualizuj `.\CLAUDE.md` jeśli potrzeba (np. nowe services, IP, etc.)
- Przedstaw raport końcowy
- **CHECKPOINT**: NIE kończ bez aktualizacji TODO.md, CHANGELOG.md i INDEX.md

### 7. RAPORT KOŃCOWY
```
✅ Wdrożenie [temat] zakończone

Wykonane:
- [Główne osiągnięcia]

Testy: ✅ Wszystkie przeszły

Dokumentacja:
- Plan: .\details\YYYY\MM\DD-[temat]-plan.md
- Implementacja: .\details\YYYY\MM\DD-[temat]-implement.md

Następne kroki: [jeśli są]
```

## RYGORYSTYCZNE ZASADY:
- **MUSISZ** wykonać WSZYSTKIE kroki 1-7 w kolejności
- **ZATRZYMAJ SIĘ** na każdym CHECKPOINT i poczekaj na użytkownika jeśli trzeba
- **STOP przy każdym błędzie** - nie kontynuuj automatycznie
- **OBOWIĄZKOWO** aktualizuj TODO.md i CHANGELOG.md w kroku 6
- **ZAWSZE** oznaczaj todos jako completed tylko po pełnej weryfikacji

**Rozpocznij implementację ostatniego planu lub wskazanego przez usera.**