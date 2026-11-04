# Plan – AutoVoorraad & Proefritten (Ames occasions)

Een webapplicatie waarmee een autobedrijf zijn occasions beheert en bezoekers een proefrit kunnen aanvragen.
Ik heb hem uitgewerkt voor **Ames** (Amega Groep): officieel dealer van Volkswagen, Audi, SEAT, Škoda en CUPRA,
met zes vestigingen in de Drechtsteden en de Hoeksche Waard.

**Stack:** C# / .NET 10 (ASP.NET Core: Razor Pages + minimal API) · EF Core 10 + MySQL 8 · Serilog · xUnit.

---

## Werkwijze

**Stap voor stap, in de blokken van de case.** De app is opgebouwd in de volgorde van de voorgestelde tijdsindeling:
opzet, datamodel, API + RDW-koppeling, proefrit-logica, front-end, en tot slot logging en documentatie. Elke stap is een
eigen commit met een groene build en groene tests. Zo zijn de RDW-koppeling en de proefrit-regels, het hart van de case,
als eerste af en getest, en bouwt de front-end daarop voort.

**Correcties met een test die het probleem eerst laat zien.** De gevonden problemen staan elk als eigen commit in de
historie, met de test en de oplossing samen (zie *Gevonden en opgelost*).

**Eigen aanvullingen** bovenop de kern, elk als aparte commit:
- overbodige commentaarregels weggehaald: de code moet zichzelf uitleggen, de onderbouwing staat hier en in de README;
- voertuigen wijzigen en verwijderen op de website (de API kon het al);
- paginering en sorteren, op het overzicht en in de API;
- de circuit breaker afgesteld op het verkeer van één dealer;
- een laadstatus bij het versturen van formulieren, zoals de case vraagt.

**Principes die ik overal heb aangehouden:**
- **Business rules zijn pure functies.** Geen database, geen HTTP, geen klok: alles komt binnen als parameter.
  Daardoor zijn ze in milliseconden en volledig voorspelbaar te testen, ook voor zomertijd of "precies nu".
- **De server is de baas.** Alles wat de browser controleert, controleert de server opnieuw. De API is direct aan te
  roepen, dus browservalidatie is alleen voor snelle feedback.
- **Elke fout heeft een bedoeling.** Een ongeldig kenteken, een onbekend kenteken, een RDW-storing en een al geboekt
  tijdslot zijn verschillende situaties, en krijgen elk een eigen, duidelijke melding en statuscode.
- **Aannames controleer ik, ik vertrouw er niet op.** De tests draaien op SQLite, maar de app op MySQL. Wat
  MySQL-specifiek is, is ook op een echte MySQL-database gecontroleerd: de migration (heen, terug en weer heen) en de
  unique indexen onder echte gelijktijdigheid. Dat is nodig: SQLite vergelijkt tekst anders en dwingt bijvoorbeeld geen
  kolomlengtes af, MySQL wel.
- **Kleine, betekenisvolle commits**, zodat de historie laat zien hoe het gegroeid is.

---

## Opzet & architectuur (± 1 u)

Drie projecten, bewust niet meer:

| Project | Inhoud | Waarom |
|---|---|---|
| `AutoInventory.Core` | Entities, `TestDriveRules`, `VehicleRules`, `LicensePlate`, `OpeningHours`, `Limits` | **Nul dependencies.** De regels die er écht toe doen, los van alle techniek. |
| `AutoInventory.Api` | Razor Pages, JSON-API, services, EF Core, RDW-client, logging | Alles wat met de buitenwereld praat. |
| `AutoInventory.Tests` | xUnit | Unit tests op Core en RDW; integratietests op API én pagina's. |

- **Twee voordeuren, één set regels.** De Razor Pages (voor bezoeker en dealer) en de JSON-API (gevraagd in de case)
  roepen dezelfde `VehicleService` en `TestDriveService` aan. Anders zou dezelfde logica twee keer bestaan en
  vroeg of laat uit elkaar lopen. De pagina's roepen de API ook niet via HTTP aan: dat zou een extra netwerkstap zijn
  binnen één proces, met dubbele foutafhandeling.
