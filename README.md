# AutoVoorraad & Proefritten — Ames occasions

Kleine full-stack app voor een autobedrijf, uitgewerkt voor **Ames** (officieel dealer van Volkswagen, Audi,
SEAT, Škoda en CUPRA, met zes vestigingen in de Drechtsteden en Hoeksche Waard): occasions beheren met een
RDW-koppeling op kenteken, en proefritten aanvragen.

**Stack:** .NET 10 / ASP.NET Core Razor Pages + minimal API · EF Core 10 + MySQL 8 · Serilog · xUnit.
De gekozen stappen en afwegingen staan in **[PLAN.md](PLAN.md)**.

## Lokaal opstarten

Vereist: .NET 10 SDK en Docker (of een eigen MySQL 8). Geen Node of front-end build nodig.

```bash
# 1. Database
docker compose up -d

# 2. App (in Development: draait migrations + seed-data automatisch bij opstarten)
dotnet run --project src/AutoInventory.Api
```

- Website (Razor Pages): **http://localhost:5080**
- JSON-API: **http://localhost:5080/api/vehicles** (per pagina; en verder, zie de documentatie)
- API-documentatie (OpenAPI/Scalar): **http://localhost:5080/scalar**

`dotnet run` start in de omgeving *Development* (via `launchSettings.json`); de lokale connection string en het
automatisch migreren staan daarom in `appsettings.Development.json`, niet in de basisconfig.
Eigen MySQL? Pas `ConnectionStrings:Default` daar aan, of zet `ConnectionStrings__Default=...`.
Opnieuw beginnen met een lege database: `docker compose down -v`.

De zeven seed-kentekens zijn fictief. Om de RDW-koppeling te proberen, voeg je een auto toe met een bestaand kenteken.

### Tests

```bash
dotnet test
```

- **Unit tests**: proefrit-regels incl. openingstijden, zomertijd en tijdslots; kenteken; RDW-mapping (ook brandstof);
  de RDW-client tegen een nep-HTTP-handler; de cache; en een retry door de échte resilience-pipeline.
- **Integratietests** (`WebApplicationFactory`): de echte HTTP-pipeline met in-memory SQLite, een nep-RDW en een vaste klok.
  Er is geen MySQL of internet nodig. Elke test krijgt een verse database, dus de volgorde maakt niet uit.
  Ook getest: een verloren race om een tijdslot geeft 409 (deterministisch nagebootst met een EF-interceptor),
  verouderde wijzigingen worden geweigerd, een auto met geplande proefritten kan niet weg, en paginering en sortering
  (een ongeldige pagina of sortering geeft 400).
- **Paginatests**: de Razor Pages via echte HTTP, inclusief het anti-forgery token zoals een browser het meestuurt:
  veldfouten naast het veld (en de invoer blijft staan), Post/Redirect/Get, een auto toevoegen, wijzigen en
  verwijderen via de formulieren, een verouderde wijziging of "markeer als verkocht", verwijderen dat geweigerd wordt
  door een geplande proefrit, de paginering die filters en sortering vasthoudt, en de 404-pagina. Een POST zonder
  token wordt geweigerd.

### Migrations

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add <Naam> -p src/AutoInventory.Api -o Data/Migrations
```

## Configuratie

| Sleutel | Standaard | Doel |
|---|---|---|
| `ConnectionStrings:Default` | alleen in Development | Database. In productie via een omgevingsvariabele of secret store, niet in een ingecheckt bestand. |
| `Rdw:BaseUrl` | `https://opendata.rdw.nl/` | RDW open data |
| `OpeningHours` | ma–vr 08:30–17:30, za 09:00–16:00 (Europe/Amsterdam) | Wanneer proefritten mogen. **Aanname**, niet de echte Ames-tijden. |
| `Database:MigrateOnStartup` | `false` (Development: `true`) | Migrations bij opstarten: handig lokaal; in productie via de pipeline |
| `Serilog` | console + `logs/` (14 dagen) | Logging |

