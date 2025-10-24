param functionAppName string
param storageAccountName string
param keyVaultName string
param rawBlobContainerName string
param extractedBlobContainerName string
param deploymentContainerName string
param storageBlobEndpoint string
@allowed([512, 2048, 4096])
param instanceMemoryMB int = 2048
@allowed( [40, 1000])
param maxInstanceCount int = 40
param openAIChatModel string
param openAIChatDeploymentName string 
param openAIEmbeddingModel string
param openAIEmbeddingDeploymentName string 
param location string = resourceGroup().location
param docIntelligenceEndpoint string
param aiSearchEndpoint string

var constants = loadJsonContent('./constants.json')
var kvKeys = loadJsonContent('./kvKeys.json')
var deploymentContainerUrl = '${storageBlobEndpoint}${deploymentContainerName}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-04-01' existing = {
  name: storageAccountName
}



resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${functionAppName}-asp'
  location: location
  kind: 'functionapp'
  sku:{
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}


resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  tags: {
    'azd-service-name': 'documentquestionsfunction'
  }
  properties: {
    httpsOnly: true
    serverFarmId: appServicePlan.id
    siteConfig: {
      //linuxFxVersion: 'DOTNETCORE|8.0'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: storageAccount.properties.primaryEndpoints.queue
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: storageAccount.properties.primaryEndpoints.table
        }
        {
          name:  constants.DOCUMENTINTELLIGENCE_KEY
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.DOCUMENTINTELLIGENCE_KEY})'
        }
        {
          name: constants.DOCUMENTINTELLIGENCE_ENDPOINT
          value: docIntelligenceEndpoint
        }
        {
          name: constants.RAW_CONTAINER_NAME
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
           name: constants.STORAGE_ACCOUNT_BLOB_URL
           value: storageAccount.properties.primaryEndpoints.blob
        }
        {
           name: constants.STORAGE_ACCOUNT_QUEUE_URL
           value: storageAccount.properties.primaryEndpoints.queue
        }
        {
          name: constants.STORAGE_ACCOUNT_NAME
          value: storageAccountName
        }
        // {
        //   name: 'WEBSITE_RUN_FROM_PACKAGE'
        //   value: deploymentContainerUrl
        // }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE_BLOB_MI_RESOURCE_ID'
          value: 'SystemAssigned'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        // {
        //   name: 'FUNCTIONS_WORKER_RUNTIME'
        //   value: 'dotnet-isolated'
        // }
        {
          name: constants.AISEARCH_ENDPOINT
          value: aiSearchEndpoint
        }
         {
          name: constants.AISEARCH_KEY
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${kvKeys.AISEARCH_KEY})'
        }
       
      ]
    }
      functionAppConfig: {
        deployment: {
          storage: {
            type: 'blobContainer'
            value: deploymentContainerUrl
            authentication: {
              type: 'SystemAssignedIdentity'
            }
          }
        }
        runtime: {
          name: 'dotnet-isolated'
          version: '8.0'
        }
        scaleAndConcurrency: {
          instanceMemoryMB: instanceMemoryMB
          maximumInstanceCount: maxInstanceCount
        }
      }
  }
}
  resource functionAppConfig 'Microsoft.Web/sites/config@2024-04-01' = {
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
