@description('Base name for every resource. Keep it short — it seeds globally unique names.')
@minLength(3)
@maxLength(18)
param appName string = 'fpai-connect'

@description('Deployment region.')
param location string = resourceGroup().location

@allowed(['dev', 'test', 'prod'])
param environmentName string = 'prod'

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

@description('App Service plan size. B1 is the smallest that supports Always On.')
@allowed(['B1', 'B2', 'S1', 'P0v3', 'P1v3'])
param appServiceSku string = 'B1'

var uniqueSuffix = uniqueString(resourceGroup().id)
var webAppName = '${appName}-${environmentName}-${uniqueSuffix}'
var sqlServerName = '${appName}-sql-${environmentName}-${uniqueSuffix}'
var databaseName = '${appName}-db'
var planName = '${appName}-plan-${environmentName}'
var storageName = toLower(replace('${appName}st${uniqueSuffix}', '-', ''))

// ---------------------------------------------------------------- storage (documents)
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: take(storageName, 24)
  location: location
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

// ---------------------------------------------------------------- azure sql
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
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
  sku: environmentName == 'prod'
    ? { name: 'S1', tier: 'Standard' }
    : { name: 'Basic', tier: 'Basic' }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
  }
}

// ---------------------------------------------------------------- app service
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  sku: { name: appServiceSku }
  kind: 'linux'
  properties: { reserved: true }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
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
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${appName}-insights-${environmentName}'
  location: location
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
    Cors__AllowedOrigins__0: 'https://${webAppName}.azurewebsites.net'
    Storage__Provider: 'AzureBlob'
    Storage__ContainerName: 'documents'
    Storage__ConnectionString: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
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