## Projectstructuur

```
src/AutoInventory.Core    entities + business rules (geen dependencies)
src/AutoInventory.Api
  Pages/                  Razor Pages: overzicht, detail + proefrit, voertuig toevoegen en wijzigen, foutpagina
  Endpoints/              JSON-API (dun: service aanroepen, uitkomst → HTTP-status)
  Services/               use cases, gedeeld door pagina's en API
  Rdw/                    RDW-client, resilience, cache
  Data/                   EF Core DbContext + migrations
  wwwroot/                CSS + een klein site.js (alleen progressive enhancement)
tests/AutoInventory.Tests xUnit: unit tests, API-integratietests en paginatests
```

## Keuzes & trade-offs (kort)

- **Elk merk is toegestaan**, niet alleen de vijf dealermerken: ingeruilde auto's van andere merken zijn ook occasions.
  De dealermerken staan wel altijd bovenaan in het filter.
- **Proefritten alleen binnen openingstijden**, gerekend in Nederlandse tijd (incl. zomertijd), en de hele rit (1 uur) moet erin passen.
- **Vaste tijdslots op het hele uur + een unique index op (voertuig, starttijd).** De controle in de code geeft een nette
  melding (400). De index is het vangnet voor twee gelijktijdige aanvragen: de tweede krijgt 409.
  Getest op MySQL: 16 gelijktijdige aanvragen op 8 tijdslots gaven 8 boekingen, nooit twee in hetzelfde slot.
  - De migration die de index uniek maakt is met de hand aangepast: MySQL laat geen index droppen die een foreign key nodig heeft.
  - Het formulier toont alleen geldige tijden (`/availability`), met bezette tijden uitgegrijsd.
  - De tijdenlijst bevat de starttijd in UTC en het formulier stuurt die ongewijzigd terug, dus de tijdzone van de bezoeker doet er niet toe.
- **RDW is een externe afhankelijkheid** en krijgt daarom:
  - **Resilience** (`Microsoft.Extensions.Http.Resilience`): 3 s per poging, 8 s totaal, 2 retries bij tijdelijke fouten,
    en een circuit breaker die RDW even met rust laat als het misgaat: open zodra minstens de helft van 5 of meer
    pogingen binnen 30 s mislukt (de standaard van 100 pogingen haalt één dealer nooit). Elke dataset heeft een eigen pipeline,
    zodat de optionele brandstof-dataset de verplichte voertuig-lookup nooit kan meeslepen.
  - **Een cache** (decorator rond de client): 12 uur voor gevonden kentekens, 10 minuten voor onbekende. Fouten worden nooit gecachet.
  - **Brandstof als voorinvulling** uit een tweede RDW-dataset, parallel opgehaald. Als die faalt, mist alleen de voorinvulling.
    De dealer kan de waarde aanpassen, want de RDW ziet een mild hybrid als gewone benzineauto.
- **Vestiging per auto**: de bezoeker ziet waar de auto staat.
- **De server haalt de RDW-data op bij opslaan**, niet de client. Zo is de server de bron van waarheid voor merk/model.
  Merknamen krijgen hun officiële schrijfwijze (`SKODA` → `Škoda`); het model blijft zoals de RDW het levert.
- **Validatie zit in C#, de browser checkt alleen voor snelle feedback.** De API is direct aan te roepen, dus de
  server moet alles zelf controleren. Veldlengtes staan één keer in `Core.Limits` en worden gebruikt door zowel de
  validatie als de kolomgroottes, zodat de API nooit iets accepteert wat MySQL daarna weigert (anders volgt een 500).
  - E-mail: een bewust pragmatische regex (geen volledige RFC-check; alleen een bevestigingsmail bewijst dat een adres werkt).
  - Telefoon: alleen cijfers, spaties, `+`, `-` en haakjes, 10–15 cijfers.
