# Pinterest Affiliate Automation

Sistema autónomo de generación de contenido para blog + Pinterest con Microsoft Agent Framework.

## Stack

- **Orquestación:** Azure Durable Functions (.NET 8)
- **Agente:** Microsoft Agent Framework con Claude Sonnet 4.5
- **Modelo:** Claude en Microsoft Foundry
- **Storage:** Azure Storage (default, más barato)

## Cómo Funciona el Agente

Microsoft Agent Framework usa un modelo simple:

```
AGENTE = Modelo (Claude) + Instrucciones + Tools
```

El agente NO es código complejo. Es simplemente:
1. Un LLM (Claude) que razona
2. Instrucciones de qué hacer
3. Tools (funciones C#) que puede llamar

```csharp
// Así de simple es crear un agente
var agent = new ChatCompletionAgent
{
    Name = "ContentCreator",
    Instructions = "Eres un creador de contenido de viajes...",
    Kernel = kernel  // Kernel tiene el modelo + tools registrados
};

// El agente decide qué tools usar basado en tu pedido
var response = await agent.InvokeAsync("Genera un artículo sobre playas en Portugal");
```

## Flujo Diario

```
06:00 AM - Timer dispara
    │
    ▼
[Durable Function Orchestrator]
    │
    ├─► GetTrendingKeywords()     // Tool: busca trends
    │
    ├─► Para cada keyword:
    │   │
    │   ├─► GenerateArticle()     // Tool: Claude genera contenido
    │   │   └── checkpoint ✓
    │   │
    │   ├─► GenerateImages()      // Tool: Bannerbear crea pins
    │   │   └── checkpoint ✓
    │   │
    │   ├─► PublishToWordPress()  // Tool: REST API
    │   │   └── checkpoint ✓
    │   │
    │   └─► CreatePinterestPins() // Tool: Pinterest API
    │       └── checkpoint ✓
    │
    └─► Fin (~15 min total)
```

## Estructura del Proyecto

```
/PinterestAffiliate
├── PinterestAffiliate.sln
├── /src
│   ├── Program.cs                 # Setup DI y Kernel
│   ├── /Functions
│   │   └── DailyOrchestrator.cs   # Durable Function
│   ├── /Tools
│   │   ├── ResearchTools.cs       # Buscar keywords
│   │   ├── ContentTools.cs        # Generar artículos
│   │   ├── ImageTools.cs          # Generar imágenes
│   │   ├── WordPressTools.cs      # Publicar blog
│   │   └── PinterestTools.cs      # Publicar pins
│   └── /Models
│       └── Models.cs              # DTOs
├── host.json
├── local.settings.json
└── appsettings.json
```

## Costo Estimado

| Servicio | Costo/mes |
|----------|-----------|
| Claude Sonnet 4.5 (~90 artículos) | ~$8 |
| Azure Functions (Flex Consumption) | ~$1 |
| Azure Storage | ~$1 |
| WordPress Hosting | ~$5 |
| **Total** | **~$15** |

## Documentos para Claude CLI

Usá estos archivos en orden:

1. `01-SETUP.md` - Crear proyecto y configurar
2. `02-TOOLS.md` - Implementar los 5 tools
3. `03-ORCHESTRATOR.md` - Durable Function
4. `04-DEPLOY.md` - Deploy a Azure

Cada documento está diseñado para pasarlo a Claude CLI:

```bash
claude "Lee 01-SETUP.md y crea el proyecto base"
claude "Lee 02-TOOLS.md e implementa ResearchTools.cs"
# etc...
```
