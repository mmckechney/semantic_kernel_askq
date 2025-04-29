#!/usr/bin/env pwsh

# Get the function app name from environment variables
$functionAppName = $(azd env get-values --output json | ConvertFrom-Json).AZURE_FUNCTIONAPP_NAME

# Publish the function app to Azure
Write-Host "Publishing function app to $functionAppName"
Push-Location -Path ./DocumentQuestionsFunction
func azure functionapp publish $functionAppName
Pop-Location

Write-Host "Function app deployment completed"


# Create local settings file for Console app with the environment values
# Get updated values after potentially setting them above
$envValues = azd env get-values --output json | ConvertFrom-Json

$appSettings = az functionapp config appsettings list --name $envValues.AZURE_FUNCTIONAPP_NAME --resource-group $envValues.AZURE_RESOURCEGROUP_NAME | ConvertFrom-Json
Write-Host -ForegroundColor Green "Getting AI Search account account key"
$aiSearchKey = az search admin-key show --resource-group $envValues.AZURE_RESOURCEGROUP_NAME   --service-name $envValues.AZURE_AISEARCH_NAME -o tsv --query primaryKey
$docIntelligenceKey = az cognitiveservices account keys list --name $envValues.AZURE_DOCUMENTINTELLIGENCE_ACCOUNT_NAME --resource-group $envValues.AZURE_RESOURCEGROUP_NAME  -o tsv --query key1


Write-Host -ForegroundColor Green "Getting OpenAI key and endpoint"
$openAiKey =  az cognitiveservices account keys list --name $envValues.AZURE_OPENAI_SERVICE_NAME --resource-group $envValues.AZURE_RESOURCEGROUP_NAME  -o tsv --query key1
$openAiEndpoint =  az cognitiveservices account show --name $envValues.AZURE_OPENAI_SERVICE_NAME --resource-group $envValues.AZURE_RESOURCEGROUP_NAME  -o tsv --query properties.endpoint


#Write-Host $appSettings 

$appSettingsHash = @{}
$appSettings | ForEach-Object { $appSettingsHash[$_.name] = $_.value }

$json = Get-Content 'infra/constants.json' | ConvertFrom-Json
$localSettings = @{
        "$($json.OPENAI_ENDPOINT)" = $openAiEndpoint
        "$($json.OPENAI_KEY)"= $openAiKey
        "$($json.OPENAI_CHAT_MODEL_NAME)"= $appSettingsHash[$json.OPENAI_CHAT_MODEL_NAME]
        "$($json.OPENAI_CHAT_DEPLOYMENT_NAME)" = $appSettingsHash[$json.OPENAI_CHAT_DEPLOYMENT_NAME]
     
        "$($json.OPENAI_EMBEDDING_MODEL_NAME)" = $appSettingsHash[$json.OPENAI_EMBEDDING_MODEL_NAME]
        "$($json.OPENAI_EMBEDDING_DEPLOYMENT_NAME)" =  $appSettingsHash[$json.OPENAI_EMBEDDING_DEPLOYMENT_NAME]

        "$($json.DOCUMENTINTELLIGENCE_ENDPOINT)" = $appSettingsHash[$json.DOCUMENTINTELLIGENCE_ENDPOINT]
        "$($json.DOCUMENTINTELLIGENCE_KEY)" = $docIntelligenceKey
     
        "$($json.AISEARCH_ENDPOINT)"=   $appSettingsHash[$json.AISEARCH_ENDPOINT]
        "$($json.AISEARCH_KEY)" = $aiSearchKey

        "$($json.STORAGE_ACCOUNT_BLOB_URL.Replace("__", ":"))" = $appSettingsHash[$json.STORAGE_ACCOUNT_BLOB_URL]
        "$($json.STORAGE_ACCOUNT_QUEUE_URL.Replace("__", ":"))" = $appSettingsHash[$json.STORAGE_ACCOUNT_QUEUE_URL]
        "$($json.STORAGE_ACCOUNT_NAME)" = $appSettingsHash[$json.STORAGE_ACCOUNT_NAME]
        "$($json.EXTRACTED_CONTAINER_NAME)" = "extracted"
        "$($json.RAW_CONTAINER_NAME)" = "raw"
        
        "UseOpenAIKey" = $true
}
#Write-Host $localSettings | ConvertTo-Json -Depth 10

# Ensure directory exists
$consoleDirPath = "./DocumentQuestionsConsole"
if (Test-Path $consoleDirPath) {
    # Save local settings to file
    $localSettings | ConvertTo-Json -Depth 10 | Out-File -FilePath "$consoleDirPath/local.settings.json"
    Write-Host "Created $consoleDirPath/local.settings.json for DocumentQuestionsConsole"
} else {
    Write-Warning "Directory $consoleDirPath not found. Skipping local.settings.json creation."
}

$localSettings[$json.OPENAI_ENDPOINT] = $appSettingsHash[$json.OPENAI_ENDPOINT]
$localSettings[$json.OPENAI_KEY] = $appSettingsHash[$json.OPENAI_KEY]
$localSettings[$json.DOCUMENTINTELLIGENCE_KEY] = $appSettingsHash[$json.DOCUMENTINTELLIGENCE_KEY]
$localSettings[$json.AISEARCH_KEY] = $appSettingsHash[$json.AISEARCH_KEY]

Write-Host $localSettings | ConvertTo-Json -Depth 10

$funcDirPath = "./DocumentQuestionsFunction"
if (Test-Path $funcDirPath) {
    # Save local settings to file
    $localSettings | ConvertTo-Json -Depth 10 | Out-File -FilePath "$funcDirPath/local.settings.json"
    Write-Host "Created $funcDirPath/local.settings.json for DocumentQuestionsConsole"
} else {
    Write-Warning "Directory $funcDirPath not found. Skipping local.settings.json creation."
}