- **Geen repository-laag** bovenop EF Core. `DbContext` is al een unit of work; een repository eromheen zou bij deze
  omvang alleen indirectie toevoegen.
- **Minimal APIs** in plaats van controllers: de endpoints zijn dun (service aanroepen, uitkomst omzetten naar een
  statuscode), daar is geen controller-ceremonie voor nodig.
- **Razor Pages** in plaats van MVC: per pagina één model met handlers (`OnPostBook`, `OnPostSave`). Overzichtelijker
  dan controllers met losse views voor vier pagina's.

## Datamodel & persistentie (1–2 u)

- `Vehicle` (`LicensePlate`, `Brand`, `Model`, `Year`, `Mileage`, `Fuel`, `Price`, `Status`, `Branch`, `Version`) en
  `TestDrive` (`Name`, `Email`, `Phone`, `ScheduledAt`, `RequestedAt`).
- **Kenteken genormaliseerd** opgeslagen (`AB123C`) met een **unique index**. Zo kan dezelfde auto er nooit twee keer
  in staan, ook niet als twee medewerkers hem op exact hetzelfde moment toevoegen.
- **Enums als tekst** in de database: leesbaar bij het debuggen, en veilig als iemand later de volgorde aanpast.
- **Prijs als `decimal(10,2)`**: geld nooit als `double`.
- **Tijden altijd in UTC.** MySQL kent bij `datetime` geen tijdzone; één converter in de `DbContext` markeert alles wat
  uit de database komt als UTC, in plaats van dat in elke query te moeten onthouden.
- **Bouwjaar mag leeg zijn** (`null`) als de RDW het niet weet: "onbekend" moet in het datamodel ook echt onbekend
  zijn, niet een 0 die overal vertaald moet worden.
- **Veldlengtes op één plek** (`Core.Limits`). Validatie en kolomgroottes gebruiken dezelfde getallen, dus de API kan
  nooit iets accepteren wat de database daarna weigert.
- **Provider:** Oracle's `MySql.EntityFrameworkCore`. Pomelo is bekender, maar heeft geen versie voor EF Core 10;
  terugvallen op EF Core 9 zou maar tijdelijk zijn.
- **Twee migrations:** `InitialCreate` (schema + seed-data) en `UniqueTimeSlotPerVehicle`, die de index op
  (voertuig, starttijd) uniek maakt (zie *Robuustheid*). Die tweede heb ik met de hand aangepast, omdat EF het
  MySQL-probleem niet zelf oplost.
- **Seed-data:** zeven occasions, vooral VW-groep, één ingeruilde Toyota en één verkochte auto, zodat elke regel
  meteen te zien is.
- Lokaal draaien de migrations bij het opstarten, zodat de reviewer niets extra hoeft te doen. Dat staat alleen in de
  Development-configuratie: in productie hoort migreren in de deployment-pipeline, niet in elke app-instantie.

## API + RDW-koppeling + validatie (2 u) — *het hart*

**De RDW-flow:**
1. De dealer vult het kenteken in en kiest *Ophalen bij de RDW*. De pagina toont merk, model en bouwjaar, en vult de
   brandstof alvast in.
2. Bij opslaan stuurt het formulier alleen het kenteken en de velden van de dealer (km, brandstof, prijs, vestiging).
   **Merk, model en bouwjaar haalt de server zelf opnieuw op** voor het kenteken dat écht verstuurd wordt.
   - Waarom: de server is de bron van waarheid. Een gemanipuleerd formulier kan geen Ferrari op een Fiat-kenteken
     zetten, en als iemand het kenteken na het ophalen nog wijzigt, wordt nooit de vorige auto opgeslagen.
3. De API biedt hetzelfde (`GET /api/rdw/{licensePlate}`, `POST /api/vehicles`), via dezelfde service.

