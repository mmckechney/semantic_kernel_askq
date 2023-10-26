
param cognitiveServicesAccountName string
param cogSvcsPrincipalId string
param functionAppName string
param functionPrincipalId string
param storageAccountName string
param rawBlobContainerName string
param extractedBlobContainerName string

resource storageAccount 'Microsoft.Storage/storageAccounts@2021-04-01' existing = {
  name: storageAccountName
}
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2021-06-01' existing = {
  name: 'default'
  parent: storageAccount
}

resource rawBlobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-06-01' existing= {
  name: rawBlobContainerName
  parent: blobService
}

resource extractedBlobContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-06-01' existing = {
  parent: blobService
  name: extractedBlobContainerName
}

var storageBlobDataContrib = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource func_blob_contrib_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(functionAppName, storageBlobDataContrib, resourceGroup().id)
  scope: blobService
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContrib)
    principalId: functionPrincipalId
  }
}

resource cogsvc_blob_contrib_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cognitiveServicesAccountName, storageBlobDataContrib, resourceGroup().id)
  scope: extractedBlobContainer
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContrib)
    principalId: cogSvcsPrincipalId
   }
  
}

resource cogsvc_blob_reader_role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cognitiveServicesAccountName, storageBlobDataReader, resourceGroup().id)
  scope: rawBlobContainer
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReader)
    principalId: cogSvcsPrincipalId
  }
}
