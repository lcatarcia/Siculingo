# Siculingo — Runbook Operativo di Distribuzione e Gestione Beta (Tappa 9)

Questo documento descrive la configurazione a costo zero selezionata per la beta pubblica monitorata di Siculingo, le procedure riproducibili di provisioning, rilascio, migrazione, backup/ripristino, rollback e presidio anti-abuso.

> **Vincolo operativo:** nessuna operazione su repository Git remoti o sottoscrizioni Azure viene eseguita automaticamente dagli script locali senza autorizzazione esplicita del committente.

---

## 1. Architettura selezionata (€0/mese, zero container)

Per rispettare la scelta della **configurazione più economica possibile senza Container Apps né container registry**, l'intera PWA e l'API ASP.NET Core 10 vengono ospitate **same-origin** in un unico artefatto:

| Componente | Servizio Azure predefinito | Costo mensile | Ruolo e protezioni |
| --- | --- | --- | --- |
| **Web + API same-origin** | **Azure App Service Linux (`F1` Free)** (`DOTNETCORE\|10.0`) | **€0 / mese** | Serve l'API `/api/v1/*`, `/health/*` e la PWA statica compilata in `wwwroot`. HTTPS gratuito su `*.azurewebsites.net`, cookie `__Host-Siculingo.Auth` e `__Host-Siculingo.Csrf` (`HttpOnly`, `Secure`), Data Protection persistita su `/home/site/dataprotection-keys`. |
| **Database relazionale** | **Azure SQL Database — Free Offer** (`GP_S_Gen5` Serverless, `useFreeLimit: true`, `freeLimitExhaustionBehavior: 'AutoPause'`) | **€0 / mese** | 100.000 vCore-secondi/mese + 32 GB dati + 32 GB backup PITR (7 giorni). In caso di inattività o esaurimento della quota mensile gratuita va in **pausa automatica** fino al mese successivo **senza mai generare addebiti**. |
| **Autenticazione DB e Deploy** | **Microsoft Entra ID + Managed Identity + GitHub OIDC** | **€0 / mese** | Zero password SQL (`Authentication=Active Directory Default`), zero client secret a lunga scadenza in GitHub Actions (`azure/login@v2` federato OIDC). |

### Quote del profilo gratuito (`F1` + `SQL Free Offer`) e percorso di upgrade

- **App Service `F1` Free:** tetto rigido di **60 minuti CPU/giorno**, **1 GB RAM**, **1 GB storage `/home`** (il pacchetto completo `siculingo-release.zip` pesa **~28,8 MB**, meno del 3% dello spazio disponibile) e **165 MB/giorno** di traffico in uscita. Se la quota giornaliera viene raggiunta, Azure sospende temporaneamente l'app fino al reset giornaliero senza fatturare alcun sovrapprezzo.
- **Latenza dopo inattività (cold start / auto-pause):** dopo 1 ora di inattività il database Serverless va in pausa; la prima richiesta al risveglio può impiegare **15–45 secondi** (gestita dai retry su errori transitori `40613`/`40197`/`40501` sia in EF Core sia in `OperationalCli`).
- **Upgrade immediato senza cambiare codice né architettura:** se il traffico della beta supera i 60 min CPU/giorno o richiede dominio personalizzato con TLS gestito e `Always On`, basta aggiornare i parametri in `infra\main.bicepparam`:
  - `param appServicePlanSku = 'B1'`
  - `param sqlPricingMode = 'Basic'` (oppure mantenere `'FreeOffer'` passando a fatturazione standard)

---

## 2. Generazione locale del pacchetto di rilascio

Dalla radice del repository:

```powershell
.\tools\publish-release.ps1
```