**De RDW-client** (dataset `m9d7-ebf2`):
- **Eerst lokaal controleren**: kenteken normaliseren (`ab-123-c` → `AB123C`) en het formaat checken. Een ongeldig
  kenteken geeft meteen een melding (400), zonder de RDW lastig te vallen.
- **Lege lijst van de RDW** = onbekend kenteken → 404 met een duidelijke melding.
- **Netwerkfout, timeout, foutstatus of onleesbare JSON** → één `RdwUnavailableException` → 502 "De RDW is op dit
  moment niet bereikbaar". De details gaan naar de log, niet naar de gebruiker.
- **Mapping naar mijn eigen model**: `merk` (met officiële schrijfwijze: `SKODA` → `Škoda`), `handelsbenaming` → model,
  `datum_eerste_toelating` (`yyyyMMdd`) → bouwjaar. Lange waarden worden afgekapt op de kolomgrootte: externe data mag
  mijn opslag nooit kunnen laten mislukken.

**Endpoints:**

| Methode | Route | Statuscodes |
|---|---|---|
| GET | `/api/vehicles?brand=&branch=&fuel=&status=&sort=&page=&pageSize=` | 200, 400 (ongeldige sortering of pagina) |
| GET | `/api/vehicles/{id}` | 200, 404 |
| POST | `/api/vehicles` | 201, 400, 404 (RDW), 409 (bestaat al), 502 |
| PUT | `/api/vehicles/{id}` | 200, 400, 404, 409 (intussen gewijzigd) |
| DELETE | `/api/vehicles/{id}` | 204, 404, 409 (er staan proefritten gepland) |
| GET | `/api/rdw/{licensePlate}` | 200, 400, 404, 502 |
| GET | `/api/brands`, `/api/branches`, `/api/opening-hours` | 200 |
| GET | `/api/vehicles/{id}/availability?date=` | 200, 404 |
| POST | `/api/vehicles/{id}/testdrives` | 201, 400, 404, 409 (tijdslot net bezet) |

- `GET /api/vehicles` geeft één pagina terug: `{ items, page, pageSize, totalCount, totalPages }`, standaard 20 per
  pagina en maximaal 50. Sorteren kan op merk (standaard), prijs (op- en aflopend), kilometerstand en bouwjaar.
  Sorteren en pagineren gebeurt in de database, met het `Id` als laatste sorteersleutel, zodat een auto nooit op twee
  pagina's tegelijk staat of ertussen wegvalt.
- Alle API-fouten zijn **RFC 7807 ProblemDetails**, met per veld een foutmelding. Elke client hoeft dus maar één
  foutvorm te kennen.
- De services geven een `ServiceResult` terug (Ok / NotFound / Invalid / Conflict / Unavailable). De API vertaalt dat
  op één plek naar een statuscode, de pagina's naar een melding bij het juiste veld.
- API-documentatie via OpenAPI + Scalar op `/scalar`, met per endpoint een omschrijving en de mogelijke statuscodes.

## Proefrit-logica + tests (1–2 u)

`TestDriveRules.Validate(request, vehicle, nowUtc, existingSlotsUtc, openingHours)`:
- **Verkocht voertuig** → geen proefrit.
- **De datum moet in de toekomst liggen.** Strikt: "precies nu" telt niet als toekomst.
- **Starttijd op het hele uur**, binnen de **openingstijden** in Nederlandse tijd (inclusief zomertijd), en de hele rit
  van een uur moet erin passen. Een proefrit om 17:00 bij sluiting om 17:30 kan dus niet.
- **Geen dubbele boeking** van dezelfde auto in hetzelfde tijdslot.
- **Naam, e-mail en telefoon** verplicht, met maximale lengtes. E-mail met een bewust pragmatische regex: een volledige
  RFC-controle is onhaalbaar en zegt niets over of het adres bestaat; dit vangt typefouten. Telefoon: alleen cijfers,
  spaties, `+`, `-` en haakjes, 10 tot 15 cijfers.
