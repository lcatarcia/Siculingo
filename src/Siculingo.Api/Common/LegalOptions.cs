namespace Siculingo.Api.Common;

public sealed class LegalOptions
{
    public const string SectionName = "Legal";

    public string TermsVersion { get; set; } = "beta-1.0";
    public string LastUpdatedIsoDate { get; set; } = "2026-11-18";
    public string ControllerName { get; set; } = "Progetto sperimentale Siculingo (configurare Legal:ControllerName prima del lancio pubblico)";
    public string ContactChannel { get; set; } = "Canale di contatto da configurare in Legal:ContactChannel prima dell'apertura pubblica";
    public int AuditRetentionDays { get; set; } = 180;
}
