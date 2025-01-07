param(
	[string] $functionAppName,
    [string] $location,
    [string] $openAiEndpoint,
    [string] $openAiKey,
    [string] $openAIChatModel = "gpt-4",
    [string] $openAIChatDeploymentName = "gpt-4-turbo",
    [string] $openAIEmbeddingModel = "text-embedding-ada-002",
    [string] $openAIEmbeddingDeploymentName =  "text-embedding-ada-002",
    [bool] $localCodeOnly = $false
)

$error.Clear()
$ErrorActionPreference = 'Stop'

$resourceGroupName = $functionAppName + "-rg"
$storageAccountName = ($functionAppName + "storage").ToLower().Replace("-","").Replace("_","")
$docIntelligenceAccountName = $functionAppName + "-aidoc"
$aiSearchName = $functionAppName.ToLower() + "-aisearch"
$keyVaultName = $functionAppName + "-keyvault"


Write-Host "Getting current user object id" -ForegroundColor DarkCyan
$currentUserObjectId = az ad signed-in-user show -o tsv --query id
Write-Host "Current User Object Id: $currentUserObjectId" -ForegroundColor Green

Write-Host "Creating Resource Group $resourceGroupName" -ForegroundColor Green
Write-Host "Location: $location" -ForegroundColor Green
Write-Host "Function App Name: $functionAppName" -ForegroundColor Green
Write-Host "Storage Account Name: $storageAccountName" -ForegroundColor Green
Write-Host "Document Intelligence AI Services Account Name: $docIntelligenceAccountName" -ForegroundColor Green
Write-Host "AI Search Account Name: $aiSearchName" -ForegroundColor Green
Write-Host "Azure OpenAI chat model: $openAIChatModel" -ForegroundColor Green
Write-Host "Azure OpenAI chat deployment name: $openAIChatDeploymentName" -ForegroundColor Green
Write-Host "Azure OpenAI embedding model: $openAIEmbeddingModel" -ForegroundColor Green
Write-Host "Azure OpenAI embedding deployment name: $openAIEmbeddingDeploymentName" -ForegroundColor Green
Write-Host "Azure Open AI Endpoint: $openAiEndpoint" -ForegroundColor Green
if($localCodeOnly -eq $true)
{
    Write-Host "Local code only, skipping Azure Bicep Deployment" -ForegroundColor DarkCyan
}
else
{
   Write-Host "Running Azure Bicep Deployment..." -ForegroundColor DarkCyan
   $result =  az deployment sub create --name $functionAppName --location $location  --template-file ./infra/main.bicep `
       --parameters resourceGroupName=$resourceGroupName location=$location `
       functionAppName=$functionAppName storageAccountName=$storageAccountName `
       docIntelligenceAccountName=$docIntelligenceAccountName aiSearchName=$aiSearchName `
       openAiEndpoint=$openAiEndpoint openAiKey=$openAiKey `
       openAIChatModel=$openAIChatModel openAIChatDeploymentName=$openAIChatDeploymentName `
       openAIEmbeddingModel=$openAIEmbeddingModel openAIEmbeddingDeploymentName=$openAIEmbeddingDeploymentName `
       currentUserObjectId=$currentUserObjectId `
       keyVaultName=$keyVaultName | ConvertFrom-Json -Depth 10

   if(!$?){ exit }


   Push-Location -Path ./DocumentQuestionsFunction
   Write-Host "Publishing $functionAppName to $resourceGroupName" -ForegroundColor Green
   dotnet clean -c release
   dotnet clean -c debug
   func azure functionapp publish $functionAppName 
   Pop-Location
}

Write-Host -ForegroundColor Green "Getting AI Search account account key"
$aiSearchKey = az search admin-key show --resource-group $resourceGroupName  --service-name $aiSearchName -o tsv --query primaryKey
$docIntelligenceKey = az cognitiveservices account keys list --name $docIntelligenceAccountName --resource-group $resourceGroupName -o tsv --query key1