- **Alle fouten tegelijk terug**, niet alleen de eerste. De bezoeker wil niet vijf keer opnieuw versturen.

Waarom `nowUtc` en `openingHours` parameters zijn: dan kan een test zeggen "het is dinsdag 13:00" en precies nagaan wat
er gebeurt op zondag, net voor sluitingstijd of bij de overgang naar wintertijd. In de app komen ze uit `TimeProvider`
en de configuratie.

**Tests:**
- **Unit tests** op alle regels hierboven, inclusief de randen (precies nu, een minuut later, het laatste slot van de
  dag, zomer- versus wintertijd), plus kenteken, RDW-mapping en merknamen.
- **Integratietests** met `WebApplicationFactory`: de echte HTTP-pipeline met een in-memory database, een nep-RDW en een
  vaste klok. Elke test krijgt een verse database, zodat de volgorde er niet toe doet.

## Front-end met Razor Pages (2 u)

Vier pagina's, server-side gerenderd:
- **Overzicht** (`/`): kaarten en filters (merk, vestiging, brandstof, status), sorteren, en 6 auto's per pagina.
  Een GET-formulier, dus filters, sortering en pagina staan in de URL en een gefilterde lijst is te delen. De links
  "Vorige" en "Volgende" houden filters en sortering vast; een ander filter of een andere sortering begint weer op
  pagina 1.
- **Detail** (`/voertuig/{id}`): specificaties en het proefritformulier. Na het kiezen van een datum toont de pagina de
  vrije tijden van die dag; bezette tijden zijn uitgegrijsd. Zo kan een bezoeker nooit een tijd kiezen die daarna wordt
  geweigerd. Bij een verkochte auto staat er uitleg in plaats van een formulier. Onder "Beheer": wijzigen, markeer
  als verkocht, en verwijderen (met bevestiging, en niet zolang er proefritten gepland staan).
- **Toevoegen** (`/voertuig/toevoegen`): eerst het kenteken ophalen bij de RDW, dan de rest invullen en opslaan.
- **Wijzigen** (`/voertuig/{id}/wijzigen`): kilometerstand, brandstof, prijs, vestiging en status. Kenteken, merk, model
  en bouwjaar komen van de RDW en zijn niet te wijzigen. Het versienummer gaat als verborgen veld mee (zie *Robuustheid*).

Hoe de pagina's zich gedragen:
- **Foutmeldingen staan naast het veld**, wat er getypt was blijft staan, en de focus springt naar het eerste foute veld.
- **Post/Redirect/Get** na succes: verversen boekt niet nog een keer, en de bevestiging verschijnt één keer.
- **Anti-forgery tokens** op elk formulier, en Razor encodeert alle uitvoer: geen CSRF en geen XSS. Standaard
  encodeert Razor ook "Škoda" als `&#x160;koda`; ik sta alle Unicode-tekens toe, terwijl `<`, `>`, `&` en quotes
  geëncodeerd blijven.
- **Formulierwaarden worden cultuuronafhankelijk gelezen.** Anders wordt "21950.5" op een Nederlandse Windows-server
  219505; de weergave is wel Nederlands (`€ 21.950,50`).
- **Een foutpagina** voor 404 en 500, met een foutcode die in de log terug te vinden is. Nooit een stacktrace voor de bezoeker.
- **Alles werkt zonder JavaScript.** Een klein `site.js` scheelt alleen klikken: filters en sortering direct toepassen,
  tijden herladen bij een andere datum, bevestigen voor "verkocht" en "verwijderen", en een laadstatus tijdens het
  versturen: de knop toont "Bezig…" en alle knoppen van het formulier zijn even uitgeschakeld. Zo ziet de bezoeker dat
  er iets gebeurt (een RDW-aanroep kan tot 8 seconden duren) en kan hij niet dubbel versturen.

## Logging & foutafhandeling (± 1 u)

**Serilog** naar de console en een dagelijks rollend bestand in `logs/` (14 dagen bewaard), ingesteld via `appsettings.json`.
- **Een bootstrap-logger**, zodat ook een crash tijdens het opstarten (database onbereikbaar) in de log komt. Zo'n crash
  eindigt met exitcode 1, zodat Docker of een andere host ziet dat het misging.
