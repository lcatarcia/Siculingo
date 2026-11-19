# Siculingo - Piano operativo di sviluppo

Aggiornato: 19 novembre 2026  
Stato: **tappa 9 completata — infrastruttura Azure a €0/mese provisionata su `rg-siculingo-beta` (`westeurope`) e pipeline OIDC collegata a `lcatarcia/Siculingo`** (tappe 0–9 chiuse; **50/50 test di integrazione, hosting di produzione e carico su SQL Server reale superati**; Web App `siculingo-beta-app-lpyzen` `F1` Free + Azure SQL `Siculingo_beta` Free Offer con `AutoPause` attivi). Catalogo pilota pubblicato (40 esercizi approvati), percorso con sblocco sequenziale, sessioni di lezione e di ripasso libero con snapshot immutabile, quattro formati di esercizio, correzione server-side senza esporre soluzioni, ledger XP atomico e idempotente con anti-farming, livelli 1-8, serie giornaliera su fuso `Europe/Rome`, sei trofei base, cuori (`5/5`, `-1` su errore nel percorso, `+1` ogni 4 ore, `+1` a ripasso completato, feature flag `Hearts:ConsumptionEnabled`), 3 missioni giornaliere (+10 XP) e 2 settimanali (+25 XP), leghe settimanali `Bronzo / Argento / Oro` con promozioni/retrocessioni lazy al cambio settimana, amicizie con auto-accettazione reciproca, blocchi, impostazioni privacy, cancellazione autonoma immediata dell'account con conferma password e anonimizzazione delle segnalazioni/audit storici, informativa privacy e termini beta (`GET /api/v1/system/legal`) con accettazione esplicita alla registrazione, metriche di prodotto pseudonimizzate in console Admin (funnel, durata mediana vs target 3-5 min, coorti D1/D7, hotspot errori esercizi), manifest e service worker PWA solo-statico (`sw.js`), hosting di produzione same-origin (`ProductionHosting` + `OperationalCli`), template Bicep a costo zero provisionato su Azure (`rg-siculingo-beta`), script di packaging (`publish-release.ps1`) e drill di backup/ripristino SQL verificato (`verify-backup-restore.ps1`), workflow GitHub Actions OIDC (`deploy-azure.yml`) e runbook operativo (`OPERATIONS_RUNBOOK.md`). Direzione **A - Il sentiero** attiva; prototipo storico archiviato su `prototype/visual-guide`.

## 1. Obiettivo e metodo

Realizzare Siculingo, piattaforma mobile-first per imparare il catanese partendo dall'italiano, con identita visiva propria e Ficodi come mascotte.

Questo piano traduce il documento allegato `Siculingo_Piano_Prodotto_e_Sviluppo_Beta_v1.2.md` e le tavole grafiche fornite in tappe eseguibili. Il documento originale resta il riferimento di prodotto; qui vengono esplicitate dipendenze, risultati verificabili e decisioni ancora aperte.

All'avvio la cartella di lavoro era vuota. Ora contiene il piano, il kit grafico, gli strumenti di estrazione, il frontend React/Vite/TypeScript, la solution .NET e i test SQL. Git locale e stato inizializzato sul branch `main` dopo autorizzazione, senza commit o destinazione remota.

Procederemo una tappa alla volta:

1. Confermiamo obiettivo e decisioni della tappa.
2. Implementiamo una porzione utilizzabile, senza aggiungere funzioni fuori perimetro.
3. Verifichiamo comportamento, integrazione e resa grafica sui dispositivi previsti.
4. Presentiamo il risultato per approvazione prima della tappa successiva.

Le decisioni approvate e gli eventuali cambiamenti di perimetro verranno aggiornati in questo documento. La tappa 0 e **Completata per l'avvio**; per la tappa 1 sono confermati campione iniziale e direzione A, mentre l'approvazione puntuale del kit esteso resta aperta. Le tappe 2, 3, 4, 5, 6, 7, 8 e l'intero pacchetto tecnico/operativo della tappa 9 sono **Completati e verificati in locale**; l'esecuzione effettiva su GitHub remoto e Azure resta in attesa di esplicita conferma finale dell'utente. Le decisioni differite nella sezione 9 restano vincolanti prima del rilascio pubblico.

## 2. Perimetro e traguardi

### Traguardo A - Prima versione completa per test interni

Account con username e password e accesso da piu dispositivi, percorso, quattro tipi di esercizio, correzione server-side, progressione persistente, XP, livelli, serie giornaliera, dashboard, profilo, trofei base e amministrazione minima dei contenuti.

Questa versione deve dimostrare il ciclo completo: entrare, imparare, ricevere feedback, vedere il progresso e tornare il giorno successivo.

### Traguardo B - Beta pubblica monitorata

Aggiunge cuori secondo la regola approvata, obiettivi giornalieri e settimanali, amicizie, blocchi, segnalazioni, classifiche, impostazioni privacy, metriche essenziali, gestione operativa e distribuzione Azure. Registrazione aperta fin dall'inizio della beta, senza inviti o codici di accesso; 20-50 tester restano un obiettivo iniziale di raccolta feedback, non un limite agli iscritti.

La separazione A/B risolve una contraddizione del documento originale: social e missioni compaiono sia nella beta sia tra le priorita successive. La proposta e testarli dopo il nucleo didattico, ma prima di dichiarare completa la beta descritta dai criteri di accettazione.

### Dopo la beta

Ascolto con audio revisionato, riordino parole, personalizzazione della mascotte, ricompense cosmetiche, sfide, ripasso adattivo, notifiche e animazioni piu elaborate.

Restano esclusi dal primo rilascio scrittura libera, valutazione della pronuncia, chat, pagamenti, pubblicita, AI conversazionale, marketplace e app native.

## 3. Direzione visiva

### Identita

Usare le tavole allegate come riferimento per mascotte, palette, logo e icone. Riprendere da Duolingo i principi di usabilita richiesti: caratteri arrotondati, gerarchie nette, ampi spazi, percorso a nodi, pulsanti volumetrici, feedback immediato e tono incoraggiante.

Non copiare logo, mascotte, font proprietari o schermate identiche di Duolingo. Il risultato deve essere riconoscibile come Siculingo, non una riproduzione del suo marchio.

### Palette approvata

La tavola "Character Design Kit v1.0 / Asset Pack Completo" e stata approvata come riferimento principale il 17 novembre 2026. Per ciascun ritaglio scegliere comunque la versione piu nitida tra tutte le immagini fornite.

| Ruolo | Colore | Utilizzo |
| --- | --- | --- |
| Viola principale | `#6A00F4` | Brand, azioni principali, navigazione attiva |
| Verde Ficodi | `#8DDC3E` | Progresso, conferme, elementi della mascotte |
| Giallo | `#FFCD00` | Premi, energia, accenti |
| Corallo | `#FF6B8B` | Accenti e dettagli; errori solo dopo verifica contrasto |
| Crema | `#FFF4E6` | Superfici e sfondo chiaro |
| Azzurro | `#4EC9FF` | Informazioni ed elementi secondari |

Le altre tavole contengono valori e colori aggiuntivi non sempre coerenti: non mescolarli automaticamente. Testi, bordi, stati disabilitati e varianti scure saranno definiti nel design system con contrasto adeguato. Non usare bianco su verde o giallo senza verifica.

### Tipografia

Scelta approvata il 17 novembre 2026: **Nunito** per titoli, pulsanti e interfaccia; **Inter** per testi estesi, spiegazioni e pannello amministrativo. Verificare licenze e disponibilita dei pesi prima di integrarli; preferire font ospitati insieme all'app.

"Siculingo Rounded" compare come etichetta nella tavola, ma non e stato fornito un file font: non lo consideriamo una risorsa installabile. Il logo resta un asset grafico distinto.

### Componenti e comportamento

