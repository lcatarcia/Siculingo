using './main.bicep'

param environmentName = 'beta'
param appNamePrefix = 'siculingo'
param appServiceSku = 'F1'
param sqlTier = 'FreeOffer'

// Replace with your Microsoft Entra admin login/UPN and Object ID before provisioning:
param sqlEntraAdminLogin = 'admin-placeholder@example.com'
param sqlEntraAdminObjectId = '00000000-0000-0000-0000-000000000000'
param sqlEntraAdminPrincipalType = 'User'

param legalControllerName = 'Progetto sperimentale Siculingo'
param legalContactChannel = 'Segnalazioni integrate in Siculingo'
param heartsConsumptionEnabled = true
