# Siculingo

PWA mobile-first italiano -> catanese. Direzione grafica scelta: **A, Il sentiero**.

Stato: **Tappa 9 completata in locale — pacchetto di rilascio a €0/mese pronto e verificato** (tappe 2–9 chiuse in locale; **50/50 test SQL Server superati**). Account reali su ASP.NET Core Identity, EF Core 10 e SQL Server; registrazione con accettazione informativa privacy e termini beta (`GET /api/v1/system/legal`), accesso, recupero senza email, impostazioni profilo/password/privacy e cancellazione autonoma immediata con anonimizzazione segnalazioni/audit (`POST /api/v1/auth/delete`); percorso sequenziale **A — Il sentiero** con le cinque lezioni pilota approvate dal committente (40 esercizi pubblicati), runner interattivo end-to-end sui quattro formati e sessioni di ripasso libero con segnalazione esercizi; cuori (`5/5`, `-1` su errore nel percorso, `+1` ogni 4 ore, `+1` a ripasso completato, feature flag `Hearts:ConsumptionEnabled`), 3 missioni giornaliere e 2 settimanali, leghe settimanali `Bronzo / Argento / Oro`, amicizie con auto-accettazione reciproca e blocchi; ledger XP transazionale e idempotente con anti-farming, livelli 1–8, serie giornaliera `Europe/Rome`, sei trofei base; console protetta `Redazione / Admin` con ruoli `Editor` e `Admin`, archivio lessicale catanese, moderazione utenti/segnalazioni, registro audit e metriche di prodotto pseudonimizzate (`D1`/`D7`, durata lezioni, hotspot errori); PWA installabile (`manifest.webmanifest` + `sw.js` solo asset statici, banner stato offline, responsive verificato fino a `360 px`); hosting di produzione same-origin, template Bicep Azure a €0/mese senza container (App Service Linux `F1` + Azure SQL Free Offer con `AutoPause`), packaging `publish-release.ps1`, drill di backup/restore `verify-backup-restore.ps1`, workflow GitHub Actions OIDC `deploy-azure.yml` e runbook operativo `OPERATIONS_RUNBOOK.md`. Il prototipo visivo comparativo della Tappa 1 è archiviato sul branch dedicato `prototype/visual-guide`.

## Prerequisiti

- Windows con .NET SDK 10.0.204 o feature band 10 compatibile con `global.json`.
- SQL Server LocalDB 2019 o successivo e comando `SqlLocalDB` disponibile.
- Node.js 22.12+ e npm.

## Primo avvio

Dalla radice:

```powershell
.\tools\setup-local.ps1 -RunTests
dotnet run --project .\src\Siculingo.Api --launch-profile local
```

Il setup crea/avvia solo l'istanza dedicata `SiculingoDev`, applica le migrazioni al database `Siculingo_Development`, conserva la connessione negli user-secrets locali e pubblica in modo idempotente le cinque lezioni pilota approvate (`content\pilot-greetings.v1.json`). Non modifica `MSSQLLocalDB` o database preesistenti di altre applicazioni. Non inserire credenziali o connection string nei file versionati.

In un secondo terminale:

```powershell
Set-Location .\frontend\siculingo-web
npm ci
npm run dev
```

