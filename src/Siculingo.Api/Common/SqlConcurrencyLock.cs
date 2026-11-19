using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Siculingo.Infrastructure.Persistence;

namespace Siculingo.Api.Common;

public static class SqlConcurrencyLock
{
    public static Task<IDbContextTransaction> BeginUserTransactionAsync(
        SiculingoDbContext db,
        Guid userId,
        CancellationToken cancellationToken) =>
        BeginResourceTransactionAsync(db, $"siculingo:user:{userId:N}", cancellationToken);

    public static async Task<IDbContextTransaction> BeginResourceTransactionAsync(
        SiculingoDbContext db,
        string resourceName,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var resultParam = new SqlParameter("@lockResult", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            var resourceParam = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
            {
                Value = resourceName
            };

            await db.Database.ExecuteSqlRawAsync(
                "EXEC @lockResult = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000;",
                [resultParam, resourceParam],
                cancellationToken);

            var lockCode = (int)(resultParam.Value ?? -999);
            if (lockCode < 0)
            {
                throw new InvalidOperationException($"Acquisizione lock transazionale non riuscita per '{resourceName}' (codice {lockCode}).");
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public static bool IsConcurrencyConflict(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 2601 or 2627 or 1222 })
                return true;
        }

        return false;
    }
}
