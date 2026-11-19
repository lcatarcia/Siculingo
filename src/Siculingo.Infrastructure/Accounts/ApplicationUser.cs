using Microsoft.AspNetCore.Identity;

namespace Siculingo.Infrastructure.Accounts;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser() => Id = Guid.NewGuid();

    public DateTimeOffset RegisteredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcceptedTermsAtUtc { get; set; }
    public string? AcceptedTermsVersion { get; set; }
    public string? DisplayName { get; set; }
    public bool IsSuspended { get; set; }
    public string? SuspensionReason { get; set; }
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public Guid? SuspendedByUserId { get; set; }
}