$json = Get-Content 'infra/constants.json' | ConvertFrom-Json
$appSettings = @{
        "$($json.OPENAI_ENDPOINT)" = $openAiEndpoint
        "$($json.OPENAI_KEY)"= $openAiKey
        "$($json.OPENAI_CHAT_MODEL_NAME)"= $openAIChatModel
        "$($json.OPENAI_CHAT_DEPLOYMENT_NAME)" = $openAIChatDeploymentName
     
        "$($json.OPENAI_EMBEDDING_MODEL_NAME)" = $openAIEmbeddingModel 
        "$($json.OPENAI_EMBEDDING_DEPLOYMENT_NAME)" = $openAIEmbeddingDeploymentName

        "$($json.DOCUMENTINTELLIGENCE_ENDPOINT)" = "https://$($location).api.cognitive.microsoft.com/"
        "$($json.DOCUMENTINTELLIGENCE_KEY)" = $docIntelligenceKey
     
        "$($json.AISEARCH_ENDPOINT)"=   "https://$($aiSearchName).search.windows.net"
        "$($json.AISEARCH_KEY)" = $aiSearchKey

        "$($json.STORAGE_ACCOUNT_BLOB_URL.Replace("__", ":"))" = "https://$($storageAccountName).blob.core.windows.net/"
        "$($json.STORAGE_ACCOUNT_QUEUE_URL.Replace("__", ":"))" = "https://$($storageAccountName).queue.core.windows.net/"
        "$($json.STORAGE_ACCOUNT_NAME)" = $storageAccountName
        "$($json.EXTRACTED_CONTAINER_NAME)" = "extracted"
        "$($json.RAW_CONTAINER_NAME)" = "raw"
        
        "UseOpenAIKey" = $true
}

 Write-Host -ForegroundColor Green "Creating console app  local.settings.json"
 Push-Location -Path .\DocumentQuestionsConsole\
 $localsettingsJson = ConvertTo-Json $appSettings -Depth 100
 $localsettingsJson | Out-File -FilePath "local.settings.json"
 Pop-Location
 if(!$?){ exit }

Push-Location -Path .\DocumentQuestionsFunction\
Write-Host "Creating functin app local.settings.json" -ForegroundColor Green
$funcSettings = @{
    "IsEncrypted" = $false
    Values = @{
        "AzureWebJobsStorage"= "UseDevelopmentStorage=true"
        "FUNCTIONS_EXTENSION_VERSION"= "~4"
        "FUNCTIONS_WORKER_RUNTIME"= "dotnet-isolated"
        "WEBSITE_RUN_FROM_PACKAGE"= "1"
        "$($json.OPENAI_ENDPOINT)" = $openAiEndpoint
        "$($json.OPENAI_KEY)"= $openAiKey
        "$($json.OPENAI_CHAT_MODEL_NAME)"= $openAIChatModel
        "$($json.OPENAI_CHAT_DEPLOYMENT_NAME)" = $openAIChatDeploymentName
        "$($json.OPENAI_EMBEDDING_MODEL_NAME)" = $openAIEmbeddingModel 
        "$($json.OPENAI_EMBEDDING_DEPLOYMENT_NAME)" = $openAIEmbeddingDeploymentName

        "$($json.DOCUMENTINTELLIGENCE_ENDPOINT)" = "https://$($location).api.cognitive.microsoft.com/"
        "$($json.DOCUMENTINTELLIGENCE_KEY)" = $docIntelligenceKey

        "$($json.AISEARCH_ENDPOINT)"=   "https://$($aiSearchName).search.windows.net"
        "$($json.AISEARCH_KEY)" = $aiSearchKey

        "$($json.STORAGE_ACCOUNT_BLOB_URL.Replace("__", ":"))" = "https://$($storageAccountName).blob.core.windows.net/"
        "$($json.STORAGE_ACCOUNT_QUEUE_URL.Replace("__", ":"))" = "https://$($storageAccountName).queue.core.windows.net/"
        "$($json.STORAGE_ACCOUNT_NAME)" = $storageAccountName
        "$($json.EXTRACTED_CONTAINER_NAME)" = "extracted"
        "$($json.RAW_CONTAINER_NAME)" = "raw"

        "UseOpenAIKey" = $true
    }
}
 $localsettingsJson = ConvertTo-Json $funcSettings -Depth 100
 $localsettingsJson | Out-File -FilePath "local.settings.json"
 Pop-Location
 if(!$?){ exit }

Push-Location -Path .\DocumentQuestionsConsole\
Write-Host "Building Connsole App" -ForegroundColor Green
dotnet build . -c debug -warnaserror none
Pop-Location

.\DocumentQuestionsConsole\bin\Debug\net8.0\dq.exe
