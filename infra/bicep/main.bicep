// ============================================================
// Bicep — Remote Config Service Infrastructure
// Deploys: App Service / AKS + SQL DB + Redis + Service Bus + IoT Hub ref
// ============================================================

targetScope = 'resourceGroup'

@description('Environment name: dev / staging / prod')
param environment string = 'dev'

@description('SQL Server name (globally unique)')
param sqlServerName string

@description('SQL Database name')
param sqlDatabaseName string = 'iot-remote-config-${environment}'

@description('Key Vault name for secrets')
param keyVaultName string

@description('ACR name for container images')
param acrName string

@description('Docker image tag (git sha)')
param imageTag string = 'latest'

@description('Service Bus namespace name')
param serviceBusNamespace string = 'sb-iot-${environment}'

@description('Redis cache name')
param redisName string = 'redis-rc-${environment}'

// ─── Variables ─────────────────────────────────────────
var location = resourceGroup().location
var appServicePlanName = 'plan-remote-config-${environment}'
var sqlAdminGroupId = '00000000-0000-0000-0000-000000000000' // Replace with actual AAD group

// ─── App Service Plan ──────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: environment == 'prod' ? 'P1v3' : 'B2'
    tier: environment == 'prod' ? 'PremiumV3' : 'Basic'
    capacity: environment == 'prod' ? 3 : 1
  }
  properties: {
    reserved: true
  }
}

// ─── App Service (Linux container) ────────────────────
resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: 'app-remote-config-${environment}'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      linuxFxVersion: 'DOCKER'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appCommandLine: 'dotnet RemoteConfig.Api.dll'
      healthCheckPath: '/health'
      healthCheckEnabled: true
    }
    httpsOnly: true
  }
  dependsOn: [appServicePlan]
}

// ─── Container config ──────────────────────────────────
resource siteConfig 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: appService
  name: 'web'
  properties: {
    appSettings: [
      { name: 'DOCKER_REGISTRY_SERVER_URL', value: 'https://${acrName}.azurecr.io' }
      { name: 'WEBSITES_PORT', value: '8080' }
      { name: 'ASPNETCORE_ENVIRONMENT', value: toUpper(environment) }
      { name: 'ApplicationInsights:ConnectionString', value: 'InstrumentationKey=${appInsights.properties.InstrumentationKey}' }
    ]
    healthCheckPath: '/health'
  }
}

// ─── Application Insights ──────────────────────────────
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-remote-config-${environment}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'la-remote-config-${environment}'
  location: location
  properties: {
    sku: {
      name: environment == 'prod' ? 'PerGB2018' : 'Free'
    }
  }
}

// ─── SQL Server ────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-02-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    version: '12.0'
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'Group'
      login: 'sql-admins'
      sid: sqlAdminGroupId
      azureADOnlyAuthentication: true
    }
  }
}

// ─── SQL Database ──────────────────────────────────────
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-02-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: environment == 'prod' ? 'S3' : 'S1'
    tier: 'Standard'
  }
  properties: {
    maxSizeBytes: environment == 'prod' ? 53687091200 : 21474836480 // 50GB / 20GB
    collation: 'SQL_Latin1_General_CP1_CI_AS'
  }
}

// ─── Firewall: Allow Azure services ────────────────────
resource firewallRule 'Microsoft.Sql/servers/firewallrules@2023-02-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ─── Azure Cache for Redis ─────────────────────────────
resource redis 'Microsoft.Cache/redis@2023-08-01' = {
  name: redisName
  location: location
  properties: {
    sku: {
      name: environment == 'prod' ? 'Standard' : 'Basic'
      family: 'C'
      capacity: environment == 'prod' ? 1 : 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

// ─── Service Bus Namespace + Queue ────────────────────
resource sbNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusNamespace
  location: location
  sku: {
    name: environment == 'prod' ? 'Standard' : 'Basic'
    tier: environment == 'prod' ? 'Standard' : 'Basic'
  }
  properties: {
    disableLocalAuth: true
  }
}

resource commandQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'device-commands'
  properties: {
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
    defaultMessageTtl: 'PT5M'
    deadLetteringOnMessageExpiration: true
  }
}

resource resultQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'device-commands-results'
  properties: {
    maxDeliveryCount: 3
    lockDuration: 'PT30S'
    defaultMessageTtl: 'PT5M'
    deadLetteringOnMessageExpiration: true
  }
}

// ─── Grant App Service MI access to Key Vault ──────────
resource kvAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-02-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: appService.identity.principalId
        permissions: {
          secrets: ['get', 'list']
          certificates: ['get', 'list']
        }
      }
    ]
  }
}

// Reference existing Key Vault (created in Phase 0)
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

// ─── Outputs ───────────────────────────────────────────
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output sqlConnectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Managed Identity;'
output redisConnectionString string = redis.properties.hostName
output serviceBusEndpoint string = sbNamespace.properties.serviceBusEndpoint
output appServiceIdentityPrincipalId string = appService.identity.principalId
