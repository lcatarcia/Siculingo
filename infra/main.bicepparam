using './main.bicep'

param environmentName = 'beta'
param appNamePrefix = 'siculingo'
param appServiceSku = 'F1'
param sqlTier = 'FreeOffer'

// Set SQL_ENTRA_ADMIN_LOGIN and SQL_ENTRA_ADMIN_OBJECT_ID in the shell before provisioning (keeps personal UPN/OID out of Git):
param sqlEntraAdminLogin = readEnvironmentVariable('SQL_ENTRA_ADMIN_LOGIN', 'admin-placeholder@example.com')
param sqlEntraAdminObjectId = readEnvironmentVariable('SQL_ENTRA_ADMIN_OBJECT_ID', '00000000-0000-0000-0000-000000000000')
param sqlEntraAdminPrincipalType = 'User'

param legalControllerName = 'Progetto sperimentale Siculingo'
param legalContactChannel = 'Segnalazioni integrate in Siculingo'
param heartsConsumptionEnabled = true
