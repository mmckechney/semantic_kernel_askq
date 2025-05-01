#!/usr/bin/env pwsh

# Get the current user's object ID
$currentUserObjectId = az ad signed-in-user show -o tsv --query id
if (-not $currentUserObjectId) {
    Write-Error "Failed to get current user object ID. Make sure you're logged into Azure CLI."
    exit 1
}

$envValues = azd env get-values --output json | ConvertFrom-Json
$AZURE_LOCATION = $envValues.AZURE_LOCATION
$envName = $envValues.AZURE_ENV_NAME
$safeEnvName = $envName -replace '[^a-zA-Z0-9]', ''

# Path to .env file
$envFilePath = Join-Path (Split-Path $PSScriptRoot -Parent) ".env"
$envContent = @()

# Function to set environment variables in both azd and .env file
function Set-EnvironmentVariable {
    param (
        [string]$Name,
        [string]$Value
    )
    
    # Set in azd environment
    Write-Host "Setting $Name to $Value"
    azd env set $Name $Value
    
    # Add to .env content
    $envContent += "$Name=$Value"
}

# Set the user object ID as an environment variable for the deployment
Set-EnvironmentVariable -Name "AZURE_CURRENT_USER_OBJECT_ID" -Value $currentUserObjectId
Set-EnvironmentVariable -Name "AZURE_RESOURCEGROUP_NAME" -Value "$envName-rg"
Set-EnvironmentVariable -Name "AZURE_FUNCTIONAPP_NAME" -Value "$envName-func"
Set-EnvironmentVariable -Name "AZURE_KEYVAULT_NAME" -Value "$envName-keyvault"
Set-EnvironmentVariable -Name "AZURE_STORAGEACCT_NAME" -Value "$($safeEnvName)storage"
Set-EnvironmentVariable -Name "AZURE_DOCUMENTINTELLIGENCE_ACCOUNT_NAME" -Value "$envName-aidoc"
Set-EnvironmentVariable -Name "AZURE_AISEARCH_NAME" -Value "$envName-aisearch"
Set-EnvironmentVariable -Name "AZURE_OPENAI_LOCATION" -Value $AZURE_LOCATION
Set-EnvironmentVariable -Name "AZURE_OPENAI_SERVICE_NAME" -Value "$envName-openai"

# Write all environment variables to .env file
Write-Host "Writing environment variables to .env file at $envFilePath"
$envContent | Out-File -FilePath $envFilePath -Encoding utf8 -Force
Write-Host ".env file created/updated successfully."