- Applicazione e account: `http://127.0.0.1:5173`.
- Stato servizi, solo in sviluppo: `http://127.0.0.1:5173/foundation`.
- Prototipo visivo storico (Tappa 1, varianti A/B/C): archiviato fuori da `main` sul branch dedicato [`prototype/visual-guide`](https://github.com/lcatarcia/Siculingo/tree/prototype/visual-guide).
- API: `http://127.0.0.1:5080/api/v1/system/status`.
- OpenAPI di sviluppo: `http://127.0.0.1:5080/openapi/v1.json`.
- Liveness: `/health/live`; readiness SQL e migrazioni: `/health/ready`.

Preview e diagnostica sono caricate solo in sviluppo e non vengono incluse nella build applicativa di produzione. L'app non simula connessioni riuscite: mostra gli errori se API o database non sono disponibili. Non vengono creati account dimostrativi o pubblicate bozze automaticamente.

## Account e sessioni

- Username di 3-24 caratteri ASCII: lettere, numeri e underscore; univoci anche cambiando maiuscole e minuscole. Password di 12-128 caratteri, senza obblighi artificiali di composizione; hashing standard ASP.NET Core Identity.
- Cookie HttpOnly, SameSite=Lax, scadenza assoluta a 30 giorni dal login e nessun rinnovo automatico. Secure e prefisso `__Host-` fuori da Development/Testing; solo lo sviluppo locale usa HTTP.
- Tutti i POST di `/api/v1/auth` richiedono cookie antiforgery e header `X-CSRF-TOKEN`, ottenuti tramite GET `/api/v1/auth/csrf`. Richiedere un nuovo token dopo login, registrazione, logout o recupero. Nessun token o password in localStorage.
- Logout corrente: elimina il cookie di questo browser. Logout globale e recupero: cambiano il security stamp; i cookie precedenti vengono rifiutati alla richiesta successiva, su ogni dispositivo, senza attendere intervalli di rinnovo.
- Il codice di recupero ha 256 bit casuali, viene mostrato solo alla registrazione o dopo un recupero riuscito e viene conservato nel database soltanto come hash SHA-256. Consumo, nuova password e rotazione del codice sono atomici e protetti dalla concorrenza. Il recupero revoca tutti gli accessi e rimuove il blocco da tentativi errati.
- Dopo il recupero occorre salvare il nuovo codice e accedere con la nuova password. Senza password e codice non e possibile il recupero autonomo; non e previsto invio di email.
- Limite locale per IP: 20 richieste a credenziali al minuto, risposta 429 e Retry-After; dopo 5 password errate l'account e bloccato per 15 minuti. I limiti in memoria valgono per la singola istanza, non sono ancora un sistema anti-abuso distribuito.
- Dal menu account, **Impostazioni account** permette di salvare un nome visualizzato fino a 40 caratteri, anche con accenti. Non cambia lo username di login; lasciandolo vuoto si usa di nuovo lo username. La modifica viene letta dagli altri dispositivi senza rinnovare i cookie.
- Il cambio password richiede quella attuale e una nuova password diversa di 12-128 caratteri. Chiude tutti gli accessi, compreso quello corrente; si accede nuovamente senza rinnovi impliciti. Nome visualizzato, username e codice di recupero non cambiano. Anche le password attuali errate concorrono al blocco temporaneo.

`GET /api/v1/learning/path` richiede accesso e restituisce corso/sezione/unita/lezione ordinati, filtrando i non pubblicati a ogni livello, con stato sequenziale per utente (`completed`, `available`, `locked`) ed eventuale `activeAttempt` in corso. Gli endpoint `/api/v1/learning/*` gestiscono avvio/ripresa/sostituzione tentativo (snapshot immutabile `LessonSnapshotJson`), lettura senza esposizione anticipata delle soluzioni, correzione server-side per i quattro tipi di esercizio (`choice`, `matching`, `guidedTranslation`, `fillBlank`), completamento atomico e idempotente con accredito ricompense e sblocco della lezione successiva, e abbandono esplicito.

## Gamification essenziale (XP, livelli, serie e trofei)

- **Ledger XP e anti-farming (`XpLedger`):** ogni accredito usa una chiave univoca `IdempotencyKey` dentro transazione serializzabile. Primo completamento lezione: **+2 XP** per risposta esatta + **+10 XP** lezione. Ripetizione/ripasso: **+5 XP** per ripasso completato fino a **3 ripassi premiati per giornata locale** (`Europe/Rome`), senza riaccredito per singolo esercizio. Bonus **+5 XP** per lezione perfetta (zero errori) e **+5 XP** per la prima lezione completata della giornata locale.
- **Livelli, serie e trofei:** soglie livelli 1–8 persistite in `LevelThresholds`; serie attuale e record calcolati sul fuso `Europe/Rome` (con gestione cambio giorno, ora legale e azzeramento dopo giorno saltato); sei trofei base in `Achievements` / `UserAchievements` come riconoscimenti puri (0 XP extra).
- **Endpoint gamification, cuori, missioni e social (Tappa 7):** `GET /api/v1/gamification/summary` (statistiche riconciliate con il ledger, barra livello, cuori con countdown rigenerazione, serie, ripassi premiati usati oggi e ultimi accrediti XP), `GET /api/v1/gamification/hearts`, `GET /api/v1/gamification/missions` (3 missioni giornaliere da **+10 XP** e 2 settimanali da **+25 XP**, idempotenti e non ricorsive), `GET /api/v1/gamification/achievements`, `POST /api/v1/learning/practice/start` (ripasso libero sempre disponibile anche a zero cuori, senza consumo su errore e con **+1 cuore** recuperato al completamento) e `/api/v1/social/*` (visibilita profilo `Public / FriendsOnly / Private`, ricerca utenti, richieste di amicizia con auto-accettazione reciproca, blocchi e classifiche settimanali `Bronzo / Argento / Oro` + filtro amici su fuso `Europe/Rome`).

## Unita pilota pubblicata e flusso bozze

`content\pilot-greetings.v1.json` contiene le cinque lezioni pilota da otto esercizi ciascuna (saluti, nomi, come stai, cortesia e dialogo riepilogativo) approvate dal committente il 18 novembre 2026 (`reviewStatus: "approved-by-owner"`). Ogni lezione include scelta multipla, abbinamento con coppie, traduzione con tessere e completamento frase.

Dopo il setup, dalla radice sono disponibili due comandi espliciti solo in Development:

```powershell
$pilot = (Resolve-Path .\content\pilot-greetings.v1.json).Path
dotnet run --project .\src\Siculingo.Api --launch-profile local -- --import-pilot $pilot
dotnet run --project .\src\Siculingo.Api --launch-profile local -- --publish-pilot $pilot
py -3 .\tools\export_pilot_review.py --output "$env:TEMP\siculingo-pilot-review.html"
```

`--import-pilot` carica corso/sezione/unita/lezioni come bozze non pubblicate (`EditorialDraftJson`); `--publish-pilot` verifica che il file abbia `reviewStatus: "approved-by-owner"` e popola `PublishedSnapshotJson` (marcando `EditorialState = Approved`) rendendo le lezioni visibili e avviabili sul percorso senza mai alterare tentativi gia in corso. Entrambi terminano senza avviare il server HTTP e non applicano migrazioni. Nuovi contenuti catanesi restano sempre in bozza fino a revisione linguistica approvata.

## Redazione, ruoli e amministrazione (Tappa 6)

Per promuovere il primo amministratore o redattore in sviluppo locale:

```powershell
dotnet run --project .\src\Siculingo.Api --launch-profile local -- --grant-role "<username>:Admin"
dotnet run --project .\src\Siculingo.Api --launch-profile local -- --grant-role "<username>:Editor"
```

Dopo l'assegnazione del ruolo occorre effettuare nuovamente il login nel browser per aggiornare i claim del cookie: comparira la scheda **Redazione** (`Editor`) o **Admin** (`Admin`).

- **`Editor`:** consulta indicatori e catalogo editoriale, crea/salva bozze strutturate di lezioni (senza toccare la versione online ne i tentativi attivi), invia lezioni a revisione linguistica e gestisce voci dell'archivio lessicale catanese (`LinguisticLexemes`: lemma, significato, contesto, registro, varianti ortografiche e fonte).
- **`Admin` (Revisore):** approva o rimanda in bozza lezioni e voci lessicali, pubblica sul sentiero solo lezioni con stato `Approved`, gestisce segnalazioni inviate dagli studenti dagli esercizi (`POST /api/v1/reports/exercises`), assegna/revoca ruoli, sospende/riattiva account con revoca immediata delle sessioni attive e consulta il registro `AdminAuditEntries`.

Il comando Python genera un documento HTML locale leggibile/stampabile con consegne, soluzioni e note di revisione, non servito dal frontend ne dalle API studenti.

## Contratti e verifiche

Con API locale avviata, dalla cartella frontend:

```powershell
npm run api:types
npm run build
npm run lint
```

I tipi in `src\shared\api\schema.d.ts` sono generati: non modificarli a mano. TypeScript 5.9.3 e allineato al peer range supportato da `openapi-typescript` 7.13.0.

Dalla radice, `dotnet test Siculingo.sln` esegue test su database temporanei con nomi `Siculingo_Test_<GUID>`, eliminati a fine test. La connessione di base arriva da `SICULINGO_TEST_DATABASE` o dagli user-secrets API; i test accettano solo LocalDB, mai un server di produzione.

Le migrazioni si applicano dal setup locale o da una procedura di deploy controllata, **non** all'avvio dell'API. Readiness restituisce 503 se non puo collegarsi o trova migrazioni non applicate; liveness resta indipendente da SQL.

## Distribuzione, backup/ripristino e CI/CD (Tappa 9)

La distribuzione predisposta adotta un'architettura **same-origin a €0/mese senza container**: **Azure App Service Linux (`F1` Free)** + **Azure SQL Database Free Offer** (`useFreeLimit: true`, `AutoPause` automatico senza addebiti imprevisti) + **Managed Identity Microsoft Entra**. Tutti i dettagli operativi, quote, upgrade opzionale (`B1` / `Basic`), primo Admin (`--promote-role`), PITR e rollback sono in **`OPERATIONS_RUNBOOK.md`**.

Comandi locali verificati dalla radice:

```powershell
# Genera il pacchetto singolo API + PWA statica per App Service (artifacts\publish\siculingo-release.zip)
.\tools\publish-release.ps1

# Esegue la prova completa di backup SQL Server, restore su DB pulito e DBCC CHECKDB
.\tools\verify-backup-restore.ps1

# Valida il template infrastrutturale Azure Bicep
az bicep build --file .\infra\main.bicep
az bicep build-params --file .\infra\main.bicepparam
```

- **Repository di riferimento:** [`https://github.com/lcatarcia/Siculingo`](https://github.com/lcatarcia/Siculingo).
- `.github\workflows\ci.yml`: build/lint frontend, test SQL su runner Windows e controllo di allineamento OpenAPI.
- `.github\workflows\deploy-azure.yml`: workflow manuale (`workflow_dispatch`, vincolato a `lcatarcia/Siculingo`, ambiente `beta`) con autenticazione passwordless Azure OIDC (`azure/login@v3`), test SQL, migrazione controllata (`--migrate-only`), deploy ZIP su App Service e verifica HTTPS `/health/live` + `/health/ready`.
- Nessun commit, repository remoto o risorsa Azure viene creato senza autorizzazione esplicita del committente. Dettagli e decisioni in `PIANO_SVILUPPO.md` e `OPERATIONS_RUNBOOK.md`.
