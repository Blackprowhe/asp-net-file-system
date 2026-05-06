# ASP.NET File System

Ett dokument- och filhanteringssystem byggt med ASP.NET Core.

Projektet fokuserar på backend-utveckling, API-design och filhantering.  
Systemet innehåller stöd för uppladdning, hämtning och organisering av filer samt realtidsfunktioner med SignalR.

## Funktioner

- REST API för filhantering
- Uppladdning och nedladdning av filer
- Realtidsuppdateringar med SignalR
- Struktur för projekt/repositories
- Tester för API-funktionalitet
- Frontend-klient för filhantering
- Versionshistorik för filer
- Bildstöd
- Push- och pull-funktionalitet via klient
- Grundläggande autentisering och inloggningslogik

## Tekniker

- ASP.NET Core
- C#
- SignalR
- JavaScript
- HTML/CSS
- Git & GitHub

---

# Komma igång

## Klona projektet

```bash
git clone https://github.com/Blackprowhe/asp-net-file-system.git
```

---

## Starta servern

Navigera till projektmappen:

```bash
cd TestApp
```

Starta sedan applikationen:

```bash
dotnet run
```

Servern startar på:

```text
http://localhost:5137
```

Detta räcker för att använda webbgränssnittet och testa funktionerna.

---

## Client för push och pull

Navigera till klientmappen från projektets root:

```bash
cd Client
```

### Pull från servern

```bash
dotnet run pull localhost:5137
```

### Push till servern

```bash
dotnet run push localhost:5137
```

---

## Om projektet

Projektet utvecklades som en del av min utbildning inom .NET-utveckling och fokuserade på att bygga ett större system med backend, 
API:er och versionshantering inspirerat av GitHub och molnlagringstjänster.

Målet med projektet var att få praktisk erfarenhet av:
- Backend-utveckling
- Realtidskommunikation
- API-design
- Versionshantering
- Filsystem och klient/server-kommunikation

---
