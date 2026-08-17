@description('Base name for every resource. Keep it short — it seeds globally unique names.')
@minLength(3)
@maxLength(18)
param appName string = 'fpai-connect'

@description('Deployment region.')
param location string = resourceGroup().location

@description('Target environment. Picks the resource group this deploys into, the free-tier eligibility, and the "env" tag on every resource.')
@allowed(['dev', 'test', 'prod'])
param environmentName string

@description('Azure SQL administrator login.')
param sqlAdminLogin string

@description('Azure SQL administrator password. Pass securely; never commit it.')
@secure()
param sqlAdminPassword string

@description('Signing key for JWT access tokens. At least 32 characters.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Google OAuth client id. Leave empty until Google sign-in is configured.')
param googleClientId string = ''

@description('Microsoft (Entra) OAuth client id. Leave empty until Microsoft sign-in is configured.')
param microsoftClientId string = ''

@description('App Service plan size. F1 is free but has no Always On and a daily CPU quota.')
@allowed(['F1', 'B1', 'B2', 'S1', 'P0v3', 'P1v3'])
param appServiceSku string = 'F1'

var uniqueSuffix = uniqueString(resourceGroup().id)
var webAppName = '${appName}-${environmentName}-${uniqueSuffix}'
var sqlServerName = '${appName}-sql-${environmentName}-${uniqueSuffix}'
var databaseName = '${appName}-db-${environmentName}'
var planName = '${appName}-plan-${environmentName}'
// Storage account names are 3-24 chars, lowercase letters/numbers only, and globally unique
// across all of Azure — so the full uniqueSuffix must survive intact; appName is capped at 6
// chars here (rather than dropped) so the env is still legible in the name without risking
// truncating the part that actually guarantees uniqueness.
var storageName = toLower('${take(replace(appName, '-', ''), 6)}${environmentName}${uniqueSuffix}')

// Mandatory tag set — applied to every resource type below that supports `tags`. A few nested
// config resource types (blobServices, containers, firewallRules, site config, role
// assignments) don't have a `tags` property in their ARM schema, so they're left untagged.
var commonTags = {
  env: environmentName
  region: location
  subscription: subscription().subscriptionId
  githubRepo: 'athelite-mgt/FPAI'
}

// Built-in "Storage Blob Data Contributor" role — lets the App Service's managed identity
// read/write blobs with no account key anywhere.
var storageBlobDataContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

// ---------------------------------------------------------------- storage (documents)
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take(storageName, 24)
  location: location
  tags: commonTags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource documentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'documents'
  properties: { publicAccess: 'None' }
}

// Passwordless blob access: the App Service's own system-assigned identity authenticates via
// DefaultAzureCredential (see AzureBlobFileStorage.cs) instead of an account key.
resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, webApp.id, storageBlobDataContributorRoleId)
  properties: {
    roleDefinitionId: storageBlobDataContributorRoleId
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- azure sql
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  tags: commonTags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// Lets App Service reach the database without opening it to the internet.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: commonTags
  // Serverless General Purpose, Gen5, up to 2 vCores — the backend has no workload heavy
  // enough to need more, and this SKU is eligible for Azure SQL's free monthly limit (one
  // free database per subscription; whichever environment is deployed first gets it).
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
  }
}

// ---------------------------------------------------------------- app service
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: commonTags
  sku: { name: appServiceSku }
  kind: 'linux'
  properties: { reserved: true }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: commonTags
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/api/health'
      // App settings live in the dedicated `appsettings` resource below. Declaring them here
      // as well would have the two resources overwrite each other on redeploy.
      connectionStrings: [
        {
          name: 'Default'
          type: 'SQLAzure'
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'
        }
      ]
    }
  }
}

// ---------------------------------------------------------------- observability
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${appName}-logs-${environmentName}'
  location: location
  tags: commonTags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights-${environmentName}'
  location: location
  tags: commonTags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource insightsSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: webApp
  name: 'appsettings'
  properties: {
    ASPNETCORE_ENVIRONMENT: environmentName == 'prod' ? 'Production' : 'Staging'
    Database__Provider: 'SqlServer'
    Jwt__SigningKey: jwtSigningKey
    Jwt__Issuer: 'FpaiConnect'
    Jwt__Audience: 'FpaiConnect.Client'
    Authentication__Google__ClientId: googleClientId
    Authentication__Microsoft__ClientId: microsoftClientId
    Cors__AllowedOrigins__0: 'https://${webAppName}.azurewebsites.net'
    Storage__Provider: 'AzureBlob'
    Storage__ContainerName: 'documents'
    // No account key anywhere: AzureBlobFileStorage authenticates via DefaultAzureCredential
    // using the App Service's own managed identity (granted Storage Blob Data Contributor
    // above) whenever AccountUrl is set instead of a connection string.
    Storage__AccountUrl: 'https://${storage.name}.blob.${environment().suffixes.storage}'
    Seed__Enabled: environmentName == 'prod' ? 'false' : 'true'
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = databaseName
output storageAccountName string = storage.name
