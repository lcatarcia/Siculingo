@description('Short environment identifier (e.g. beta).')
@minLength(2)
@maxLength(12)
param environmentName string = 'beta'

@description('Azure region for all resources (e.g. italynorth or westeurope).')
param location string = resourceGroup().location

@description('Globally unique prefix for App Service and SQL Server names.')
@minLength(3)
@maxLength(20)
param appNamePrefix string = 'siculingo'

@description('App Service Linux SKU: F1 (0 EUR/month Free tier, shared compute with daily CPU/bandwidth quota) or B1 (~12-13 EUR/month dedicated Basic tier with Always On and custom domain TLS).')
@allowed([
  'F1'
  'B1'
])
param appServiceSku string = 'F1'

@description('Azure SQL tier: FreeOffer (0 EUR/month guaranteed with AutoPause at monthly free limit, 100k vCore-s/mo + 32 GB) or Basic (~4.60 EUR/month 5 DTU without idle auto-pause).')
@allowed([
  'FreeOffer'
  'Basic'
])
param sqlTier string = 'FreeOffer'

@description('Microsoft Entra administrator display name / UPN for passwordless Azure SQL administration.')
param sqlEntraAdminLogin string

@description('Microsoft Entra Object ID (User, Group, or Service Principal) for passwordless Azure SQL administration.')
param sqlEntraAdminObjectId string

@description('Principal type of the Microsoft Entra SQL administrator.')
@allowed([
  'User'
  'Group'
  'Application'
])
param sqlEntraAdminPrincipalType string = 'User'

@description('Data controller display name shown in /api/v1/system/legal.')
param legalControllerName string = 'Progetto sperimentale Siculingo'

@description('Contact channel shown in /api/v1/system/legal.')
param legalContactChannel string = 'Canale segnalazioni in-app Siculingo'

@description('Whether hearts consumption is active on the learning path.')
param heartsConsumptionEnabled bool = true

@description('GitHub repository (owner/name) authorized via OIDC federated credential for deployments.')
param githubRepository string = 'lcatarcia/Siculingo'

var uniqueSuffix = substring(uniqueString(resourceGroup().id, appNamePrefix, environmentName), 0, 6)
var appServicePlanName = '${appNamePrefix}-${environmentName}-plan-${uniqueSuffix}'
var webAppName = '${appNamePrefix}-${environmentName}-app-${uniqueSuffix}'
var sqlServerName = '${appNamePrefix}-${environmentName}-sql-${uniqueSuffix}'
var sqlDatabaseName = 'Siculingo_${environmentName}'
var runtimeIdentityName = '${appNamePrefix}-${environmentName}-web-id-${uniqueSuffix}'
var deployIdentityName = '${appNamePrefix}-${environmentName}-gha-id-${uniqueSuffix}'
var isFreeWebTier = appServiceSku == 'F1'

var websiteContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'de139f84-1756-47ae-9be6-808fbbe84772')
var sqlServerContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '6d8ee4ec-f05a-4a1d-8b00-a9b17e38b437')

resource runtimeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: runtimeIdentityName
  location: location
}

resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: deployIdentityName
  location: location
}

resource deployOidcCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deployIdentity
  name: 'github-actions-${environmentName}'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:${environmentName}'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource deployWebContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployIdentity.id, websiteContributorRoleId)
  scope: resourceGroup()
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: websiteContributorRoleId
  }
}

resource deploySqlContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployIdentity.id, sqlServerContributorRoleId)
  scope: resourceGroup()
  properties: {
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: sqlServerContributorRoleId
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSku
    tier: isFreeWebTier ? 'Free' : 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: sqlEntraAdminPrincipalType
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

resource allowAzureServicesFirewall 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: sqlTier == 'FreeOffer'
    ? {
        name: 'GP_S_Gen5'
        tier: 'GeneralPurpose'
        family: 'Gen5'
        capacity: 1
      }
    : {
        name: 'Basic'
        tier: 'Basic'
        capacity: 5
      }
  properties: sqlTier == 'FreeOffer'
    ? {
        collation: 'SQL_Latin1_General_CP1_CI_AS'
        maxSizeBytes: 34359738368
        minCapacity: json('0.5')
        autoPauseDelay: 60
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause'
        requestedBackupStorageRedundancy: 'Local'
      }
    : {
        collation: 'SQL_Latin1_General_CP1_CI_AS'
        maxSizeBytes: 2147483648
        requestedBackupStorageRedundancy: 'Local'
      }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${runtimeIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet Siculingo.Api.dll'
      alwaysOn: !isFreeWebTier
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      healthCheckPath: isFreeWebTier ? null : '/health/live'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: runtimeIdentity.properties.clientId
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'Hosting__EnableForwardedHeaders'
          value: 'true'
        }
        {
          name: 'Hosting__ForwardLimit'
          value: '1'
        }
        {
          name: 'DataProtection__KeysDirectory'
          value: '/home/site/dataprotection-keys'
        }
        {
          name: 'Hearts__ConsumptionEnabled'
          value: heartsConsumptionEnabled ? 'true' : 'false'
        }
        {
          name: 'Legal__ControllerName'
          value: legalControllerName
        }
        {
          name: 'Legal__ContactChannel'
          value: legalContactChannel
        }
      ]
      connectionStrings: [
        {
          name: 'Database'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;ConnectRetryCount=5;ConnectRetryInterval=10;Authentication=Active Directory Managed Identity;User Id=${runtimeIdentity.properties.clientId};'
        }
      ]
    }
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output runtimeIdentityName string = runtimeIdentity.name
output runtimeIdentityClientId string = runtimeIdentity.properties.clientId
output deployIdentityName string = deployIdentity.name
output deployIdentityClientId string = deployIdentity.properties.clientId
output sqlServerName string = sqlServer.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
