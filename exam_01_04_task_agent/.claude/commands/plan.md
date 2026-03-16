---
description: Systematyczne planowanie wdrożenia z analizą problemu i dokumentacją
allowed-tools: "*"
---

# PLAN - Komenda systematycznego planowania wdrożenia

## Kontekst systemu
- **Serwer**: Amazon Web App
- Front End - TypeScript App
- Back End - .Net c# App
- **Szczegóły**: `CLAUDE.md`

## Zadanie: $ARGUMENTS

Wykonaj systematyczny plan wdrożenia dla: **$ARGUMENTS**

### 1. ANALIZA PROBLEMU
- Zidentyfikuj wymagania i ograniczenia
- **CHECKPOINT**: NIE przechodź dalej bez wykonania analizy

### 2. RESEARCH I OPCJE
- **ZALECENIE**: Użyj think harder przed analizowaniem opcji dla lepszego planowania
- Przeszukaj najlepsze praktyki
- Rozważ minimum 3 różne podejścia
- Przeanalizuj zalety\wady każdego
- **WAŻNE**: Przedstaw opcje użytkownikowi i uzyskaj zgodę na wybrane rozwiązanie
- **CHECKPOINT**: NIE przechodź dalej bez przedstawienia opcji użytkownikowi

### 3. KONSULTACJA Z UŻYTKOWNIKIEM
Przed stworzeniem planu:
- Przedstaw znalezione opcje rozwiązania
- Wyjaśnij zalety i wady każdej opcji
- Zapytaj użytkownika o preferencje i wymagania
- Uzyskaj potwierdzenie wybranego podejścia
- **CHECKPOINT**: NIE przechodź dalej bez EXPLICITE zgody użytkownika na wybrane rozwiązanie

### 4. PLAN WDROŻENIA
Stwórz szczegółowy plan w formacie:

```markdown
# Plan Wdrożenia [$ARGUMENTS]

## Data: [YYYY-MM-DD]

## Problem do rozwiązania
[Opis obecnej sytuacji i celów]

## Wybrane rozwiązanie
[Nazwa i krótkie uzasadnienie wybranego podejścia z sekcji konsultacji]

## Plan implementacji
### Faza 1: Przygotowanie
### Faza 2: Implementacja  
### Faza 3: Testowanie

## Parametry rozwiązania
## Oczekiwane rezultaty
```

### 5. ZAPISZ DOKUMENTACJĘ
- Utwórz katalog `.\details\YYYY\MM\` jeśli nie istnieje
- Zapisz plan jako `.\details\YYYY\MM\DD-[normalized-topic]-plan.md`
- Dodaj wpis do `.\TODO.md` w sekcji "📋 Zaplanowane projekty"
- Zaktualizuj `.\details\INDEX.md` z nowym wpisem
- **CHECKPOINT**: NIE przechodź dalej bez zapisania wszystkich plików

### 6. PREZENTUJ PLAN
Pokaż userowi podsumowanie i zapytaj: **"Czy przystąpić do implementacji? Użyj \implement"**
- **CHECKPOINT**: KONIEC - Nie wykonuj automatycznie \implement

## RYGORYSTYCZNE ZASADY:
- **MUSISZ** wykonać WSZYSTKIE kroki 1-6 w kolejności
- **ZATRZYMAJ SIĘ** na każdym CHECKPOINT i poczekaj na użytkownika jeśli trzeba
- **NIE SKACZ** między krokami
- **NIE WYKONUJ** \implement automatycznie
- **ZAPISZ** wszystkie wymagane pliki zanim skończysz

Rozpocznij analizę dla: **$ARGUMENTS**