# 01-SETUP: Crear Proyecto Base

## Objetivo
Crear el proyecto .NET 8 con Azure Functions y Microsoft Agent Framework configurado para usar Claude en Foundry.

## Crear Proyecto

```bash
# Crear solución
dotnet new sln -n PinterestAffiliate

# Crear Function App
dotnet new func -n PinterestAffiliate -o src --worker-runtime dotnet-isolated

# Agregar a solución
dotnet sln add src/PinterestAffiliate.csproj
```

## Paquetes Necesarios

Agregar al `.csproj`:

```xml
<ItemGroup>
  <!-- Azure Functions -->
  <PackageReference Include="Microsoft.Azure.Functions.Worker" Version="2.0.0" />
  <PackageReference Include="Microsoft.Azure.Functions.Worker.Sdk" Version="2.0.0" />
  <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.Timer" Version="4.3.0" />
  <PackageReference Include="Microsoft.Azure.Functions.Worker.Extensions.DurableTask" Version="1.1.0" />
  
  <!-- Microsoft Agent Framework / Semantic Kernel -->
  <PackageReference Include="Microsoft.SemanticKernel" Version="1.40.0" />
  <PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.40.0" />
  <PackageReference Include="Azure.AI.Inference" Version="1.0.0" />
  <PackageReference Include="Azure.Identity" Version="1.13.0" />
  
  <!-- HTTP y utilidades -->
  <PackageReference Include="WordPressPCL" Version="2.1.0" />
  
  <!-- Application Insights -->
  <PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.22.0" />
</ItemGroup>
```

## Program.cs

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Azure.Identity;
using PinterestAffiliate.Tools;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // ═══════════════════════════════════════════════════════
        // CONFIGURAR SEMANTIC KERNEL CON CLAUDE EN FOUNDRY
        // ═══════════════════════════════════════════════════════
        
        services.AddKernel();
        
        services.AddAzureAIInferenceChatCompletion(
            endpoint: config["Foundry:Endpoint"],
            credential: new DefaultAzureCredential(),
            modelId: "claude-sonnet-4-5"
        );

        // ═══════════════════════════════════════════════════════
        // REGISTRAR TOOLS
        // ═══════════════════════════════════════════════════════
        
        services.AddSingleton<ResearchTools>();
        services.AddSingleton<ContentTools>();
        services.AddSingleton<ImageTools>();
        services.AddSingleton<WordPressTools>();
        services.AddSingleton<PinterestTools>();

        // HttpClient para APIs
        services.AddHttpClient();

        // Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
    })
    .Build();

await host.RunAsync();
```

## host.json

```json
{
  "version": "2.0",
  "logging": {
    "applicationInsights": {
      "samplingSettings": {
        "isEnabled": true
      }
    }
  },
  "extensions": {
    "durableTask": {
      "maxConcurrentActivityFunctions": 3,
      "maxConcurrentOrchestratorFunctions": 2
    }
  }
}
```

## local.settings.json

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    "Foundry__Endpoint": "https://TU-RESOURCE.services.ai.azure.com",
    
    "WordPress__Url": "https://tu-blog.com/wp-json/wp/v2",
    "WordPress__Username": "tu-usuario",
    "WordPress__AppPassword": "xxxx-xxxx-xxxx-xxxx",
    
    "Pinterest__AccessToken": "tu-token",
    
    "Bannerbear__ApiKey": "tu-api-key"
  }
}
```

## Estructura de Carpetas

Crear:
```
src/
├── Functions/
├── Tools/
└── Models/
```

## Verificar Setup

```bash
cd src
dotnet build
```

## Siguiente
Una vez que compila, ir a `02-TOOLS.md` para implementar los tools.
