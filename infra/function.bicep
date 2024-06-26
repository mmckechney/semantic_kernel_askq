param functionAppName string
param storageAccountName string
param keyVaultName string
param rawBlobContainerName string
param extractedBlobContainerName string
param openAIChatModel string = 'gpt-4-32k'
param openAIChatDeploymentName string = 'gpt-4-32k'
param openAIEmbeddingModel string = 'text-embedding-ada-002'
param openAIEmbeddingDeploymentName string = 'text-embedding-ada-002'
param location string = resourceGroup().location

var constants = loadJsonContent('./constants.json')
var kvKeys = loadJsonContent('./kvKeys.json')
resource storageAccount 'Microsoft.Storage/storageAccounts@2021-04-01' existing = {
  name: storageAccountName
}


var storageAccountConnection = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'


resource appServicePlan 'Microsoft.Web/serverfarms@2021-01-15' = {
  name: '${functionAppName}-asp'
  location: location
  kind: 'app'
  sku:{
    name: 'Y1'
    tier: 'Dynamic'
  }
}


resource functionAppConfig 'Microsoft.Web/sites/config@2022-09-01' = {
  name : 'web'
  kind: 'string'
  parent: functionApp
  properties: {
    cors: {
      allowedOrigins: [
        'https://portal.azure.com'
      ]
      supportCredentials: true
    }
  }
  
}
resource functionApp 'Microsoft.Web/sites@2022-09-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: storageAccountConnection
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageAccountConnection
        }
        // {
        //   name: 'WEBSITE_CONTENTSHARE'
        //   value: functionAppName
        // }
        {
          name:  constants.DOCUMENTINTELLIGENCE_KEY
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.DOCUMENTINTELLIGENCE_KEY})'
        }
        {
          name: constants.DOCUMENTINTELLIGENCE_ENDPOINT
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.DOCUMENTINTELLIGENCE_KEY})'
        }
        {
          name: constants.CONTAINER_NAME
          value: rawBlobContainerName
        }
        {
          name: constants.EXTRACTED_CONTAINER_NAME
          value: extractedBlobContainerName
        }
        {
          name:  constants.OPENAI_CHAT_MODEL_NAME
          value: openAIChatModel
        }
        {
          name: constants.OPENAI_CHAT_DEPLOYMENT_NAME
          value: openAIChatDeploymentName
        }
        {
          name: constants.OPENAI_EMBEDDING_MODEL_NAME
          value: openAIEmbeddingModel
        }
        {
          name: constants.OPENAI_EMBEDDING_DEPLOYMENT_NAME
          value: openAIEmbeddingDeploymentName
        }
        {
          name: constants.OPENAI_KEY
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.OPENAI_KEY})'
        }
        {
          name: constants.OPENAI_ENDPOINT
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.OPENAI_ENDPOINT})'
        }
        {
          name : constants.RAW_STORAGE_CONNECTION_STRING
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.RAW_STORAGE_CONNECTION_STRING})'
        } 
        {
          name : constants.STORAGE_CONNECTION_STRING
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.STORAGE_CONNECTION_STRING})'
        }
        {
          name: constants.STORAGE_ACCOUNT_NAME
          value: storageAccountName
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: constants.AISEARCH_ENDPOINT
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.AISEARCH_ENDPOINT})'
        }
        {
          name: constants.AISEARCH_KEY
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.AISEARCH_KEY})'
        }
      ]
    }
  }
}
resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: '${functionAppName}-insights'
  location: location
  kind: 'web'
  properties: { 
    Application_Type: 'web'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    WorkspaceResourceId: logAnalytics.id
  }
  tags: {
    // circular dependency means we can't reference functionApp directly  /subscriptions/<subscriptionId>/resourceGroups/<rg-name>/providers/Microsoft.Web/sites/<appName>"
     'hidden-link:/subscriptions/${subscription().id}/resourceGroups/${resourceGroup().name}/providers/Microsoft.Web/sites/${functionAppName}': 'Resource'
  }
}


resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${functionAppName}-log'
  location: location
  properties: {
    retentionInDays: 30
  }
}



output functionAppId string = functionApp.identity.principalId
