# Semantic Kernel and Azure OpenAI: Ask Questions on your document


## Overview

This solution provides an example of how to process your own documents and then use [Azure OpenAI](https://azure.microsoft.com/en-us/products/ai-services/openai-service) and [Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) to ask question specific to that document.

**NOTE**: In addition to the Azure Function deployment below, a console app is also provided to demonstrate how to use the OpenAI SDK to ask questions about the document using the same deployed AI services.

![ Architecture Diagram ](images/Architecture.png)

## Updates

**January 2025:**

- Updated SDK to Azure.AI.DocumentIntelligence to access the latest API versions and models
- Added ability to target additional prebuilt models on the console app with the `process --file "<file name> --model <model name> command` To see the list of available models run `process -h`
  - To add addition prebuilt models of interest, add the model name to the list found at the top of [DocumentQuestionsLibrary/DocumentIntelligence.cs](DocumentQuestionsLibrary/DocumentIntelligence.cs). The list of models is maintained [here](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/model-overview?view=doc-intel-4.0.0)
- Added new command `clear-index` which will delete process document indexes by name or all of the indexes using the `all` keyword
- Added sample telemetry with [DocumentQuestionsLibrary/SkFunctionInvocationFilter.cs](DocumentQuestionsLibrary/SkFunctionInvocationFilter.cs) demonstrating how to intercept Semantic Kernel function invocation before and after execution of the function
- Added OpenTelemetry configuration to the Console app and the Function if the `APPLICATIONINSIGHTS_CONNECTION_STRING` app setting is provided


## What's Included

 This solution consists of:

 - C# function app which has 3 functions:

     1. `HttpTriggerUploadFile` - upload documents to an Azure Storage account via a REST Api
     2. `BlobTriggerProcessFile` - detects the uploaded document and processes it through [Azure Cognitive Services Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview?view=doc-intel-3.1.0) into one or more JSON files (depending on the size of the document)
     3. `HttpTriggerSemanticKernelAskQuestion` - REST Api to ask questions about the document using Semantic Kernel SDK

- A console app to easily run and test locally

## Getting Started

### Prerequisites

- The deployment script can create a new Azure OpenAI Service for you however if you want to reuse an existing one, it will need to be in the same subscription where you are going to deploy your solution and retrieve its `Endpoint` and a `Key`.
- The PowerShell deployment script defaults to `gpt-4o` and `text-embedding-ada-002` models with a deployment name matching the model name. If you have something different in your Azure OpenAI instance, you will want to pass in those values to the PowerShell command line deployed to your Azure OpenAI instance each with a deployment name matching the model name. Be aware, that using a different GPT model may result in max token violations with the example below.

### Deploying

Deployment is automated using PowerShell, the [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/) and [Bicep](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/).\
To run the script, you will need to select an Azure location for deployment, the Azure Open AI endpoint and key and pick a name for the function (this must be a globally unique name and less than 10 characters).

By default, the script will deploy an [Azure Cognitive Search](https://azure.microsoft.com/en-us/services/search/) instance and use it to store the results of the document processing and searching. If you do not want to deploy Azure Cognitive Search, you can use the `-useCognitiveSeach $false` parameter option to skip the deployment of Azure Cognitive Search and only use the `extracted` blob container to store the results of the document processing.

**NOTE:** If deploying a new Azure OpenAI instance, be aware there are location limitations base on model. Please set your `openAiLocation` value accordingly: 
[Region Availability](https://learn.microsoft.com/en-us/azure/ai-services/openai/concepts/models?tabs=global-standard%2Cstandard-chat-completions#model-summary-table-and-region-availability)

Also, depending on your availble Azure OpenAI model quota, you may get a capacity related deployment error. If you do, you will need to modify the `capacity` value for the appropriate model found in the [`infra/azureopenai.bicep`](infra/azureopenai.bicep) file


``` powershell
# obtain an Azure access token
az login

# deploy the solution - creating a new Azure OpenAI Service (note the separate item for OpenAI Location as there are some restrictions for this service)
.\deploy.ps1 -functionAppName  <function name>  -location <azure location> -openAILocation <openai key>

# OR.. reusing an existing Azure Open AI Service
.\deploy.ps1 -functionAppName  <function name>  -openAiEndpoint <http endpoint value> -openAiKey <openai key> -location <azure location>
```

If successful, this process will create:

- Storage account with two blob containers (`raw` for uploaded documents and `extracted` for processed output)
- Application Insights instance
- Function app with 3 functions with system assigned managed identity
  - Role assigment for the function identity to access blob storage and call Azure OpenAI
- Azure Cognitive Services account with system assigned managed identity
  - Role assigment for Cognitive Services identity for read access to `raw` container and write access to `extracted` container
- Azure Cogitive Search account
- *In addition, it will configure, compile and start the demo console app.*
  

### Running Samples via Azure Functions

1. Upload a document using the `HttpTriggerUploadFile` REST API. 
For this example, download and use [US Declaration of Independence as a PDF file](https://uscode.house.gov/download/annualhistoricalarchives/pdf/OrganicLaws2006/decind.pdf)
2. Once the file i uploaded, the `BlobTriggerProcessFile` will automatically trigger, process it with Document Intelligence and create a new folder called `decind` in the `extracted` blob container and save 3 JSON files.

3. Ask questions using the `HttpTriggerSemanticKernelAskQuestion` function - this uses semantic config to only load max of 2 pages to reduce tokens provided to Azure OpenAI.

   Question:

      Return:

      ``` text
      The document was signed by fifty-six signers.
      ```

   Question:

   ``` json
      {
      "filename": "decind.pdf",
      "question": "summarize this document in two bulleted sentences"
      }
   ```

   Return:

   ``` text
   - The Declaration of Independence was unanimously agreed upon by thirteen united states of America on July 4, 1776, to express their decision to dissolve their political connection with Great Britain and become independent due to numerous abuses and usurpations by the king.
   - The fundamental principles of their new government would be based on the belief that all men are created equal with certain unalienable rights including life, liberty, and the pursuit of happiness, and if any government becomes destructive of these ends, it is the right of the people to alter or abolish it, and to institute a new government.
   ```

### Running Samples via Console App

If you used the `deploy.ps1` script, the console app will be compiled and started automatically. Otherwise, you can run the console app by opening the `DocumentQuestionsFunction.sln` in Visual Studio or VS Code and running the `DocumentQuestionsConsole` project.

1. Open the console app. If this is your first time running the app or the Functions, you will not have any documents processed and you will be prompted to upload a document with the `process` 

   ![first time running the app](images/first-run.png)

2. Upload a document using the `process` command

   ![file processing](images/file-processing.png)

3. Set the current document using the `doc` command

   ![active document](images/active-document.png)

4. Start ask questions!

   ![question](images/question1.png)

   ![question](images/question2.png)

### Other Console App Features

- `list` - list all the documents processed

   ![list command](images/list.png)

- `clear-index` - clear index by name or `all`

   ![clear-index](images/clear-index.png)

- `ai list` - list the Azure OpenAI models configured for the app

   ![ai list command](images/ai-list.png)

- `ai set` - set the Azure OpenAI model to use for asking questions (these must already be deployed in your Azure AI instance)

   ![ai set command](images/ai-set.png)

### What's next?

Try uploading your own documents and start asking question
