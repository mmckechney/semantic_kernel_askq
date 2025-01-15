param(
    [Parameter(Mandatory = $True)]
	[string] $functionAppName,
    [Parameter(Mandatory = $True)]
    [string] $location,
    [string] $openAILocation,
    [string] $openAiEndpoint,
    [string] $openAiKey,
    [string] $openAIChatModel = "gpt-4o",
    [string] $openAIChatDeploymentName = "gpt-4o",
    [string] $openAIEmbeddingModel = "text-embedding-ada-002",
    [string] $openAIEmbeddingDeploymentName =  "text-embedding-ada-002",
    [bool] $localCodeOnly = $false
)

$error.Clear()
$ErrorActionPreference = 'Stop'

$safeFuncAppName =  $functionAppName.ToLower() -replace '[-_]', ''
$safeFuncAppName  = $safeFuncAppName.Substring(0, [math]::Min(13, $safeFuncAppName.Length))

$resourceGroupName = $functionAppName + "-rg"
$storageAccountName = $safeFuncAppName + "storage"
$docIntelligenceAccountName = $functionAppName + "-aidoc"
$aiSearchName = $safeFuncAppName + "-aisearch"
$keyVaultName = $functionAppName + "-keyvault"
$openAiServiceName = $safeFuncAppName + "-openai"

if($openAILocation.Length -eq 0)
{
    $openAILocation = $location
}

$deployOpenAi = $true
Write-Host "Getting current user object id" -ForegroundColor DarkCyan
$currentUserObjectId = az ad signed-in-user show -o tsv --query id
Write-Host "Current User Object Id: $currentUserObjectId" -ForegroundColor Green

Write-Host "Creating Resource Group $resourceGroupName" -ForegroundColor Green
Write-Host "Location: $location" -ForegroundColor Green
Write-Host "Function App Name: $functionAppName" -ForegroundColor Green
Write-Host "Storage Account Name: $storageAccountName" -ForegroundColor Green
Write-Host "Document Intelligence AI Services Account Name: $docIntelligenceAccountName" -ForegroundColor Green
Write-Host "AI Search Account Name: $aiSearchName" -ForegroundColor Green
if($openAiEndpoint.Length -ne 0 -and $openAiKey.Length -ne 0)
{
    Write-Host "OpenAI endpoint and key provided, using existing Azure OpenAI deployment" -ForegroundColor DarkCyan
    Write-Host "Open AI Endpoint: $openAiEndpoint" -ForegroundColor Green
    $deployOpenAi = $false
}
else 
{
    Write-Host "OpenAI Location: $openAILocation" -ForegroundColor Green
    Write-Host "OpenAI Service Name: $openAiServiceName" -ForegroundColor Green
}    

Write-Host "OpenAI chat model: $openAIChatModel" -ForegroundColor Green
Write-Host "OpenAI chat deployment name: $openAIChatDeploymentName" -ForegroundColor Green
Write-Host "OpenAI embedding model: $openAIEmbeddingModel" -ForegroundColor Green
Write-Host "OpenAI embedding deployment name: $openAIEmbeddingDeploymentName" -ForegroundColor Green
  

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
       openAILocation=$openAILocation openAIServiceName=$openAiServiceName deployOpenAI=$deployOpenAi `
       openAIEndpoint=$openAiEndpoint openAIKey=$openAiKey `
       openAIChatModel=$openAIChatModel openAIChatDeploymentName=$openAIChatDeploymentName `
       openAIEmbeddingModel=$openAIEmbeddingModel openAIEmbeddingDeploymentName=$openAIEmbeddingDeploymentName `
       currentUserObjectId=$currentUserObjectId `
       keyVaultName=$keyVaultName | ConvertFrom-Json -Depth 10

   if(!$?){ exit }


   Push-Location -Path ./DocumentQuestionsFunction
   Write-Host "Publishing $functionAppName to $resourceGroupName" -ForegroundColor Green
   dotnet clean -c release
   dotnet clean -c debug
   func azure functionapp publish $functionAppName  --dotnet-isolated
   Pop-Location
}

Write-Host -ForegroundColor Green "Getting AI Search account account key"
$aiSearchKey = az search admin-key show --resource-group $resourceGroupName  --service-name $aiSearchName -o tsv --query primaryKey
$docIntelligenceKey = az cognitiveservices account keys list --name $docIntelligenceAccountName --resource-group $resourceGroupName -o tsv --query key1

if(($openAiEndpoint.Length -eq 0 -or $openAiKey.Length -eq 0) -and $openAiServiceName.Length -ne 0)
{
    Write-Host -ForegroundColor Green "Getting OpenAI key and endpoint"
    $openAiKey =  az cognitiveservices account keys list --name $openAiServiceName --resource-group $resourceGroupName -o tsv --query key1
    $openAiEndpoint =  az cognitiveservices account show --name $openAiServiceName --resource-group $resourceGroupName -o tsv --query properties.endpoint
}

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
Write-Host "Creating function app local.settings.json" -ForegroundColor Green
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
dotnet build . -c debug -nowarn:*
Pop-Location

if(!$?){ exit }
Push-Location -Path .\DocumentQuestionsConsole\
Write-Host "Running Connsole App" -ForegroundColor Green
dotnet run --no-build -- -h
Pop-Location