- Pulsanti arrotondati con bordo inferiore/ombra, stati premuto, caricamento e disabilitato.
- Card semplici e percorso didattico come centro della dashboard.
- Feedback corretto/errato con icona, testo e spiegazione, non soltanto colore.
- Navigazione principale: Impara, Classifica, Amici, Trofei, Profilo; mostrare solo le sezioni implementate.
- Cuori, serie e XP leggibili senza appesantire la schermata lezione.
- Supporto tastiera, focus visibile, lettori di schermo e preferenza movimento ridotto.
- Atmosfera siciliana tramite Ficodi e dettagli mirati; evitare sfondi illustrati dietro testi ed esercizi.

## 4. Piano asset: dalle tavole ai PNG

Le immagini allegate sono tavole raster composite, non un pacchetto di elementi gia separati. La dicitura "1024x1024" stampata in una tavola non garantisce che l'icona raffigurata abbia quella risoluzione disponibile.

### Procedura

1. Conservare gli originali senza modificarli e verificare duplicati, dimensioni e diritti d'uso.
2. Catalogare gli elementi e scegliere, per ciascuno, la tavola con resa e risoluzione migliori.
3. Eseguire prima un campione: Ficodi frontale, una posa, una piccola icona.
4. Ritagliare escludendo etichette, cornici, elementi vicini e decorazioni estranee.
5. Scontornare gli elementi isolabili e produrre PNG RGBA con trasparenza reale.
6. Rifinire aloni e bordi su sfondi chiari, scuri e viola; mantenere raggi, coriandoli o ombre solo se intenzionali.
7. Uniformare proporzioni, margini e punto di appoggio senza deformare la mascotte.
8. Registrare sorgente, coordinate del ritaglio, trasformazioni, dimensioni utili e utilizzo previsto.
9. Generare le varianti di consegna necessarie, mantenendo i PNG master; ottimizzare il formato web quando utile.

Non rimuovere gli sfondi con una semplice soglia sul bianco: occhi e riflessi rischiano di diventare trasparenti. Un ritaglio rettangolare non sara presentato come PNG scontornato.

### Primo set necessario

| Gruppo | Elementi | Prima destinazione |
| --- | --- | --- |
| Brand | Logo principale, variante chiara/scura se recuperabile | Accesso, navigazione |
| Mascotte | Frontale/saluto, legge, felice, incoraggiante, vittoria | Onboarding, percorso, lezione, riepilogo |
| Icone | Home, lezioni/libro, esercizi, coppa, obiettivi, fiamma, cuore, amici | Navigazione e statistiche |
| Premi | Badge base, stella, conferma, XP | Trofei e riepiloghi |
| App | Icona standard, favicon, variante maskable | Installazione PWA, browser |

Gemme, costumi e altre icone possono essere estratti successivamente; non saranno mostrati controlli per funzioni inesistenti.

Percorsi previsti: `assets\source` per gli originali, `assets\manifest.json` per provenienza e ritagli, `frontend\siculingo-web\public\assets` per gli asset pronti all'uso.

### Limiti e approvazione

Le piccole icone nelle tavole potrebbero non essere sufficienti per schermi ad alta densita; il logo puo avere uno sfondo difficile da separare. Prima di elaborare tutto il set approveremo il campione alla dimensione effettiva d'uso.

Se un elemento non e recuperabile con qualita adeguata, lo segnaleremo e richiederemo il sorgente ad alta risoluzione oppure l'approvazione per un'alternativa coerente. Non promettiamo dettagli o fotogrammi di animazione che le immagini non contengono.

## 5. Architettura e regole trasversali

- Frontend React, Vite e TypeScript, organizzato per feature; PWA mobile-first confermata.
- Backend ASP.NET Core 10 / C#, monolite modulare a vertical slice.
- Entity Framework Core 10 con provider SQL Server; Azure SQL in distribuzione.
- API sotto `/api/v1`, OpenAPI come fonte dei tipi TypeScript generati.
- Nessun microservizio, repository generico, Redis, broker o worker separato senza necessita misurata.
- Database locale separato dalla beta, migrazioni versionate e applicazione controllata.
- Orari persistiti in UTC; giorni utente inizialmente `Europe/Rome`; settimana classifica unica esplicita.
- Backend autorevole per risposte, sblocchi, cuori, XP, missioni e trofei.
- Ledger XP e vincoli univoci; completamento transazionale e idempotente.
- Le API degli esercizi non espongono soluzioni o marcatori di correttezza prima della risposta.
- Errori espliciti e coerenti, niente successi simulati in caso di rete o database indisponibili.

### Decisioni tecniche da chiudere prima delle rispettive implementazioni

**Credenziali e sessione browser:** approvato l'accesso con username e password da piu dispositivi, in sostituzione del solo token dispositivo previsto dal documento originale. Il 17 novembre 2026 il committente ha confermato **cookie HttpOnly con protezione CSRF**, frontend/API same-origin e **30 giorni dal login, senza rinnovo automatico**. Implementazione con ASP.NET Core Identity, cookie Secure fuori dallo sviluppo, nessun token in `localStorage`, logout del browser corrente e comando di revoca globale. Logout globale e recupero invalidano le credenziali precedenti alla richiesta successiva su tutti i dispositivi. Accesso amministrativo separato da implementare nella tappa 6; configurazione key ring, HTTPS e proxy da completare prima del deploy.

**Recupero account:** generare il codice con CSPRNG e alta entropia; mostrarlo alla registrazione con istruzioni per conservarlo e salvare lato server solo un verificatore crittografico, mai il codice in chiaro o nei log. Il codice permette di impostare una nuova password, non di conoscere quella precedente. Il suo utilizzo deve essere atomico e monouso, con emissione di un nuovo codice e revoca delle sessioni precedenti; proteggere il flusso con rate limiting ed errori che non rivelino l'esistenza di un account. Comunicare che la perdita di password e codice impedisce il recupero autonomo. Definire i dettagli del flusso nella tappa 3.

