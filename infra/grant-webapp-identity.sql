-- Idempotent Microsoft Entra Managed Identity grant on Azure SQL Database.
-- Run once connected to [Siculingo_beta] as the Microsoft Entra SQL Administrator,
-- replacing <WEB_APP_NAME> with the `webAppName` output from `infra/main.bicep`.

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'<WEB_APP_NAME>')
BEGIN
    CREATE USER [<WEB_APP_NAME>] FROM EXTERNAL PROVIDER;
END;

ALTER ROLE db_datareader ADD MEMBER [<WEB_APP_NAME>];
ALTER ROLE db_datawriter ADD MEMBER [<WEB_APP_NAME>];
GO
