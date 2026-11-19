namespace Siculingo.Infrastructure.Accounts;

public sealed class AccountRecoveryCode
{
    public Guid UserId { get; set; }
    public required byte[] CodeHash { get; set; }
}