- **Optimistic concurrency** op voertuigen (`Version`): een wijziging op basis van verouderde gegevens krijgt 409
  in plaats van stilletjes het werk van een collega te overschrijven. Het wijzigformulier op de website stuurt de
  versie mee als verborgen veld; bij een conflict toont het een melding en blijft de invoer staan.
- **Wijzigen en verwijderen op de website**, onder "Beheer" op de detailpagina. Kenteken, merk, model en bouwjaar
  komen van de RDW en zijn niet te wijzigen. Verwijderen vraagt om bevestiging en kan niet zolang er proefritten
  gepland staan.
- **`GET /api/vehicles` geeft één pagina terug**: `{ items, page, pageSize, totalCount, totalPages }`, standaard 20
  per pagina (maximaal 50). Sorteren met `sort`: `brand` (standaard), `price`, `price_desc`, `mileage` of `year_desc`.
  Sorteren en pagineren gebeurt in de database; een ongeldige waarde geeft 400 (ProblemDetails).
  Het overzicht toont 6 auto's per pagina, en de paginalinks houden filters en sortering vast.
- **Gelijktijdige requests**: zowel dubbele kentekens als dubbele tijdslots worden door een unique index afgevangen
  en netjes als 409 teruggegeven. Getest op MySQL: 16 gelijktijdige toevoegingen van hetzelfde kenteken gaven
  één auto en 15× 409.
- **Business rules als pure functies in Core** (klok, bestaande boekingen en openingstijden als parameters) → snelle, deterministische tests.
- **Geen repository-laag** boven EF Core: weinig winst bij deze omvang.
- **Oracle `MySql.EntityFrameworkCore`** i.p.v. Pomelo: Pomelo heeft (nog) geen EF Core 10-versie.
- **MySQL i.p.v. SQLite**: dichter bij productie. Docker Compose houdt het opstarten op één commando.
- **Razor Pages** voor de front-end, zoals de case voorstelt: server-side gerenderd, geen front-end build, één proces.
  Alles werkt zonder JavaScript; `site.js` maakt het alleen sneller in gebruik. Tijdens het versturen toont de knop
  een laadstatus ("Bezig…", bij de RDW "Ophalen bij de RDW…") en kan er niet twee keer geklikt worden.
- **De pagina's en de API delen services** (`VehicleService`, `TestDriveService`). Ze roepen elkaar dus niet via HTTP aan,
  en beide volgen exact dezelfde regels. Een uitkomst (`ServiceResult`) wordt in de API een statuscode, op een pagina een veldfout.
- **Formulieren:** anti-forgery tokens, Post/Redirect/Get na succes, waarden cultuuronafhankelijk gelezen.
- **Integratietests op SQLite** i.p.v. MySQL: snel en zonder Docker, maar ze vangen geen provider-specifieke verschillen af.
- **Serilog naar console + bestand**, gestructureerd. Een Seq- of App Insights-sink toevoegen is één regel config.
  Er komen geen e-mailadressen of telefoonnummers van bezoekers in de logs (AVG). De foutpagina toont een foutcode
  (de TraceId) die in elke logregel van dat request staat, zodat een melding van een bezoeker direct terug te vinden is.

## Met meer tijd

- **JWT-authenticatie** met een `Medewerker`-rol op POST/PUT/DELETE, de RDW-lookup en het blok "Beheer" op de
  detailpagina (wijzigen, markeer als verkocht, verwijderen). Dat blok is nu voor iedereen zichtbaar.
- Integratietests ook tegen echte MySQL (Testcontainers).
- Openingstijden per vestiging, feestdagen, en echte Ames-tijden.
- Een gedeelde cache (Redis) als de app op meerdere servers draait; de huidige cache is per proces.
- Een beheeroverzicht van aangevraagde proefritten, een bevestigingsmail naar de bezoeker.