Lo script esegue in sequenza:
1. `npm run lint` (`oxlint`) + `npm run build` (`tsc -b && vite build`) in `frontend\siculingo-web` (escludendo automaticamente `/prototype` e `/foundation`).
2. `dotnet publish .\src\Siculingo.Api\Siculingo.Api.csproj -c Release` in `artifacts\publish\app`.
3. Copia il bundle PWA compilato in `artifacts\publish\app\wwwroot` e il catalogo approvato `content\pilot-greetings.v1.json` in `artifacts\publish\app\content\`.
4. Genera l'archivio pronto per App Service in `artifacts\publish\siculingo-release.zip` senza sporcare `src\Siculingo.Api\wwwroot` durante lo sviluppo locale.

---

## 3. Primo provisioning Azure (solo dopo autorizzazione)

### 3.1 Validazione template Bicep (offline, già verificata)

```powershell
az bicep build --file .\infra\main.bicep
az bicep build-params --file .\infra\main.bicepparam
```

### 3.2 Creazione risorse e identità gestita

1. Impostare in `infra\main.bicepparam` i valori reali di `entraSqlAdminLogin` e `entraSqlAdminObjectId` (ottenibile con `az ad signed-in-user show --query "{upn:userPrincipalName, objectId:id}"`) e completare i riferimenti del titolare in `legalControllerSummary` e `legalPrivacyContact` prima dell'apertura al pubblico.
2. Creare il resource group (es. `rg-siculingo-beta` in `italynorth` o `westeurope`) e applicare il template:
   ```powershell
   az group create --name rg-siculingo-beta --location italynorth
   az deployment group create --resource-group rg-siculingo-beta --template-file .\infra\main.bicep --parameters .\infra\main.bicepparam
   ```
3. Connettersi una sola volta al database Azure SQL come amministratore Microsoft Entra ed eseguire `infra\grant-webapp-identity.sql` (sostituendo `<WEB_APP_NAME>` con l'output `webAppName` del deploy Bicep) per concedere `db_datareader`, `db_datawriter` e `db_ddladmin` alla System-Assigned Managed Identity della Web App.

### 3.3 Federazione OIDC per GitHub Actions su `lcatarcia/Siculingo` (zero segreti statici)

Repository di riferimento: [`https://github.com/lcatarcia/Siculingo`](https://github.com/lcatarcia/Siculingo) (ambiente GitHub Actions: **`beta`**).

1. Creare una User-Assigned Managed Identity (o App Registration) di deploy con ruolo `Website Contributor` sul resource group `rg-siculingo-beta` (e accesso Microsoft Entra al database SQL per `--migrate-only`) e associare la credenziale federata OIDC con:
   - **Issuer:** `https://token.actions.githubusercontent.com`
   - **Subject:** `repo:lcatarcia/Siculingo:environment:beta`
   - **Audience:** `api://AzureADTokenExchange`
2. Creare l'ambiente `beta` su `lcatarcia/Siculingo` e configurare le variabili richieste da `.github\workflows\deploy-azure.yml`:
   ```powershell
   gh api --method PUT repos/lcatarcia/Siculingo/environments/beta
   gh variable set AZURE_CLIENT_ID        -R lcatarcia/Siculingo --env beta --body "<client-id>"
   gh variable set AZURE_TENANT_ID        -R lcatarcia/Siculingo --env beta --body "<tenant-id>"
   gh variable set AZURE_SUBSCRIPTION_ID  -R lcatarcia/Siculingo --env beta --body "<subscription-id>"
   gh variable set AZURE_RESOURCE_GROUP   -R lcatarcia/Siculingo --env beta --body "rg-siculingo-beta"
   gh variable set AZURE_WEBAPP_NAME      -R lcatarcia/Siculingo --env beta --body "<webAppName>"
   gh variable set AZURE_WEBAPP_URL       -R lcatarcia/Siculingo --env beta --body "https://<webAppName>.azurewebsites.net"
   gh variable set AZURE_SQL_SERVER_FQDN  -R lcatarcia/Siculingo --env beta --body "<sqlServerFqdn>"
   gh variable set AZURE_SQL_DATABASE_NAME -R lcatarcia/Siculingo --env beta --body "Siculingo"
   ```
3. Avviare manualmente il workflow da GitHub Actions (`Deploy Beta to Azure App Service`) o da CLI:
   ```powershell
   gh workflow run deploy-azure.yml -R lcatarcia/Siculingo -f seed_approved_pilot=true
   ```

---

## 4. Migrazioni controllate, catalogo approvato e primo Admin

Le migrazioni EF Core **non vengono mai applicate automaticamente all'avvio delle repliche web**. L'eseguibile pubblicato espone tre comandi operativi controllati (`OperationalCli.cs`) con retry automatico per il risveglio di Azure SQL Serverless:

```bash
# 1. Applicazione esplicita e controllata delle migrazioni SQL (eseguita anche dal workflow di deploy)
dotnet Siculingo.Api.dll --migrate-only

# 2. Pubblicazione idempotente dell'unità pilota approvata (rifiuta file privi di reviewStatus = "approved-by-owner")
dotnet Siculingo.Api.dll --seed-approved-pilot ./content/pilot-greetings.v1.json

# 3. Promozione del primo revisore/amministratore dopo essersi registrato via HTTPS sulla beta
dotnet Siculingo.Api.dll --promote-role "<username>:Admin"
```

> Nota: `--import-pilot` (importazione bozze non approvate) è bloccato fuori da `Development`. Qualsiasi nuova lezione catanese in produzione passa esclusivamente dal flusso `Redazione -> Revisione linguistica -> Approvazione Admin -> Pubblicazione`.

---

## 5. Backup, prova di ripristino e rollback

### 5.1 Prova automatizzata di backup/restore SQL Server + `DBCC CHECKDB`

Prima di ogni rilascio importante è possibile verificare end-to-end backup completo, restore su database pulito, integrità fisica/logica (`DBCC CHECKDB`) e consistenza del catalogo pubblicato con:

```powershell
.\tools\verify-backup-restore.ps1
```

### 5.2 Ripristino Point-in-Time (PITR) su Azure SQL

Azure SQL Database (incluso il profilo Free Offer) esegue backup automatici continui con Point-in-Time Restore fino a **7 giorni**:

```powershell
# Ripristina il database a un istante precedente su un nuovo DB affiancato senza sovrascrivere i dati correnti
az sql db restore `
  --resource-group rg-siculingo-beta `
  --server <sqlServerName> `
  --name Siculingo `
  --dest-name Siculingo_Restored_YYYYMMDD_HHMM `
  --time "2026-11-19T10:00:00Z"
```

Dopo aver verificato `/health/ready` e l'integrità dei dati sul database ripristinato, aggiornare la connection string `Database` (o rinominare i database in finestra di manutenzione) e riavviare la Web App.

### 5.3 Procedura di rollback applicativo

1. Scaricare l'artefatto `siculingo-release` dell'ultima esecuzione verde precedente da [`lcatarcia/Siculingo` Actions](https://github.com/lcatarcia/Siculingo/actions).
2. Ridistribuire il pacchetto precedente:
   ```powershell
   az webapp deploy --resource-group rg-siculingo-beta --name <webAppName> --src-path .\siculingo-release.zip --type zip
   ```
3. Verificare `https://<webAppName>.azurewebsites.net/health/live` e `/health/ready`.
4. **Regola per le migrazioni:** mantenere migrazioni additive/retrocompatibili tra due versioni consecutive; se una migrazione distruttiva fallisce, eseguire il PITR Azure SQL (§5.2) prima di riaprire il traffico.

---

## 6. Sicurezza dietro reverse proxy, monitoraggio quote e anti-abuso

- **Intestazioni proxy (`Hosting:EnableForwardedHeaders = true`, `Hosting:ForwardLimit = 1`):** in produzione dietro il load balancer Azure App Service viene letto esclusivamente l'ultimo hop aggiunto da Azure in `X-Forwarded-For` e `X-Forwarded-Proto`, impedendo a client malevoli di falsificare l'IP sorgente per aggirare il rate limit (`20` richieste/minuto su credenziali, blocco account di `15` minuti dopo `5` password errate).
- **Controlli giornalieri durante l'apertura ai primi 20–50 tester (con registrazione aperta):**
  1. **Salute servizio:** `/health/live` (`200`) e `/health/ready` (`200`, schema SQL allineato).
  2. **Quote gratuite Azure Portal:** metriche App Service `CpuTime` (`< 60 min/giorno`) e `BytesSent` (`< 165 MB/giorno`); metrica Azure SQL `free_limit_remaining` (`vCore seconds`).
  3. **Moderazione e qualità didattica (console `Admin`):** coda `Segnalazioni` per refusi o dubbi linguistici, scheda `Utenti` per sospensione immediata di eventuali account abusivi (con invalidazione istantanea del `SecurityStamp`), e scheda `Metriche beta` per monitorare completamento lezioni, durata mediana vs obiettivo `3–5 min`, ritorno `D1`/`D7` e hotspot errori esercizi.