- **Eén regel per request** (methode, pad, status, duur) via `UseSerilogRequestLogging`.
- **Gestructureerde logs** (`{LicensePlate}`, `{VehicleId}`). Doorsturen naar Seq of Application Insights is daardoor
  alleen een extra sink in de configuratie, geen codewijziging.
- **Onverwachte fouten** worden één keer gelogd met de volledige stacktrace. De API geeft een algemene 500
  (ProblemDetails), de website de foutpagina.
- **Niet alles is een fout.** Een bezoeker die halverwege wegklikt breekt zijn request af; dat log ik niet als
  serverfout. Een verloren race om een tijdslot is een waarschuwing, geen error.
- **Privacy (AVG):** geen namen, e-mailadressen of telefoonnummers van bezoekers in de logs, alleen veldnamen en meldingen.

---

## Ames-specifiek

| Keuze | Waarom |
|---|---|
| **Alle merken toegestaan**, de vijf dealermerken bovenaan in het filter | Ingeruilde auto's van andere merken zijn ook occasions. Alleen VW-groep toestaan zou een verkeerde business rule zijn. |
| **Proefritten alleen binnen openingstijden** | Een proefrit om 03:00 of op zondag is niet realistisch. De tijden staan in de configuratie, dus aanpassen kan zonder nieuwe release. De huidige tijden zijn een aanname, niet de echte Ames-tijden. |
| **Vestiging per auto** | Met zes locaties moet de bezoeker weten waar de auto staat. |
| **Officiële merknamen** | De RDW levert `SKODA`; op de site hoort `Škoda`. |
| **Geen Ames-logo of huisstijl** | Het is een case, geen kopie van hun website. |

## Robuustheid

**De RDW is een externe dienst die ik niet in de hand heb.** Daarom:
- **Timeouts, retries en een circuit breaker** (`Microsoft.Extensions.Http.Resilience`): 3 seconden per poging,
  8 seconden totaal, twee keer opnieuw proberen bij een tijdelijke fout. Als de RDW structureel faalt, stopt de app een
  tijdje met aanroepen en meldt hij de storing direct, in plaats van elke bezoeker 8 seconden te laten wachten. De
  breaker opent zodra minstens de helft van 5 of meer pogingen binnen 30 seconden mislukt; een test bewijst dat de RDW
  daarna niet meer wordt aangeroepen.
- **Een cache** als decorator rond de client: een gevonden kenteken 12 uur, een onbekend kenteken 10 minuten.
  Fouten worden nooit gecachet, zodat een herstelde RDW meteen weer gebruikt wordt. Omdat de rest van de app alleen
  `IRdwClient` kent, is de cache los te testen en zonder andere wijzigingen weg te halen.
- **Brandstof uit een tweede dataset** (`8ys7-d773`), tegelijk opgehaald zodat het geen wachttijd kost. Dit is een
  voorinvulling: de RDW ziet een mild hybrid als gewone benzineauto, dus de dealer kan het aanpassen. Faalt deze
  dataset, dan mist alleen de voorinvulling. Elke dataset heeft een eigen pipeline, zodat deze optionele dataset de
  verplichte voertuig-lookup nooit kan meeslepen.

**Dubbele boekingen.** Een controle in de code ("is dit slot nog vrij?") werkt, maar twee aanvragen op exact hetzelfde
moment kunnen er allebei langs. Daarom:
- **Vaste tijdslots op het hele uur.** Dan betekent "zelfde slot" gewoon "zelfde starttijd", en dat kan de database
  afdwingen met een **unique index** op (voertuig, starttijd).
- De controle in de code blijft voor een nette melding. De index is het vangnet: verliest een aanvraag de race, dan
  krijgt die een 409 "zojuist door iemand anders geboekt". Op MySQL getest met 16 gelijktijdige aanvragen op 8 slots:
  8 boekingen, nooit dubbel.
