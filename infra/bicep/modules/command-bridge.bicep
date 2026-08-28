// Command Bridge - Azure Function App (Consumption) that forwards Service Bus commands to IoT Hub C2D.
param location string = resourceGroup().location
param environment string
param serviceBusConnectionString string
param iotHubHost string
param iotHubServiceConnection string = ''
param appInsightsInstrumentationKey string = ''
param tags object = {}

var functionAppName = 'func-cmdbridge-${environment}'

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'stcmdbridge${environment}${uniqueString(resourceGroup().id)}'
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: { allowSharedKeyAccess: true }
  tags: tags
}

resource plan 'Microsoft.Web/serverFarms@2023-01-01' = {
  name: 'plan-cmdbridge-${environment}'
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' } // Consumption plan
  properties: { reserved: false }
  tags: tags
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    siteConfig: {
      appSettings: [
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'AzureWebJobsStorage', value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${listKeys(storage.id, '2023-01-01').keys[0].value}' }
        { name: 'ServiceBusConnection', value: serviceBusConnectionString }
        { name: 'IoTHubHost', value: iotHubHost }
        { name: 'IoTHubServiceConnection', value: iotHubServiceConnection }
        { name: 'APPINSIGHTS_INSTRUMENTATIONKEY', value: appInsightsInstrumentationKey }
      ]
      netFrameworkVersion: 'v8.0'
      linuxFxVersion: 'DOTNET-ISOLATED|8.0'
    }
    httpsOnly: true
  }
  tags: tags
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${functionApp.defaultHostName}'
