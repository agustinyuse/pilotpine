# Tools vs Llamadas Directas: Análisis para PilotPine

## Contexto

En Semantic Kernel / Microsoft Agent Framework hay dos formas de interactuar con el LLM:

1. **Tools (Function Calling)**: El agente decide autónomamente qué funciones llamar
2. **Llamadas directas**: Tu código controla explícitamente qué se ejecuta y cuándo

## Qué es cada approach

### Approach A: Tools (Function Calling)

```csharp
// Registras funciones como "tools" del agente
kernel.Plugins.AddFromObject(researchTools, "Research");
kernel.Plugins.AddFromObject(wordPressTools, "WordPress");

var agent = new ChatCompletionAgent
{
    Name = "ContentWriter",
    Instructions = "Investigá keywords, generá un artículo y publicalo en WordPress",
    Kernel = kernel  // El agente ve todos los tools registrados
};

// El agente DECIDE qué tools llamar, en qué orden, con qué parámetros
await agent.InvokeAsync("Generá contenido sobre playas de Portugal");
// Claude podría: 1) GetKeywords() → 2) CreateArticle() → 3) PublishPost()
// O podría decidir un orden diferente según su razonamiento
```

### Approach B: Llamadas Directas (Orquestación explícita)

```csharp
// Tu código controla el flujo exacto
var keywords = await researchTools.GetKeywords(3);       // Paso 1
var article = await GenerateWithLLM(keywords[0]);        // Paso 2: llamada directa al LLM
var result = await wordPressTools.PublishPost(article);   // Paso 3
```

---

## Comparación

| Aspecto | Tools (Function Calling) | Llamadas Directas |
|---------|--------------------------|-------------------|
| **Control de flujo** | El LLM decide el orden | Tu código decide el orden |
| **Predictibilidad** | Baja: el LLM puede tomar caminos inesperados | Alta: siempre hace lo mismo |
| **Costo de tokens** | Mayor: cada tool call consume tokens extra (schema, decisión) | Menor: solo pagas por el contenido |
| **Debugging** | Difícil: hay que inspeccionar qué decidió el LLM | Fácil: stack trace normal |
| **Flexibilidad** | Alta: el agente se adapta a situaciones nuevas | Baja: solo hace lo que programaste |
| **Error handling** | Complejo: el LLM puede reintentar por su cuenta o no | Simple: try/catch estándar |
| **Checkpointing** | Difícil: ¿dónde poner el checkpoint si el LLM decide? | Fácil: checkpoint entre cada paso |
| **Latencia** | Mayor: ida y vuelta LLM por cada decisión de tool | Menor: solo las llamadas necesarias |
| **Costo por ejecución** | ~$0.15-0.20 (tool decisions + content) | ~$0.06-0.08 (solo content) |

---

## Análisis para PilotPine

### Dónde CONVIENEN Tools

**Generación de contenido**: El LLM es genuinamente mejor decidiendo:
- Qué estructura darle al artículo
- Dónde poner affiliate links naturalmente
- Qué copy usar para pins
- Cómo adaptar el tono según el keyword

```csharp
// BUENO: El LLM decide cómo estructurar el artículo
var agent = new ChatCompletionAgent
{
    Instructions = """
        Generá un artículo de viajes. Tenés disponible:
        - CreateArticleStructure: para guardar el artículo
        - GeneratePinHeadlines: para crear variaciones de títulos de pins
        Decidí la mejor estructura según el keyword.
        """,
    Kernel = kernelWithContentTools
};
```

**Research con múltiples fuentes**: Si en el futuro integrás Pinterest Trends + Google Trends + competencia, el LLM puede decidir qué fuentes consultar.

### Dónde CONVIENEN Llamadas Directas

**Publicación en WordPress y Pinterest**: Son operaciones mecánicas. No necesitás que el LLM "decida" publicar - siempre vas a publicar.

```csharp
// BUENO: Control explícito, sin gastar tokens en decisiones obvias
var wpResult = await wordPressTools.PublishPost(article);
if (wpResult.Success)
    await pinterestTools.CreatePin(article.Title, wpResult.PostUrl);
```

**Orquestación entre pasos**: Durable Functions ya maneja el flujo. Meter un agente que decida "¿llamo a WordPress o a Pinterest primero?" es gastar tokens en algo que tu código ya sabe.

**Estado y persistencia**: Guardar/leer estado es mecánico. No necesitás un LLM para decidir si guardar.

---

## Recomendación: Approach Híbrido