- Gevonden met een test: een test die een gelijktijdige boeking nabootst, liet eerst twee boekingen door. Pas daarna
  heb ik de index uniek gemaakt.
- De migration hiervoor heb ik met de hand aangepast: MySQL weigert een index te verwijderen die een foreign key nodig
  heeft. Een tijdelijke index houdt die foreign key gedekt tijdens de wissel; heen, terug en weer heen getest.

**Gelijktijdig wijzigen.** Elke auto heeft een versienummer. Wie een auto wijzigt of "markeer als verkocht" klikt op
een pagina die een oudere versie toont, krijgt "intussen door iemand anders gewijzigd" in plaats van stilletjes de
nieuwe prijs van een collega te overschrijven (optimistic concurrency). Op de wijzigpagina blijft de invoer dan staan,
met een link om de actuele gegevens te laden.

## Gevonden en opgelost

Elk probleem is opgelost samen met een test die zonder de oplossing faalt:

| Probleem | Oplossing | Commit |
|---|---|---|
| Twee gelijktijdige boekingen voor hetzelfde tijdslot kwamen er allebei door: de controle in de code ziet de andere aanvraag nog niet | Unique index op (voertuig, starttijd); de verliezer krijgt 409. De migration is met de hand aangepast voor MySQL | `Fix double booking of the same time slot` |
| `10:00:00.0005` gold als heel uur, waardoor de unique index te omzeilen was (MySQL slaat het op als een ander tijdstip) | Controle op exacte ticks | `Fix time slot check for sub-millisecond offsets` |
| De circuit breaker opende pas na 100 aanroepen binnen 30 seconden (de standaard), dus bij één dealer nooit | Drempel van 5 aanroepen, waarvan de helft mislukt | `Make RDW circuit breaker trip at low traffic` |
| De foutcode op de foutpagina stond niet in de logregels | De TraceId staat in elke logregel, en de foutpagina toont precies die code | onderdeel van de logging-commit |

## Ontwerpkeuzes voor randgevallen

Deze zitten er vanaf de eerste versie van hun stap in:
- **Veldlengtes op één plek** (`Core.Limits`), voor validatie én kolomgroottes: MySQL weigert nooit iets wat de API accepteerde.
- **E-mail met een pragmatische regex** in plaats van `MailAddress`, dat ook `Jan <jan@x.nl>` accepteert.
- **Unique index op kenteken:** gelijktijdig dezelfde auto toevoegen geeft 409, geen 500.
- **Versienummer als concurrency token:** een verouderde wijziging of "markeer als verkocht" geeft 409.
- **Verwijderen kan niet zolang er proefritten gepland staan**, anders zouden die stilletjes verdwijnen.
- **Eigen pipeline per RDW-dataset**, zodat de optionele brandstof-dataset de voertuig-lookup niet kan blokkeren.
- **Verse database per integratietest**, zodat de volgorde niet uitmaakt.
- **201 zonder `Location`-header:** er is bewust geen publiek endpoint om een boeking met persoonsgegevens op te vragen.
- **Een crash bij het opstarten eindigt met exitcode 1**, zodat Docker of een andere host ziet dat het misging.
- **Databasewachtwoord en automatisch migreren** alleen in `appsettings.Development.json`.
- **Expliciete `UseRouting()` ná de foutafhandeling**, zodat de foutpagina ook bij een 404 gevonden wordt.

## Bewust níet gedaan

- **Inloggen met rollen (JWT).** Toevoegen, wijzigen, verwijderen en "Markeer als verkocht" zijn daardoor nog voor
  iedereen bereikbaar. Dit is het eerste wat ik zou toevoegen.
- Integratietests tegen echte MySQL (Testcontainers) in plaats van alleen handmatig.
- Openingstijden per vestiging en feestdagen; een overzicht van aangevraagde proefritten voor medewerkers;
  een bevestigingsmail naar de bezoeker.

Zie ook de README → "Met meer tijd".
