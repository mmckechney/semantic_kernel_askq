param(
	[string] $functionAppName,
    [string] $location,
    [string] $openAiEndpoint,
    [string] $openAiKey
)

$error.Clear()
$ErrorActionPreference = 'Stop'

$resourceGroupName = $functionAppName + "-rg"
$storageAccountName = ($functionAppName + "storage").ToLower().Replace("-","").Replace("_","")
$cognitiveServicesAccountName = $functionAppName + "-cogsvcs"
$cognitiveSearchName = $functionAppName.ToLower() + "-cogsearch"
$keyVaultName = $functionAppName + "-keyvault"

Write-Host "Creating Resource Group $resourceGroupName in $location" -ForegroundColor Green
#write out the values for each variable
Write-Host "Function App Name: $functionAppName" -ForegroundColor Green
Write-Host "Location: $location" -ForegroundColor Green
Write-Host "Storage Account Name: $storageAccountName" -ForegroundColor Green
Write-Host "Cognitive Services Account Name: $cognitiveServicesAccountName" -ForegroundColor Green
Write-Host "Cognitive Search Account Name: $cognitiveSearchName" -ForegroundColor Green

Write-Host "Open AI Endpoint: $openAiEndpoint" -ForegroundColor Green

az deployment sub create --location $location  --template-file ./infra/main.bicep --parameters resourceGroupName=$resourceGroupName location=$location functionAppName=$functionAppName storageAccountName=$storageAccountName cognitiveServicesAccountName=$cognitiveServicesAccountName cognitiveSearchName=$cognitiveSearchName openAiEndpoint=$openAiEndpoint openAiKey=$openAiKey keyVaultName=$keyVaultName -o table

if(!$?){ exit }


Write-Host "Publishing $functionAppName to $resourceGroupName" -ForegroundColor Green
func azure functionapp publish $functionAppName 

