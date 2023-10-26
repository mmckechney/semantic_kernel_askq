# semantic_kernel_askq
c# function app - upload docs with rest api, and another functino to process data, and another 2 to ask qs

Permissions:
1. To leverage Azure OpenAI - this code base not using the Azure OpenAI Key - but rather IAM.  **Enable Managed Identity on the function App** and provide it with Cognitive Services OpenAI User Access.

2. 



Create an Azure Function: c#, 6 Isolated LTS 

We will have the following functions in our Function App:

1. BlobTriggerProcessFile
  - Takes data from raw container, processes it, into a json file.
  - ```
    {"FileName":"Test2_output.pdf","blobName":"Test2_output/Test2_output_1.json","Content":"This document has been updated. Here is new content for the document.Here are the reasons that Megan is cool:"}
    ```

2. httpTriggerAskAboutADoc
   -Creates a post
   ```
   {
     "filename": "Test2_output.pdf",
     "question": "who is cool?"
   }
   ```

4. httpTriggerSemanticConfigAskQuestion - uses semantic config to only load max of 2 pages to reduce tokens provided to Azure OpenAI.
   ```
   {"filename": "Test2_output.pdf", "question": "who is cool?" }
   ```

6. HttpTriggerUploadFile
   Post API to upload information
   
