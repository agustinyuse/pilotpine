# 04-DEPLOY: Deployar a Azure

## Recursos Necesarios

```bash
# Variables
RG="rg-pinterest-affiliate"
LOCATION="eastus"
STORAGE="stpinterestaffiliate"
FUNC="func-pinterest-affiliate"
FOUNDRY="ai-pinterest"
```

## 1. Crear Resource Group

```bash
az group create --name $RG --location $LOCATION
```

## 2. Crear Storage Account

```bash
az storage account create \
  --name $STORAGE \
  --resource-group $RG \
  --location $LOCATION \
  --sku Standard_LRS
```

## 3. Crear Azure AI Foundry (para Claude)

```bash
# Crear recurso de AI Services
az cognitiveservices account create \
  --name $FOUNDRY \
  --resource-group $RG \
  --kind AIServices \
  --sku S0 \
  --location $LOCATION

# Obtener endpoint
az cognitiveservices account show \
  --name $FOUNDRY \
  --resource-group $RG \
  --query properties.endpoint
```

> **Nota:** Después de crear, ir al portal y deployar Claude Sonnet 4.5 desde el Model Catalog.

## 4. Crear Function App (Flex Consumption)

```bash
az functionapp create \
  --name $FUNC \
  --resource-group $RG \
  --storage-account $STORAGE \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --flexconsumption-location $LOCATION
```

## 5. Configurar App Settings

```bash
az functionapp config appsettings set \
  --name $FUNC \
  --resource-group $RG \
  --settings \
    "Foundry__Endpoint=https://$FOUNDRY.cognitiveservices.azure.com" \
    "WordPress__Url=https://tu-blog.com/wp-json/wp/v2" \
    "WordPress__Username=tu-usuario" \
    "WordPress__AppPassword=xxxx-xxxx-xxxx" \
    "Pinterest__AccessToken=tu-token" \
    "Pinterest__DefaultBoardId=tu-board-id"
```

## 6. Habilitar Managed Identity

```bash
# Habilitar
az functionapp identity assign --name $FUNC --resource-group $RG

# Obtener principal ID
PRINCIPAL=$(az functionapp identity show --name $FUNC --resource-group $RG --query principalId -o tsv)

# Dar acceso a AI Services
az role assignment create \
  --assignee $PRINCIPAL \
  --role "Cognitive Services User" \
  --scope "/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$RG/providers/Microsoft.CognitiveServices/accounts/$FOUNDRY"
```

## 7. Deploy

```bash
cd src
func azure functionapp publish $FUNC
```

## 8. Verificar

```bash
# Ver logs
az functionapp log deployment show --name $FUNC --resource-group $RG

# Ver ejecuciones de Durable Functions
# Ir a Azure Portal > Function App > Functions > Monitor
```

## Costos Esperados

| Recurso | Costo/mes |
|---------|-----------|
| Function App (Flex) | ~$1 |
| Storage Account | ~$1 |
| AI Foundry (Claude) | ~$8 (por uso) |
| **Total Azure** | **~$10** |

+ WordPress hosting aparte (~$5)
