-- Idempotent Microsoft Entra Managed Identity grants on Azure SQL Database (without requiring Graph Directory Readers).
-- Run once connected to [Siculingo_beta] as the Microsoft Entra SQL Administrator,
-- replacing the placeholders with the exact outputs from `infra/main.bicep`.

DECLARE @RuntimePrincipal SYSNAME = N'<RUNTIME_IDENTITY_NAME>';
DECLARE @RuntimeClientId UNIQUEIDENTIFIER = '<RUNTIME_IDENTITY_CLIENT_ID>';
DECLARE @DeployPrincipal SYSNAME = N'<DEPLOY_IDENTITY_NAME>';
DECLARE @DeployClientId UNIQUEIDENTIFIER = '<DEPLOY_IDENTITY_CLIENT_ID>';

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @RuntimePrincipal)
BEGIN
    DECLARE @RuntimeSid VARCHAR(64) = CONVERT(VARCHAR(64), CAST(@RuntimeClientId AS VARBINARY(16)), 1);
    DECLARE @CreateRuntimeSql NVARCHAR(512) = N'CREATE USER [' + REPLACE(@RuntimePrincipal, N']', N']]') + N'] WITH SID = ' + @RuntimeSid + N', TYPE = E;';
    EXEC (@CreateRuntimeSql);
END;

EXEC sp_addrolemember N'db_datareader', @RuntimePrincipal;
EXEC sp_addrolemember N'db_datawriter', @RuntimePrincipal;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @DeployPrincipal)
BEGIN
    DECLARE @DeploySid VARCHAR(64) = CONVERT(VARCHAR(64), CAST(@DeployClientId AS VARBINARY(16)), 1);
    DECLARE @CreateDeploySql NVARCHAR(512) = N'CREATE USER [' + REPLACE(@DeployPrincipal, N']', N']]') + N'] WITH SID = ' + @DeploySid + N', TYPE = E;';
    EXEC (@CreateDeploySql);
END;

EXEC sp_addrolemember N'db_datareader', @DeployPrincipal;
EXEC sp_addrolemember N'db_datawriter', @DeployPrincipal;
EXEC sp_addrolemember N'db_ddladmin', @DeployPrincipal;
GO