```
┌─────────────────────────────────────────────────────┐
│           DURABLE FUNCTION ORCHESTRATOR              │
│           (Llamadas directas - tu código controla)   │
│                                                      │
│  1. GetKeywords()          ← Directo (mecánico)      │
│     │                                                │
│  2. GenerateArticle()      ← AGENT + TOOLS           │
│     │  ┌─────────────────────────────────────────┐   │
│     │  │ Claude con tools:                       │   │
│     │  │  - CreateArticleStructure               │   │
│     │  │  - GeneratePinHeadlines                 │   │
│     │  │  - InsertAffiliateLinks                 │   │
│     │  │ (El LLM decide cómo usar estas tools)   │   │
│     │  └─────────────────────────────────────────┘   │
│     │                                                │
│  3. PublishToWordPress()   ← Directo (mecánico)      │
│     │                                                │
│  4. CreatePinterestPins()  ← Directo (mecánico)      │
│     │                                                │
│  5. SaveResults()          ← Directo (mecánico)      │
└─────────────────────────────────────────────────────┘
```

### ¿Por qué este approach?

1. **Checkpointing limpio**: Durable Functions controla el flujo. Cada paso es un checkpoint claro.
   Si falla en el paso 3, no repetís el paso 2 (que costó $0.06 de Claude).

2. **Costos predecibles**: Solo usás tokens del LLM donde genuinamente agrega valor (contenido).
   Las publicaciones son llamadas HTTP directas sin pasar por el LLM.

3. **Debugging simple**: Si WordPress falla, sabés que fue en el paso 3. No tenés que
   parsear los logs del agente para entender qué tool intentó llamar.

4. **Flexibilidad donde importa**: El agente SÍ tiene libertad para decidir cómo
   estructurar el artículo, qué affiliate links usar, qué tono usar.

### Cálculo de costos: Tools everywhere vs Híbrido

**Todo con Tools (~90 artículos/mes):**
- Research decision: ~500 tokens × 90 = 45K tokens extra
- Publish decision: ~300 tokens × 90 = 27K tokens extra
- Pinterest decision: ~300 tokens × 90 = 27K tokens extra
- Tool schemas: ~1000 tokens × 90 = 90K tokens extra
- **Extra: ~189K tokens/mes ≈ $2-3/mes adicionales**
- Total: ~$13-14/mes en LLM

**Híbrido (solo contenido con tools):**
- Solo generación de contenido pasa por el LLM
- Operaciones mecánicas son llamadas directas
- **Total: ~$8-10/mes en LLM**

**Ahorro: ~$3-4/mes (30% menos)**

---

## Implementación en código

### Paso de generación (con Tools - el LLM decide):

```csharp
[Function(nameof(GenerateArticleActivity))]
public async Task<Article> GenerateArticleActivity([ActivityTrigger] ArticleInput input)
{
    // Crear un kernel CON los content tools registrados
    var kernel = _foundryProvider.CreateKernelForModel("claude-sonnet-4-5");
    kernel.Plugins.AddFromObject(_contentTools, "Content");

    var agent = new ChatCompletionAgent
    {
        Name = "ContentWriter",
        Instructions = """
            You are a travel content writer.
            Use the available tools to structure and save the article.
            Include affiliate link placeholders where natural.
            """,
        Kernel = kernel
    };

    var chat = new ChatHistory();
    chat.AddUserMessage($"Write a {input.ArticleType} about: {input.Keyword}");

    Article? result = null;
    await foreach (var msg in agent.InvokeAsync(chat))
    {
        // El agente llamará a CreateArticleStructure automáticamente
        // y el resultado estará en el tool output
    }

    return result ?? throw new Exception("Agent did not produce an article");
}
```

### Paso de publicación (llamada directa - sin LLM):

```csharp
[Function(nameof(PublishToWordPressActivity))]
public async Task<PublishResult> PublishToWordPressActivity([ActivityTrigger] Article article)
{
    // Llamada directa: no necesitamos que un LLM decida publicar
    return await _wordPressTools.PublishPost(
        article.Title,
        article.Content,
        article.MetaDescription,
        string.Join(",", article.Tags)
    );
}
```

---

## Cuándo reconsiderar

Podría convenir mover más cosas a Tools si:

- **El agente necesita decidir SI publicar o no** (ej: review de calidad antes de publicar)
- **Hay múltiples canales** y el agente decide en cuáles publicar según el contenido
- **El research se vuelve complejo** y el LLM necesita combinar múltiples fuentes inteligentemente
- **Querés un "editor" que revise** el contenido antes de publicar (multi-agent)

Por ahora, el approach híbrido es el más eficiente para el caso de uso actual.
