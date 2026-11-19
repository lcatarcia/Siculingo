using Microsoft.Extensions.Options;
using Siculingo.Api.Common;

namespace Siculingo.Api.Features.System;

public sealed record SystemStatusResponse(string Service, string ApiVersion);

public sealed record LegalDocumentSectionDto(string Title, string Body);

public sealed record LegalNoticeResponse(
    string TermsVersion,
    string LastUpdatedIsoDate,
    string ControllerName,
    string ContactChannel,
    int AuditRetentionDays,
    IReadOnlyList<LegalDocumentSectionDto> PrivacySections,
    IReadOnlyList<LegalDocumentSectionDto> TermsSections);

public static class GetStatus
{
    public static void MapSystemStatus(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/system/status",
                () => TypedResults.Ok(new SystemStatusResponse("Siculingo", "v1")))
            .WithName("GetSystemStatus")
            .WithTags("System")
            .WithSummary("Identifica il servizio senza esporre configurazione o dati utente.");

        endpoints.MapGet("/api/v1/system/legal", (IOptions<LegalOptions> options) =>
            {
                var cfg = options.Value;
                return TypedResults.Ok(new LegalNoticeResponse(
                    cfg.TermsVersion,
                    cfg.LastUpdatedIsoDate,
                    cfg.ControllerName,
                    cfg.ContactChannel,
                    cfg.AuditRetentionDays,
                    [
                        new LegalDocumentSectionDto(
                            "Dati raccolti e minimizzazione",
                            "Siculingo raccoglie solo i dati tecnici e didattici indispensabili al servizio: username pseudonimo, nome visualizzato facoltativo, hash della password, hash del codice di recupero monouso, progressi delle lezioni, serie giornaliera, cuori, missioni, classifiche settimanali e legami di amicizia o blocco scelti dall'utente. Non vengono richiesti indirizzo email, numero di telefono o profili social esterni."),
                        new LegalDocumentSectionDto(
                            "Cookie tecnici, sicurezza e assenza di tracker pubblicitari",
                            "L'applicazione utilizza esclusivamente cookie tecnici HttpOnly per la sessione autenticata (durata massima 30 giorni senza rinnovo automatico) e per la protezione anti-falsificazione CSRF. Non sono presenti pixel pubblicitari, SDK di profilazione o condivisioni commerciali con terze parti."),
                        new LegalDocumentSectionDto(
                            "Metriche di prodotto pseudonimizzate",
                            "Per valutare la qualità didattica della beta (completamento delle lezioni, durata effettiva rispetto al target di 3-5 minuti, accuratezza aggregata sugli esercizi, ritorno a 1 e 7 giorni, uso del ripasso), il sistema elabora esclusivamente conteggi aggregati interni senza identificatori esterni."),
                        new LegalDocumentSectionDto(
                            "Conservazione e cancellazione autonoma immediata",
                            $"Puoi eliminare autonomamente e in modo definitivo il tuo account in qualsiasi momento da Impostazioni, confermando la password attuale: vengono rimossi immediatamente credenziali, sessioni, progressi, XP, trofei, cuori, missioni, classifiche e amicizie. Eventuali segnalazioni didattiche storiche e tracce operative di sicurezza restano anonimizzate senza collegamento al tuo account (conservazione massima log operativi: {cfg.AuditRetentionDays} giorni).")
                    ],
                    [
                        new LegalDocumentSectionDto(
                            "Natura sperimentale della beta",
                            "Siculingo è una beta sperimentale gratuita dedicata all'apprendimento del catanese a partire dall'italiano. Contenuti, esercizi, punteggi e classifiche possono essere aggiornati, corretti o ricalibrati in seguito alla revisione linguistica continua."),
                        new LegalDocumentSectionDto(
                            "Custodia del codice di recupero",
                            "Poiché Siculingo non raccoglie indirizzi email, il codice di recupero generato alla creazione dell'account (e rigenerato a ogni recupero) è l'unico strumento per reimpostare autonomamente la password in caso di smarrimento."),
                        new LegalDocumentSectionDto(
                            "Comportamento nella comunità e connettività",
                            "Non è consentito scegliere nomi utente offensivi o impersonare altri partecipanti; gli amministratori possono sospendere gli account in caso di abuso. La web app richiede una connessione internet attiva per svolgere le lezioni e sincronizzare i progressi.")
                    ]));
            })
            .WithName("GetLegalNotice")
            .WithTags("System")
            .WithSummary("Restituisce informativa privacy e termini d'uso della beta sperimentale con riferimenti configurabili.");
    }
}
