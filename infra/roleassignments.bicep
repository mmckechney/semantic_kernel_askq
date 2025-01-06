
param cogSvcsPrincipalId string
param functionPrincipalId string
param currentUserObjectId string


var storageBlobDataContrib = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageBlobDataReader = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'
var storageBlobDataOwner = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContrib = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var cogServicesContrib = '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68'
var aiSearchContrib = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContrib = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
var asSearchReader = '1407120a-92aa-4202-b7e9-c0e197c71c8f'

var roleAssignments = [
  storageBlobDataContrib
  storageBlobDataReader
  storageBlobDataOwner
  storageQueueDataContrib
  keyVaultSecretsUser
  cogServicesContrib
  aiSearchContrib
  searchServiceContrib
  asSearchReader
]

var funcAssignments = [for role in roleAssignments: {
    role: role  
    principalId: functionPrincipalId  
}]

var userAssignments = [for role in roleAssignments: {
  role: role  
  principalId: currentUserObjectId  
}]

var cogSvcsAssignments = [for role in roleAssignments: {
  role: role  
  principalId: cogSvcsPrincipalId  
}]

var combined = union(funcAssignments, userAssignments, cogSvcsAssignments)

resource roleAssignmentsResource 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (assignment, setIndex) in combined: {  
  name: guid(assignment.principalId , assignment.role, resourceGroup().id)  
  scope: resourceGroup()  
  properties: {  
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', assignment.role)  
    principalId: assignment.principalId  
  }  
}]  