**Contenuti pubblicati:** approvato il 18 novembre 2026 il salvataggio di uno **snapshot immutabile della lezione su ciascun tentativo** (`LessonAttempt.LessonSnapshotJson`, copiato da `Lesson.PublishedSnapshotJson` all'avvio). Eventuali modifiche editoriali o ripubblicazioni valgono per i nuovi tentativi senza alterare le sessioni gia in corso.

**Cuori - regola approvata il 17 novembre 2026:** massimo 5; ogni risposta errata nel percorso consuma 1 cuore fino a zero; rigenerazione di 1 cuore ogni 4 ore fino al massimo. A zero si puo terminare la lezione gia iniziata senza perdere progresso o premi; non si possono iniziare altre lezioni del percorso finche non torna disponibile almeno un cuore. Il ripasso resta sempre disponibile. Il consumo e disattivabile tramite feature flag durante i test. Backend autorevole e aggiornamenti concorrenti non devono permettere saldi negativi o consumi duplicati. Premi del ripasso e coordinamento dei tentativi su piu dispositivi restano da precisare prima dell'implementazione.

**Ricompense e trofei - regola approvata il 18 novembre 2026:** sul **primo completamento** di una lezione vengono accreditati **+2 XP per ogni risposta corretta** e **+10 XP di completamento**; dal **secondo completamento in poi** (ripasso) non si riaccreditano gli esercizi singoli ma **+5 XP per ripasso completato** fino a un massimo di **3 ripassi premiati per giornata locale** (`Europe/Rome`). Si aggiungono **+5 XP per lezione perfetta** (zero errori) e **+5 XP per la prima lezione completata della giornata locale**. I **trofei** sono riconoscimenti puri sul profilo (0 XP extra) per evitare ricompense ricorsive.

## 6. Roadmap eseguibile

Le finestre temporali sono indicative per un piccolo team con contenuti e decisioni disponibili. Sono tappe di risultato, non scadenze garantite.

| Tappa | Periodo indicativo | Dipendenze | Risultato |
| --- | --- | --- | --- |
| 0. Decisioni di avvio | Settimana 1 | Nessuna | Perimetro e criteri approvati |
| 1. Asset e design system | Settimane 1-2 | 0 | Campione approvato, kit visivo utilizzabile |
| 2. Fondazioni tecniche | Settimane 1-2 | 0 | App, API e database locali avviabili |
| 3. Account e percorso | Settimana 3 | 1, 2 | Utente riconosciuto e percorso reale |
| 4. Prima lezione end-to-end | Settimane 4-5 | 3 | Quattro tipi di esercizio e progresso persistito |
| 5. Gamification essenziale | Settimane 6-7 | 4 | XP, livelli, serie, trofei e dashboard |
| 6. Contenuti e amministrazione | Settimane 3-8 | 3; chiusura dopo 5 | Catalogo revisionato e gestione protetta |
| 7. Motivazione e social | Settimane 8-10 | 5, moderazione di 6 | Cuori, missioni, amici e classifiche |
| 8. Preparazione al rilascio | Settimane 10-11 | 6, 7 | Prodotto verificato e gestione operativa pronta |
| 9. Distribuzione e beta | Settimana 12+ | 8 | Beta pubblica monitorata online |

Le tappe 1 e 2 possono avanzare in parallelo; la preparazione linguistica parte dalla tappa 0. Non rimandare tutta la UI o tutti i controlli di qualita alle ultime settimane.

### Tappa 0 - Decisioni di avvio

**Attivita:** confermare PWA, accesso utente, cuori e catalogo iniziale; assegnare revisione linguistica; scegliere la tavola grafica principale e definire se la registrazione beta richiede inviti.

Catalogo approvato il 17 novembre 2026: una unita pilota di 5 lezioni su saluti e presentazioni, da 8-12 esercizi ciascuna, da validare prima dell'espansione. Obiettivo beta: 30-40 lezioni revisionate, non prerequisito della prima dimostrazione.

**Uscita raggiunta il 17 novembre 2026 per avviare le tappe 1-2:** decisioni di prodotto e grafica registrate; revisione linguistica a carico del committente prima della pubblicazione; beta a registrazione aperta. Convenzioni ortografiche, dettagli tecnici di autenticazione, privacy, repository remoto e hosting vengono chiusi prima delle rispettive attivita, come indicato nella sezione 9.

### Tappa 1 - Asset e design system

**Attivita:** campione di estrazione, approvazione della qualita, primo set PNG, token colore e tipografia, pulsanti, card, indicatori, risposte, feedback e navigazione.

Preparare le schermate di riferimento: ingresso, percorso, esercizio e riepilogo. Usare componenti riutilizzabili e dati dimostrativi chiaramente separati dalle funzionalita reali.

**Uscita:** direzione grafica approvata su mobile e desktop; asset senza etichette o sfondi accidentali; nessuna dipendenza da font non forniti.

#### Campione 01 - Preparato il 17 novembre 2026

Tre PNG RGBA trasparenti sono disponibili in `frontend\siculingo-web\public\assets`:

| Asset | File | Canvas PNG | Dettaglio utile nativo |
| --- | --- | --- | --- |
| Ficodi frontale | `mascot\ficodi-front.png` | 224 x 256 px | 175 x 234 px |
| Ficodi studia | `mascot\ficodi-study.png` | 224 x 256 px | 157 x 171 px |
| Coppa | `icons\trophy.png` | 80 x 80 px | 52 x 46 px |

Il canvas include spazio trasparente; non rappresenta una maggiore risoluzione del soggetto. Nessun asset e stato ingrandito. Uso iniziale suggerito: canvas mascotte a 112 x 128 CSS px e coppa a 40 x 40 CSS px su display 2x; per elementi hero di grandi dimensioni servono originali piu risoluti.

La tavola `assets\review\sample-assets-review.png` confronta ritagli sorgente, trasparenza a scacchi, sfondo crema e sfondo viola scuro. Le mascotte sono mostrate a scala pixel 1:1; la coppa al 200% esclusivamente per ispezione.

Gli originali sono conservati in `assets\source`: le quattro immagini allegate corrispondono a tre file distinti, con due copie identiche della prima tavola. `assets\manifest.json` registra hash SHA-256, sorgenti, coordinate, correzioni delle maschere, dimensioni e stato di approvazione. Le immagini piu nitide per questi campioni provengono dalle prime due tavole; la terza resta il riferimento principale di identita e palette.

Estrazione locale riproducibile con `py -3 .\tools\extract_sample_assets.py`, che richiede Pillow e NumPy. Lo script usa gli originali gia copiati, conserva i bianchi di occhi e pagine e separa le aperture degli occhiali e dei manici della coppa. Non invia immagini a servizi esterni e non sostituisce la revisione visiva.

**Stato:** campione approvato dal committente il 17 novembre 2026, inclusi resa proposta e limiti di risoluzione comunicati. L'approvazione riguarda i tre PNG presentati, non gli asset estratti successivamente. Il manifest conserva l'approvazione finche il relativo hash non cambia; una versione modificata richiede nuova approvazione.

#### Kit esteso e schermate guida - Preparati il 17 novembre 2026

Sono stati aggiunti 15 PNG trasparenti: logo, tre reazioni/pose di Ficodi (felice, orgoglioso, vittoria), sette icone (home, libro, esercizi, fiamma, cuore, amici, obiettivo) e quattro badge (scudo, stella, conferma, XP). Totale disponibile: 18 PNG, inclusi i tre campioni gia approvati.

Provenienza, coordinate, hash e limiti di risoluzione dei nuovi asset sono in `assets\kit-manifest.json`; la tavola `assets\review\visual-kit-review.png` li raccoglie su sfondo a scacchi. Le reazioni facciali sono teste, non pose a figura intera: non ricostruiscono parti assenti dalle tavole. Rigenerazione: `py -3 .\tools\extract_visual_kit.py`. I tre campioni approvati non vengono riscritti da questo comando.

Nunito e Inter sono ospitati localmente in `frontend\siculingo-web\public\assets\fonts`, insieme alle licenze SIL Open Font License provenienti dal repository ufficiale Google Fonts. Non vengono richiesti font a servizi esterni durante l'uso della preview.

Il frontend React/Vite/TypeScript contiene un prototipo esplicitamente separato in `src\features\visual-prototype`. La domanda di design e: quale organizzazione del percorso rende piu chiaro il prossimo piccolo passo?

| Schermata | Contenuto della guida |
| --- | --- |
| Ingresso | Identita, promessa del prodotto, Ficodi e ingresso nella demo senza creare account |
| Percorso | Cinque lezioni di esempio, statistiche e obiettivo giornaliero fittizi |
| Esercizio | Tre domande dimostrative, selezione, correzione locale, feedback e conferma di uscita |
| Riepilogo | Risultato coerente con le risposte demo, oppure esempio esplicitamente indicato se aperto direttamente |

Il percorso ha tre alternative strutturali confrontabili, senza cambiare palette o contenuti:

- **A - Il sentiero:** nodi lungo un percorso, proposta principale vicina alla direzione richiesta.
- **B - Il quaderno:** indice editoriale ordinato delle lezioni.
- **C - Un passo alla volta:** una sola lezione in primo piano e invito all'azione centrale.

Preview: `http://127.0.0.1:5173/prototype?screen=percorso&variant=A`. I parametri `screen=ingresso|percorso|esercizio|riepilogo` e `variant=A|B|C` identificano le viste. La barra superiore apre le quattro schermate; la barra flottante inferiore cambia il layout del percorso. Le frecce della tastiera cambiano variante quando non si sta interagendo con un controllo. L'intero prototipo e il relativo collegamento sono disponibili solo in sviluppo e non vengono inclusi nella build di produzione.

Avvio locale dalla cartella `frontend\siculingo-web`: `npm run prototype`. Sono disponibili anche `npm run build` e `npm run lint`. Il server della preview e locale, non una distribuzione Azure.

**Limiti espliciti del prototipo:** nessun account reale, nessuna chiamata API, nessun accesso al database, nessuna persistenza, nessun XP assegnato e nessuna PWA installabile gia implementata. Le voci social non disponibili sono disabilitate; i testi catanesi restano bozze da revisionare. Tre esercizi servono esclusivamente alla demo, non modificano il requisito di 8-12 esercizi per le lezioni reali. La homepage tecnica introdotta nella tappa 2 e distinta da questa demo.

La preview e stata verificata a larghezze mobile e desktop, con font locali, assenza di immagini mancanti e flusso risposta errata/corretta fino al riepilogo. La validazione completa di accessibilita e browser reali resta parte delle tappe successive.

**Stato:** il committente ha scelto **A - Il sentiero** e autorizzato il passaggio alle fondazioni tecniche. I nuovi asset restano in attesa di approvazione puntuale nel relativo manifest: la scelta del layout non modifica automaticamente gli hash approvati. Il prototipo comparativo (varianti A/B/C) non entra su `main`: e stato archiviato come fonte primaria sul branch dedicato [`prototype/visual-guide`](https://github.com/lcatarcia/Siculingo/tree/prototype/visual-guide) (commit `c61159c`), mentre `main` conserva esclusivamente l'applicazione reale con la direzione validata **A - Il sentiero**.

### Tappa 2 - Fondazioni tecniche

**Attivita:** inizializzazione Git locale, solution e frontend, database locale, prima migrazione, configurazione ambienti, OpenAPI e tipi generati, health check, error handling e struttura dei test.

Predisporre pipeline build/test; pubblicazione del repository remoto e collegamento CI solo dopo scelta di account e destinazione. Git locale e stato autorizzato e inizializzato, senza staging, commit o remoti. Nessuna risorsa Azure viene creata in questa tappa.

**Uscita:** una nuova installazione puo avviare frontend, API e database con istruzioni riproducibili; nessun segreto nei file versionati.

**Risultato locale del 17 novembre 2026:**

- Solution `Siculingo.sln`: API, Domain, Infrastructure e test di integrazione; .NET 10 ed EF Core 10.
- Istanza SQL Server LocalDB dedicata `SiculingoDev`, database `Siculingo_Development`; prima migrazione `20261117161904_InitialLearningFoundation`, con modello corso minimo e codice univoco senza distinzione tra maiuscole e minuscole. Nessuna lezione o utenza di esempio inserita.
- Configurazione di connessione negli user-secrets locali; nessuna connection string nei sorgenti. Migrazioni applicate esplicitamente dal setup, mai automaticamente all'avvio API.
- Endpoint di stato `/api/v1/system/status`, liveness `/health/live` e readiness `/health/ready`. Readiness richiede SQL raggiungibile e schema aggiornato; errori `ProblemDetails`, dettagli diagnostici nei log e non nelle risposte pubbliche.
- OpenAPI di sviluppo `/openapi/v1.json`; tipi TypeScript generati in `frontend\siculingo-web\src\shared\api\schema.d.ts`. TypeScript 5.9.3 allineato al generatore OpenAPI; proxy Vite same-origin per API e health check.
- Homepage tecnica reale con connessione API/SQL, caricamento, errore e riprova, ora spostata su `/foundation` per lasciare la radice agli account. Demo grafica separata su `/prototype`; entrambe solo in sviluppo.
- Test SQL su database temporanei isolati: readiness, schema non migrato, vincolo corso e risposta API 404. Workflow `.github\workflows\ci.yml` predisposto per build, lint, test SQL e allineamento dei contratti; esecuzione remota ancora da collegare.

**Avvio riproducibile:** dalla radice `.\tools\setup-local.ps1 -RunTests`, poi `dotnet run --project .\src\Siculingo.Api --launch-profile local`. In un altro terminale, da `frontend\siculingo-web`, `npm ci` e `npm run dev`. Dettagli di prerequisiti, contratti e verifiche in `README.md`.

**Non inclusi nella tappa 2:** autenticazione, percorso persistito dell'utente, esercizi reali, XP e installabilita PWA; le aggiunte della tappa 3 sono registrate sotto. Nessun provisioning o deploy remoto.

### Tappa 3 - Account e percorso

**Attivita:** username normalizzato e univoco, registrazione con password e codice di recupero, login da piu dispositivi, reimpostazione password tramite codice senza email, sessioni e revoca, impostazioni, modello corso/sezione/unita/lezione, lettura percorso e contenuti pilota.

Proteggere dalla concorrenza la registrazione dello stesso username. Il catalogo in bozza non deve essere visibile agli utenti. Comunicare le condizioni del recupero account prima della registrazione e rendere esplicita la necessita di conservare il codice.

**Uscita:** un utente crea l'account, ricarica la pagina, resta riconosciuto e vede il percorso persistito; puo accedere da un secondo dispositivo con le proprie credenziali; non puo impersonare altri inserendone il solo username. Puo reimpostare la password con un codice valido; codice usato e sessioni precedenti non sono piu validi.

**Prima porzione implementata il 17 novembre 2026:**

- Registrazione con username normalizzato/univoco e password, login, ripristino sessione al caricamento, logout corrente e globale. Cookie HttpOnly di 30 giorni assoluti, protezione CSRF anche su registrazione/login/recupero, risposte API 401/403 senza redirect HTML.
- Codice di recupero casuale da 256 bit mostrato una sola volta e conservato come hash; conferma esplicita di salvataggio prima di proseguire. Recupero atomico e monouso con nuova password, nuovo codice, revoca immediata alla richiesta successiva e rimozione dell'eventuale blocco account.
- Rate limit per IP e blocco temporaneo dopo password errate; errori di recupero che non distinguono utente inesistente da codice non valido. Nessuna email o persistenza browser di password/token.
- Migrazione `20261117164852_AccountsAndPublishedLearningPath`: account Identity, verificatori di recupero e gerarchia corso/sezione/unita/lezione. Endpoint protetto `/api/v1/learning/path`, ordinato e filtrato per pubblicazione a ogni livello.
- Interfaccia reale su `/`: accesso, registrazione, recupero, conferma codice e area percorso. Catalogo vuoto esplicito quando non ci sono lezioni pubblicate; nessuna bozza trasformata in contenuto disponibile, nessun progresso o XP fittizio.

**Seconda porzione implementata il 18 novembre 2026:**

- Impostazioni approvate dal committente: nome visualizzato e cambio password, senza anticipare controlli audio/notifiche/obiettivi non funzionanti. Nome visualizzato fino a 40 caratteri, distinto dallo username, persistito e letto su tutti i dispositivi; vuoto significa usare lo username.
- Cambio password con verifica di quella attuale, conferma client della nuova, CSRF, limiti ai tentativi e revoca delle sessioni precedenti alla richiesta successiva. Anche il browser corrente deve effettuare un nuovo login: nessun rinnovo implicito dei 30 giorni. Username e codice di recupero restano invariati.
- Migrazione `20261118082009_AccountProfileAndEditorialDrafts`, con nome visualizzato e campo editoriale privato sulle lezioni.
- `content\pilot-greetings.v1.json`: cinque lezioni da otto esercizi ciascuna, per un totale di 40. Tutti e quattro i formati sono presenti in ogni lezione, con coppie per gli abbinamenti e tessere/soluzioni separate per le traduzioni guidate. I payload sono **editoriali**, non contratti delle future API esercizio.
- Import locale esplicito `--import-pilot`, solo in Development: tutte le entita restano non pubblicate. Ripetere lo stesso import non duplica; dati diversi o gia pubblicati bloccano l'operazione senza sovrascrivere. Non parte all'avvio e non applica migrazioni.
- Documento di revisione HTML generabile con `tools\export_pilot_review.py`, con consegne, opzioni, soluzioni proposte e note per ogni lezione. Non e un endpoint pubblico e non entra nel frontend distribuito.

Lezioni approvate: **Ciao e arrivederci**, **Come ti chiami?**, **Come stai?**, **Le parole gentili**, **La prima chiacchierata**. Il 18 novembre 2026 il committente ha approvato esplicitamente le cinque lezioni pilota cosi come preparate (`reviewStatus: "approved-by-owner"`), autorizzandone la pubblicazione locale tramite comando esplicito `--publish-pilot` (consentito solo su file approvati).

**Stato tappa 3:** **Completata il 18 novembre 2026**, con account, recupero senza email, impostazioni e primo catalogo approvato pubblicato sul sentiero.

### Tappa 4 - Prima lezione completa

**Attivita:** avvio tentativo, esercizi e risposte, correzione backend, spiegazione, avanzamento, uscita con conferma, riepilogo e sblocco della lezione successiva.

Supportare scelta multipla, abbinamento, traduzione guidata e completamento frase. Definire payload specifici per tipo: gli abbinamenti non vanno modellati come semplici scelte multiple. Nessuna valutazione automatica della scrittura libera.

**Uscita:** un utente completa una lezione da 8-12 esercizi; il progresso sopravvive al ricaricamento. Retry e invii concorrenti non duplicano risposte o completamenti. Rete interrotta e tentativi non propri producono esiti espliciti.

**Risultato locale del 18 novembre 2026:**

- Migrazione `20261118084414_LessonAttemptsAndPublishedSnapshots`: `PublishedSnapshotJson` separato da `EditorialDraftJson`, entita `LessonAttempt` con indice filtrato univoco per un solo tentativo attivo per utente, `LessonAttemptAnswer` con vincoli univoci per passo/esercizio e `UserLessonCompletion` per la progressione persistita.
- Comando locale `--publish-pilot` (solo in Development, rifiuta file privi di `approved-by-owner`) integrato anche in `tools\setup-local.ps1` per rendere avviabili le cinque lezioni pilota approvate su nuove installazioni locali.
- `GET /api/v1/learning/path` arricchito con stato per utente (`completed`, `available`, `locked`) secondo sblocco sequenziale e metadati dell'eventuale `activeAttempt` riprendibile.
- Endpoint protetti `/api/v1/learning/*` con CSRF, isolamento proprietario e retry su transazioni serializzabili: `POST /lessons/{lessonId}/attempts` (avvio, ripresa o sostituzione esplicita), `GET /attempts/{attemptId}`, `POST /attempts/{attemptId}/answers`, `POST /attempts/{attemptId}/complete` (idempotente) e `POST /attempts/{attemptId}/abandon`.
- Contratto OpenAPI polimorfico (`kind: choice | matching | guidedTranslation | fillBlank`) rigenerato in `schema.d.ts`: nessun indice corretto, soluzione o spiegazione viene esposto prima dell'invio della risposta; le colonne destre degli abbinamenti sono mescolate in modo deterministico per tentativo.
- Runner frontend interattivo (`LessonRunnerPage.tsx` e `LearningPathPage.tsx`) nello stile **A - Il sentiero**: banner "Sessione in corso", tutti e quattro i formati utilizzabili da tocco e tastiera, sopravvivenza al reload tramite `?attempt=<id>`, conferma di uscita (conserva progresso o interrompi tentativo), riepilogo finale e avvio diretto della lezione appena sbloccata.
- Suite di integrazione SQL Server su database temporanei estesa a tutti i formati, reload, immutabilita dello snapshot a tentativo aperto, invii concorrenti e isolamento tra utenti.

### Tappa 5 - Gamification essenziale

**Attivita:** ledger XP, tabella soglie livelli, serie attuale e record, trofei base, dashboard reale e riepilogo ricompense.

Completamento, progresso e premi immediati sono salvati atomicamente. Le proiezioni statistiche devono essere riconciliabili con il ledger.

**Uscita:** ricompense accreditate una sola volta anche con doppio invio o retry; serie corretta per giorni consecutivi, cambio giorno e ora legale; dashboard coerente dopo riapertura.

**Traguardo A funzionale:** pronto per test interni; la qualita del catalogo viene completata nella tappa 6.

**Risultato locale del 18 novembre 2026:**

- Migrazione `20261118102600_GamificationLedgerLevelsStreaksAchievements`: entita `XpLedgerEntry` con chiave univoca `IdempotencyKey`, `LevelThreshold` (livelli 1-8 da *Curioso* a *Maistru catanisi*), `UserStats` riconciliato con la somma del ledger, `UserStreak` su fuso `Europe/Rome`, `AchievementDefinition` (6 trofei base: *Il primo passo*, *Senza sbavature*, *Colpo sicuro*, *Tre giorni di fila*, *Settimana catanese*, *Pronto a salutare*) e `UserAchievement`.
- Calcolo atomico e idempotente dentro la transazione serializzabile di `CompleteAttempt` (`GamificationCalculator`): primo completamento (+2 XP/risposta esatta + 10 XP lezione), ripasso anti-farming (+5 XP fino a 3 ripassi/giorno locale), bonus zero errori (+5 XP), bonus prima attivita del giorno (+5 XP), avanzamento di livello, aggiornamento serie giornaliera (gestione mezzanotte `Europe/Rome`, passaggio ora legale e azzeramento effettivo dopo un giorno saltato preservando il record) e sblocco trofei puri senza XP ricorsivi.
- Endpoint autenticati `GET /api/v1/gamification/summary` (con verifica `IsReconciledWithLedger`, XP di oggi e settimana corrente da lunedi, ripassi premiati usati e ultimi movimenti del ledger) e `GET /api/v1/gamification/achievements` (catalogo completo con avanzamento e data di sblocco).
- Navigazione principale `Impara`, `Trofei`, `Profilo` (nascosta durante l'esecuzione di una lezione), striscia dashboard reale sopra il sentiero (`DashboardSummaryStrip`), bacheca trofei filtrabile per categoria (`AchievementsPage`), dashboard profilo studente con registro XP verificato (`ProfileDashboardPage`) e riepilogo di fine lezione con dettaglio voci XP, eventuale passaggio di livello e nuovi trofei sbloccati.
- Suite di test di integrazione SQL Server (`GamificationTests`, 35 test totali superati) su completamenti concorrenti, anti-farming e tetto giornaliero ripassi, mezzanotte/ora legale `Europe/Rome`, giorno saltato, promozione al livello 2 e sblocco trofei.

### Tappa 6 - Contenuti e amministrazione

**Attivita:** accesso amministrativo protetto, ruoli, gestione e pubblicazione contenuti, varianti, fonti, revisore, audit, sospensioni e gestione segnalazioni.

Il primo sottoinsieme editoriale arriva dopo la tappa 3, per non attendere fine sviluppo prima di inserire lezioni. Completare poi metriche aggregate e funzioni operative.

Ogni voce linguistica conserva forma, significato, contesto, registro, varianti e revisione. Il committente ha confermato che si occupera della revisione prima della pubblicazione. Le bozze preparate durante lo sviluppo restano non pubblicate fino alla sua approvazione; concordare le convenzioni ortografiche prima di validare l'unita pilota. Produrre il catalogo beta solo dopo approvazione dell'unita pilota.

**Uscita:** un editor pubblica senza modifiche manuali al database; utenti normali non accedono alle API admin; gli aggiornamenti non alterano tentativi in corso; tutte le lezioni pubblicate sono revisionate.

**Risultato locale del 18 novembre 2026:**

- Decisioni approvate dal committente: due ruoli distinti **`Editor`** (prepara bozze lezioni e voci lessicali e invia a revisione) e **`Admin`/Revisore** (approva linguisticamente, pubblica/ritira lezioni e modera utenti, ruoli, segnalazioni e registro audit), con bootstrap iniziale esplicito da terminale locale (`--grant-role "<username>:<Editor|Admin>"`) e gestione successiva dal pannello Admin.
- Migrazione `20261118135442_EditorialAdministrationAndModeration`: metadati editoriali e di revisione su `Lessons` (`EditorialState`, `OrthographicVariants`, `SourceReference`, `ReviewerNotes`, attori e timestamp di modifica/approvazione/pubblicazione), metadati di sospensione su `AspNetUsers`, ruoli `Editor` e `Admin` precaricati, tabelle `LinguisticLexemes`, `UserReports` e `AdminAuditEntries`.
- Presidio linguistico e immutabilita: `POST /api/v1/admin/lessons/{id}/draft` valida la struttura JSON degli esercizi e riporta `EditorialState` a `Draft` se la bozza cambia, senza toccare `PublishedSnapshotJson` ne la lezione online; `POST /api/v1/admin/lessons/{id}/publish` richiede ruolo `Admin` e stato `Approved` (altrimenti `409 Conflict`) e non altera mai i `LessonAttempt` gia avviati.
- Sospensione e sicurezza: la sospensione aggiorna il `SecurityStamp` invalidando immediatamente i cookie attivi (`401` alla richiesta successiva) e blocca nuovi login/recuperi (`403`); protezioni contro auto-sospensione e auto-rimozione del ruolo `Admin`.
- Interfaccia frontend protetta (`AdminConsolePage.tsx`, visibile in navigazione come `Redazione` o `Admin` solo ai profili autorizzati) e pulsante contestuale `Segnala esercizio` nel runner lezione (`POST /api/v1/reports/exercises`).
- Suite di test di integrazione SQL Server estesa con `AdminContentAndModerationTests` (**38/38 test superati**).

### Tappa 7 - Cuori, missioni e social

**Attivita:** cuori e rigenerazione, feature flag consumo, pratica, obiettivi giornalieri e settimanali, ricerca utenti, richieste, accettazione/rifiuto/rimozione, blocchi, profilo pubblico e classifiche.

La rigenerazione puo essere calcolata al momento della lettura/azione: non serve un timer server per utente. Missioni assegnate senza duplicati e premi idempotenti.

Applicare la regola cuori approvata: 5 massimi, -1 per errore nel percorso, +1 ogni 4 ore, completamento della lezione gia iniziata garantito a zero senza penalizzare progresso o premi, nuove lezioni bloccate a zero e ripasso sempre disponibile. La feature flag deve poter disattivare consumo e blocco di avvio associato.

Per la classifica partire da query indicizzate sul ledger: primi 50, posizione personale e filtro amici; aggregati dedicati solo se necessari. Le fasce Bronzo/Argento/Oro richiedono regole approvate prima di introdurre promozioni o retrocessioni.

**Uscita:** nessuna amicizia con se stessi o coppia duplicata; richieste reciproche e accettazioni concorrenti gestite; i blocchi impediscono nuove interazioni previste; privacy rispettata; classifica basata solo sulla settimana corretta.

**Risultato locale del 18 novembre 2026:**

- Decisioni approvate dal committente: sessione di ripasso libero (`IsPractice = true`) che rigenera **+1 cuore** al completamento senza consumare cuori su errore; 3 missioni giornaliere fisse (**+10 XP** ciascuna: `daily-lesson-1`, `daily-xp-25`, `daily-correct-10`) e 2 sfide settimanali (**+25 XP** ciascuna: `weekly-lessons-4`, `weekly-perfect-2`) sul fuso `Europe/Rome` senza ricorsione XP; tre leghe settimanali **`Bronzo` / `Argento` / `Oro`** (lunedi-domenica `Europe/Rome`) con promozione dei primi 10 con `WeeklyXp > 0` e retrocessione degli ultimi 5 (mai sotto `Bronzo`); visibilita profilo predefinita `Public` con controlli espliciti per `Public / FriendsOnly / Private`, ricezione richieste amicizia e presenza in classifica.
- Migrazione `20261118154754_HeartsMissionsSocialAndLeagues`: flag `IsPractice` su `LessonAttempts`, tabelle `UserHeartStates`, `UserMissionProgress`, `UserPrivacySettings`, `Friendships` (con coppia ordinata `(UserLowId, UserHighId)` univoca e vincoli contro auto-amicizia), `UserBlocks`, `UserLeagueProfiles` e `LeagueWeekSettlements`.
- Regola cuori e feature flag: saldo iniziale `5/5`, `-1` cuore per prima risposta errata ad ogni passo delle lezioni del percorso (nessun doppio consumo su retry), rigenerazione calcolata lazy (`+1` ogni 4 ore fino a `5`), blocco `409 Conflict` solo sull'avvio di nuove lezioni del percorso a `0` cuori preservando completamento e premi dei tentativi gia aperti e lasciando sempre accessibile `POST /api/v1/learning/practice/start`; `Hearts:ConsumptionEnabled = false` disattiva consumo e blocco di avvio.
- API social e leghe (`/api/v1/social/*` e `GET /api/v1/gamification/missions`): ricerca utenti filtrata lato server per blocchi bidirezionali e profili privati, auto-accettazione atomica su richieste di amicizia reciproche concorrenti, rimozione automatica dell'amicizia in caso di blocco, profilo pubblico con trofei recenti e classifica settimanale per lega o filtro amici con liquidazione pigra idempotente delle settimane concluse.
- Interfaccia frontend nello stile **A - Il sentiero**: navigazione estesa con `Classifica` (`LeaderboardPage.tsx`) e `Amici` (`FriendsPage.tsx`), contatore cuori con countdown rigenerazione, pulsante `Ripasso libero (+1 ❤️)`, banner guida a zero cuori, pannello missioni giornaliere/settimanali (`MissionsPanel.tsx`), modale profilo pubblico (`PublicProfileModal.tsx`) e scheda `Privacy, amici e classifiche` nelle impostazioni account.
- Suite di test di integrazione SQL Server estesa con `HeartsMissionsSocialAndLeagueTests` (**44/44 test superati**).

### Tappa 8 - Preparazione al rilascio

**Attivita:** verifica responsive e accessibilita, percorsi di errore, caricamento asset, metriche minimali, cancellazione account, informativa e termini beta, manifest e installabilita PWA.

La prima PWA richiede connessione per lezioni e sincronizzazione. Se presente, la cache offline riguarda solo risorse statiche: niente risposte sensibili, sessioni o completamenti simulati offline.

Eseguire prove su SQL Server reale per transazioni, vincoli e migrazioni, non soltanto su provider in memoria; test end-to-end dei percorsi principali e carico rappresentativo di 25 utenti, con scenario esplorativo di 100 lettori concorrenti.

**Uscita:** nessun difetto bloccante noto; flussi principali utilizzabili da tastiera; nessuno scorrimento orizzontale indesiderato a 360 px; prestazioni misurate distinguendo API a caldo e cold start.

**Risultato locale del 19 novembre 2026:**

- **Decisione approvata dal committente:** cancellazione autonoma immediata e definitiva dell'account con conferma della password attuale (`POST /api/v1/auth/delete`), rimozione atomica di tutti i dati personali, credenziali, sessioni, progressi, XP, trofei, cuori, missioni, classifiche e amicizie/blocchi, e anonimizzazione delle eventuali segnalazioni storiche (`UserReports.ReporterUserId = NULL`) e voci di audit (`AdminAuditEntries.ActorUserId = NULL`, snapshot `[account-eliminato]`).
- **Migrazione `20261119082017_Stage8ReleaseReadinessAndPrivacy`:** tracciamento `AcceptedTermsAtUtc` e `AcceptedTermsVersion` su `AspNetUsers`, `ReporterUserId` nullable su `UserReports` con FK `SetNull` per preservare le segnalazioni didattiche anonimizzate dopo l'eliminazione dell'account.
- **Informativa privacy e termini beta (`GET /api/v1/system/legal`):** endpoint pubblico strutturato (versione `beta-1.0`, minimizzazione dati senza email, cookie tecnici HttpOnly, metriche pseudonimizzate, cancellazione autonoma, custodia codice di recupero e regole della comunita; riferimenti titolare/contatto configurabili in `Legal:*`), modale consultabile da ingresso, footer e impostazioni (`LegalNoticeModal.tsx`) e spunta obbligatoria di presa visione in registrazione.
- **Metriche di prodotto pseudonimizzate (`GET /api/v1/admin/metrics?windowDays=7|30|90`):** scheda `Metriche beta` nella console `Redazione / Admin` con KPI di attivazione e completamento, durata mediana effettiva delle lezioni rispetto all'obiettivo di 3-5 minuti, uso del ripasso e adozione amici, tabella di ritenzione per coorti `D1` / `D7` (fuso `Europe/Rome`) e classifica hotspot degli esercizi con maggior tasso di errore al primo tentativo.
- **PWA installabile, resilienza offline e accessibilita/responsive 360 px:** `manifest.webmanifest` con icone dedicate (`192`, `512`, `maskable`) generate da Ficodi senza upscaling del soggetto, service worker `sw.js` rigorosamente limitato agli asset statici (esclusione totale di `/api/*` e `/health/*`, zero stati di completamento simulati offline), banner esplicito di disconnessione (`OfflineNoticeBanner.tsx`), link `Salta al contenuto principale`, chiusura modali da tastiera (`Escape`) e adattamento responsive verificato fino a `360 px` senza overflow orizzontale.
- **Concorrenza SQL Server e benchmark empirici (`ReleaseReadinessAndLoadTests`, 48/48 test totali superati):** introdotto `SqlConcurrencyLock` (`sp_getapplock` transazionale con `ReadCommitted`) e proiezioni SQL piatte senza blob `nvarchar(max)` superflui ne esplosioni cartesiane EF Core, eliminando i deadlock di range lock tra utenti concorrenti su SQL Server reale:
  - **Cold start (database appena migrato + prima richiesta):** `/health/ready = 5,89 s` (`< 15 s`), `/api/v1/auth/register = 2,11 s`, `/api/v1/learning/path = 1,73 s`.
  - **Carico rappresentativo a caldo (25 utenti attivi concorrenti su flusso completo percorso + cuori + missioni + 8 risposte + completamento + classifica, 350 richieste concorrenti):** **0 errori / 0 deadlock**, `56 XP` deterministici per tutti i 25 utenti, `p50 = 338,9 ms`, `p95 = 1307,1 ms` (sotto il budget `< 2500 ms`).
  - **Carico esplorativo di lettura (100 lettori concorrenti simultanei):** `p50 = 669,6 ms`, `p95 = 1145,8 ms`, `max = 1193,8 ms` (intero burst completato in `1,25 s`).

### Tappa 9 - Distribuzione e beta pubblica monitorata

**Attivita:** verifica attuale di costi e disponibilita Azure, infrastruttura riproducibile, segreti e permessi, deploy, migrazioni controllate, prova di ripristino, rollback, alert e apertura delle registrazioni senza inviti. Coinvolgere un primo gruppo di 20-50 tester per raccogliere feedback senza limitare le iscrizioni a quel gruppo.

Proposta coerente: Static Web Apps per frontend, Container Apps Consumption per API e Blob Storage per media. Scegliere esplicitamente tra Azure SQL Free offer e tier a pagamento dopo verifica di quote, pausa, affidabilita e budget.

Il documento originale alterna SQL Basic e Free offer, Container Apps e App Service; la stima 19-26 USD non puo essere usata come preventivo della configurazione aggiornata. Gli alert di budget non costituiscono un tetto automatico alla spesa.

La registrazione aperta sostituisce l'ipotesi di accesso su invito del documento originale. Prima di aprire il servizio verificare rate limit, protezioni anti-abuso, capacita di moderazione, monitoraggio e gestione del superamento delle quote. Non presumere un carico massimo di 50 utenti o un costo garantito dal solo uso di tier gratuiti.

Verificare separatamente il costo operativo dei log, del traffico, del registry e la latenza combinata di API e database dopo inattivita. Non applicare migrazioni automaticamente da ogni replica.

**Uscita:** beta raggiungibile in HTTPS con registrazione aperta, ripristino dimostrato, procedura di rollback disponibile, costi e abuso monitorati e canale feedback operativo.

**Risultato locale del 19 novembre 2026:**

- **Decisioni approvate dal committente per la Tappa 9:**
  1. Preparare e verificare prima in locale l'intero pacchetto di rilascio, mantenendo ogni operazione su Git remoto e su sottoscrizione Azure subordinata a conferma esplicita.
  2. Adottare l'architettura **piu economica possibile e senza Container Apps**: **Azure App Service Linux `F1` Free** (API + PWA same-origin servita da `wwwroot`) + **Azure SQL Database Free Offer** (`GP_S_Gen5` Serverless con `useFreeLimit: true` e `freeLimitExhaustionBehavior: 'AutoPause'`), costo **€0/mese** garantito senza container ne registry, parametrizzata per eventuale upgrade immediato a `B1` / `Basic`.
- **Hosting di produzione e CLI operativa (`ProductionHosting.cs`, `OperationalCli.cs`):**
  - Supporto reverse proxy sicuro (`Hosting:EnableForwardedHeaders`, `Hosting:ForwardLimit = 1`) contro spoofing di `X-Forwarded-For`, cookie `__Host-Siculingo.Auth` / `__Host-Siculingo.Csrf` (`Secure`, `HttpOnly`), HSTS e persistenza del key ring Data Protection su `/home/site/dataprotection-keys`.
  - Hosting statico same-origin della PWA da `wwwroot` con MIME `application/manifest+json`, `Cache-Control: no-cache` su `index.html`, `sw.js` e `manifest.webmanifest`, caching `immutable` sugli asset Vite con hash e fallback SPA che esclude rigorosamente `/api/*`, `/health/*` e `/openapi/*` (preservando `404 ProblemDetails` JSON).
  - Comandi operativi controllati per ambienti di rilascio (`--migrate-only`, `--seed-approved-pilot`, `--promote-role`) con retry automatico sul risveglio di Azure SQL Serverless; `--import-pilot` mantenuto esclusivo di `Development`.
- **Infrastruttura come codice, pipeline e verifiche operative (`infra\`, `tools\`, `.github\workflows\deploy-azure.yml`, `OPERATIONS_RUNBOOK.md`):**
  - Template `infra\main.bicep` e `infra\main.bicepparam` validati con `az bicep build` / `az bicep build-params` (App Service Linux .NET 10, System-Assigned Managed Identity, Azure SQL Entra-only con Free Offer `AutoPause`, script `infra\grant-webapp-identity.sql`).
  - Script `tools\publish-release.ps1` verificato: genera `artifacts\publish\siculingo-release.zip` (**28,76 MB** su 1 GB disponibili nel piano `F1`).
  - Prova di ripristino end-to-end verificata con `tools\verify-backup-restore.ps1`: migrazione in modalita `Production` -> pubblicazione pilota approvato -> `BACKUP DATABASE ... WITH CHECKSUM` -> `RESTORE DATABASE` su istanza pulita -> `DBCC CHECKDB` senza errori -> verifica integrita (`5` lezioni pubblicate, `8` livelli, `6` trofei).
  - Workflow manuale `.github\workflows\deploy-azure.yml` con login passwordless OIDC, gate test SQL, migrazione controllata pre-deploy, `az webapp deploy --type zip` e verifica `/health/live` + `/health/ready` con retry sul cold start.
  - Test di integrazione `ProductionHostingAndOperationsTests` superati su SQL Server reale (**50/50 test totali nella solution**).
- **Provisioning live Azure e deploy OIDC (19 novembre 2026):**
  - Creato su autorizzazione del committente il Resource Group `rg-siculingo-beta` (`westeurope`, sottoscrizione `LeMagnifique-sub`) tramite `infra\main.bicep`: App Service Plan Linux `siculingo-beta-plan-lpyzen` (`F1` Free), Web App `siculingo-beta-app-lpyzen` (`https://siculingo-beta-app-lpyzen.azurewebsites.net`), Azure SQL `siculingo-beta-sql-lpyzen` / `Siculingo_beta` (`GP_S_Gen5` Free Offer con `useFreeLimit: true` e `AutoPause`) e Managed Identity dedicate `siculingo-beta-web-id-lpyzen` (runtime) e `siculingo-beta-gha-id-lpyzen` (deploy federato OIDC verso `repo:lcatarcia/Siculingo:environment:beta`).

## 7. Verifiche prioritarie

| Area | Casi indispensabili |
| --- | --- |
| Account | Username duplicato anche concorrente e con maiuscole diverse; password errata; accesso da piu dispositivi; sessione scaduta/revocata |
| Recupero | Codice errato, riutilizzato o inviato in parallelo; nuova password funzionante, vecchie sessioni revocate e nuovo codice valido |
| Autorizzazione | Tentativi di altri utenti, risorse non pubblicate e amministrazione non accessibili |
| Didattica | Quattro formati corretti, spiegazioni, tentativi stabili dopo modifica contenuti |
| Premi | Retry, doppio clic e concorrenza non duplicano XP, missioni o trofei |
| Tempo | Mezzanotte, ora legale, giorno saltato, cambio settimana e rigenerazione cuori |
| Cuori | Completamento e premi preservati a zero; nuove lezioni bloccate a zero; ripasso disponibile; flag disattivato; retry senza doppio consumo |
| Progressione | Lezioni bloccate, completamento, pratica e ricaricamento coerenti |
| Social | Coppia unica, richieste reciproche, blocco e impostazioni privacy |
| Operativita | Migrazioni, backup/ripristino, rollback, errori di rete e database |
| Grafica | Trasparenza, aloni, nitidezza alla dimensione reale e movimento ridotto |

La velocita di risposta anomala e un segnale da osservare, non una ragione sufficiente da sola per penalizzare automaticamente un utente.

## 8. Metriche per decidere i passi successivi

Raccogliere solo eventi necessari e pseudonimizzati: registrazione, lezione avviata/completata, correttezza aggregata per esercizio, ritorno D1/D7, lezioni per utente, uso del ripasso e adozione amici.

Definire denominatori e finestra di osservazione prima del test. Con 20-50 tester i risultati saranno esplorativi, non prove statistiche definitive. Il tempo di apprendimento stimato di 3-5 minuti va verificato sui contenuti reali.

Non considerare XP elevati una prova di apprendimento; associare errori, ripasso e feedback linguistico alle metriche di frequenza.

## 9. Decisioni della tappa 0

| Decisione | Esito o proposta da approvare |
| --- | --- |
| Piattaforma | APPROVATA il 17 novembre 2026: PWA mobile-first, utilizzabile e installabile dal browser su smartphone e desktop; niente app nativa nella beta |
| Accesso | APPROVATO il 17 novembre 2026: username e password con accesso da piu dispositivi |
| Recupero account | APPROVATO il 17 novembre 2026: codice generato alla registrazione, senza email; perdita di password e codice impedisce il recupero autonomo |
| Cuori | APPROVATI il 17 novembre 2026: 5 massimi, -1 per errore nel percorso, +1 ogni 4 ore; a zero si termina la lezione gia iniziata con progresso e premi, ma non se ne iniziano altre; ripasso sempre disponibile e consumo disattivabile nei test |
| Catalogo | APPROVATO il 17 novembre 2026: 5 lezioni pilota su saluti e presentazioni, da 8-12 esercizi ciascuna; dopo la validazione, obiettivo 30-40 lezioni revisionate per la beta |
| Grafica | APPROVATA il 17 novembre 2026: ultima tavola "Character Design Kit v1.0 / Asset Pack Completo" come riferimento principale; ritagli scelti caso per caso per nitidezza |
| Font | APPROVATI il 17 novembre 2026: Nunito per titoli, pulsanti e interfaccia; Inter per spiegazioni e testi estesi; logo ricavato dalle immagini |
| Percorso visivo | SCELTO il 17 novembre 2026: A - Il sentiero; B e C restano alternative locali del prototipo, non implementazioni produttive |
| Beta | APPROVATA il 17 novembre 2026: registrazione aperta fin dall'inizio, senza inviti o codici di accesso; controlli operativi da completare prima dell'apertura |
| Lingua | RESPONSABILITA APPROVATA il 17 novembre 2026: il committente revisiona i contenuti prima della pubblicazione; il 18 novembre 2026 il committente ha approvato le 5 lezioni pilota (`approved-by-owner`) per la pubblicazione. Nuove lezioni richiedono sempre approvazione esplicita |
| Snapshot tentativi | APPROVATO il 18 novembre 2026: ogni tentativo salva uno snapshot immutabile della lezione pubblicata all'avvio, cosi modifiche successive non alterano sessioni in corso |
| Tabella XP e trofei | APPROVATA il 18 novembre 2026: 1° completamento +2 XP/corretta +10 XP lezione; dal 2° completamento +5 XP ripasso (max 3/giorno locale `Europe/Rome`); bonus +5 XP zero errori e +5 XP prima attivita del giorno; trofei come medaglie pure senza XP extra |
| Hosting | APPROVATO E PROVISIONATO il 19 novembre 2026: architettura zero-container a €0/mese su `rg-siculingo-beta` (`westeurope`, `LeMagnifique-sub`) con Azure App Service Linux `F1` Free (`siculingo-beta-app-lpyzen`) + Azure SQL Database Free Offer (`Siculingo_beta`, `useFreeLimit: true`, `AutoPause`) + User-Assigned Managed Identities Entra-only per runtime e GitHub OIDC |
| Repository | APPROVATO il 19 novembre 2026: repository di riferimento [`lcatarcia/Siculingo`](https://github.com/lcatarcia/Siculingo) per Git e GitHub Actions (`ci.yml` e `deploy-azure.yml` sull'ambiente `beta` con OIDC `repo:lcatarcia/Siculingo:environment:beta`); commit e push eseguiti solo su conferma esplicita |
| Sessioni browser | APPROVATE il 17 novembre 2026: cookie HttpOnly con CSRF e frontend/API same-origin; scadenza a 30 giorni dall'accesso senza rinnovo automatico; logout corrente e revoca globale/recupero su tutti i dispositivi |
| Prime impostazioni | APPROVATE il 18 novembre 2026: nome visualizzato e cambio password con revoca degli accessi; username e codice di recupero invariati. Audio, notifiche e obiettivi differiti alle funzioni corrispondenti |
| Privacy e termini | IMPLEMENTATI in locale il 19 novembre 2026: informativa privacy e termini beta (`beta-1.0`) serviti da `/api/v1/system/legal` con spunta obbligatoria in registrazione, conservazione log 180 giorni e cancellazione autonoma immediata con conferma password e anonimizzazione di segnalazioni/audit storici; riferimento nominativo/contatto finale del titolare configurabile in `Legal:*` prima dell'apertura pubblica |

## 10. Prossimo passo concreto

Decisioni di avvio concluse: PWA mobile-first, username/password da piu dispositivi, recupero tramite codice senza email, cuori, 5 lezioni pilota, grafica e font approvati. Revisione linguistica affidata al committente; beta con registrazione aperta.

Tutte le tappe del piano (**Tappe 0–9**) sono ora implementate e verificate in locale (**50/50 test SQL Server superati**, drill di backup/restore superato e pacchetto `siculingo-release.zip` da 28,76 MB pronto). Per andare online restano unicamente i passaggi esterni soggetti a conferma esplicita del committente (documentati passo-passo in `OPERATIONS_RUNBOOK.md`):

1. Revisione opzionale dei 15 asset estesi nel manifest grafico e valorizzazione dei riferimenti finali del titolare in `Legal:*` (`infra\main.bicepparam`).
2. Autorizzazione al primo commit Git locale e all'eventuale collegamento del repository GitHub remoto per attivare CI (`ci.yml`) e deploy OIDC (`deploy-azure.yml`).
3. Autorizzazione al provisioning sul tenant Azure tramite `infra\main.bicep`, esecuzione di `--migrate-only`, `--seed-approved-pilot` e `--promote-role`, e apertura della beta pubblica ai primi 20-50 tester.

La revisione puntuale dei 15 asset estesi resta aperta e non viene confusa con la scelta del percorso. Repository remoto, CI online e Azure richiedono autorizzazioni successive; i testi catanesi non vengono pubblicati prima della revisione concordata.
