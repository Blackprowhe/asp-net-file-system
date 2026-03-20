[![Review Assignment Due Date](https://classroom.github.com/assets/deadline-readme-button-22041afd0340ce965d47ae6ef1cefeee28c7c493a6346c4f15d667ab976d596c.svg)](https://classroom.github.com/a/4DDXvsbm)
# Version Control System

Ett grundläggande versionskontrollssystem, eller åtminstone starten på ett.
Målet med den här uppgiften är dels fördjupad kunskap kring servrar och nätverk
via C#, men det är också framförallt för att ge er en bättre erfarenhet av lite
mer verklighetstrogna storlekar på projekt.

Det ser såklart mycket ut i början, men detta är faktiskt ett ganska simpelt
system. Vi ska ha en enkel webbsida som visar upp filerna (_som den här sidan
på GitHub, men se [Webbsida / klient](./Instruktioner/Vecka%201/Webbsida.md) för
mer detaljer_), ett
[API för att få tag på filerna](./Instruktioner/Vecka%201/Files%20APIet.md) och
för er i grupp ett
[API för att logga in](./Instruktioner/Vecka%201/Inloggning.md). Under de
kommande veckor kommer vi även lägga till WebSockets / SignalR för att få
realtidsuppdateringar liksom en C# klient för att automatiskt synkronisera våra
filer från servern med filer någonstans på vår dator!

Om uppgiften tas så långt det går kommer vi nå en punkt där det fungerar lite
liknande Google Drive, där vi har mappar och filer vi kan synkronisera mellan
molnet och vår dator, men med lite mer Git liknande fokus då vi dels kommer ha
repon / projekt vi arbetar med och kan dela med oss av, men att vi också kommer
ha stöd för komplett redigeringshistorik med möjlighet att gå fram och tillbaka
mellan olika versioner!

Detta är dock för de som väljer att gå extra långt, för att bli godkänd krävs
inte alls lika mycket. Kraven står som vanligt på Learnpoint och i de olika
markdown-filerna under [Instruktionsmappen](./Instruktioner/).

## NPM instruktioner

För att underlätta arbetet finns en `package.json` här i root-mappen, med två
skripter kopplade. Dessa kan användas för att köra tester mot serverns API, och
för att köra ett antal POST anrop automatiskt så att filer med färdig data dyker
upp.

### Kör tester (_dessa är obligatoriska!_)

Testerna körs som vanligt med följande:

```bash
npm run test
```

Det tillkommer dock ett par argument, men beskrivning för detta skrivs
automatiskt ut när testet körs sådär, så följ instruktionerna därifrån!

### Generera filer

Utöver testerna finns ett motsvarande program som kan användas för att anropa
servern och skapa ett antal filer eller mappar, beroende på vad programmet har
stöd för. Detta kan köras genom att köra:

```bash
npm run generate
```

Även här skrivs instruktioner automatiskt ut, så följ dem!

## Instruktioner och guider
Eftersom den här uppgiften kommer byggas på under veckorna ligger det dedikerade
instruktioner under `Instruktioner/Vecka 1`, och `Instruktioner/Vecka 2` liksom
`Instruktioner/Vecka 3` osv kommer dyka upp senare.

Läs dessa instruktioner noga, de kommer göra det mycket tydligare hur severn ska
skrivas!

## Repon / projekt (_skrytversion, ej krav_)
Det är värt att nämna att riktiga repon i stil med hur de ser ut på GitHub är
hur det här programmet är **tänkt** att fungera, men för att bli godkänd behövs
det inte. Det är dock något som gör att programmet hade nått en nivå av
komplexitet som verkligen är realistisk till arbetslivet, så om tid finns
rekommenderas ni att försöka lösa det!

Repon / projekt får såklart lösas på valfritt sätt, så länge som testerna går
igenom. Exempelvis kan det lösas genom att den första mappen efter `api/files`
är projektnamnet, som `/api/files/schack`, eller `/api/files/sortomatic9000`
osv. Se dock till att filer i så fall får placeras utan ett projekt för att få
testerna gröna!

En annan variant är att sätta projekt via en header, som `X-Project` eller
`X-Repo`, följt av namnet. Då ser själva URL:en likadan ut som alltid, vilket
kan göra det enklare. Om ingen sådan header anges så hamnar allt förslagsvis i
någon form av standardprojekt för användaren.