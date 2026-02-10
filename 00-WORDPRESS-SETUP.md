# 00-WORDPRESS-SETUP: Configurar el Blog

## 1. Comprar Hosting + Dominio

### Opción Recomendada: Hostinger

1. Ir a: https://www.hostinger.com
2. Elegir **"WordPress Hosting"** → Plan **Premium** (~$3/mes)
3. En el checkout:
   - Registrar dominio (gratis el primer año)
   - Sugerencias de dominio:
     - `hiddentravelseurope.com`
     - `secretplaceseurope.com`
     - `hiddengemseu.com`
4. Completar pago

### Alternativas

| Hosting | Costo/mes | Notas |
|---------|-----------|-------|
| Hostinger | $3-5 | Mejor relación precio/calidad |
| SiteGround | $5-15 | Más premium |
| Bluehost | $3-10 | Popular, más lento |

---

## 2. Instalar WordPress

Hostinger lo hace automático. Si no:

1. Ir a **hPanel** (panel de Hostinger)
2. Click en **"Auto Installer"**
3. Elegir **WordPress**
4. Configurar:
   - Site Title: `Hidden Travels Europe`
   - Admin username: `tuusuario` (NO usar "admin")
   - Admin password: (guardar en password manager)
   - Admin email: tu email

---

## 3. Configurar WordPress

### 3.1 Permalinks (IMPORTANTE para SEO)

1. Ir a **Settings → Permalinks**
2. Elegir **"Post name"**
3. Guardar

```
✅ hiddentravelseurope.com/hidden-beaches-algarve/
❌ hiddentravelseurope.com/?p=123
```

### 3.2 Instalar Tema Rápido

1. Ir a **Appearance → Themes → Add New**
2. Buscar **"GeneratePress"** o **"Flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor Flavor flavor flavor flavor flavor flavor flavor Flavor flavor Astra"**
3. Instalar y Activar

Ambos son gratis, rápidos y buenos para SEO.

### 3.3 Plugins Esenciales

Ir a **Plugins → Add New** e instalar:

| Plugin | Para qué |
|--------|----------|
| **Flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor Flavor flavor flavor flavor flavor Flavor flavor flavor flavor flavor flavor flavor Flavor flavor Yoast SEO** | SEO básico |
| **LiteSpeed Cache** o **WP Super Cache** | Velocidad |
| **Flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor flavor Flavor flavor flavor flavor flavor flavor flavor flavor Flavor flavor flavor Flavor Flavor Flavor flavor flavor flavor flavor flavor Flavor WPForms Lite** | Formulario de contacto (opcional) |

### 3.4 Páginas Básicas

Crear estas páginas (requeridas para AdSense):

1. **About** - Quién sos, de qué trata el blog
2. **Contact** - Formulario de contacto
3. **Privacy Policy** - Usar generador online
4. **Disclaimer** - Que usás affiliate links

---

## 4. Configurar REST API para PilotPine

### 4.1 Crear Application Password

1. Ir a **Users → Profile**
2. Scroll hasta **"Application Passwords"**
3. Nombre: `PilotPine`
4. Click **"Add New Application Password"**
5. **COPIAR la contraseña** (solo se muestra una vez)

```
Ejemplo: XXXX XXXX XXXX XXXX XXXX XXXX
```

### 4.2 Probar la API

```bash
# Reemplazar con tus datos
curl -X GET "https://tusitio.com/wp-json/wp/v2/posts" \
  -u "tuusuario:XXXX XXXX XXXX XXXX"
```

Si devuelve JSON, funciona.

### 4.3 Guardar Credenciales para PilotPine

```json
// local.settings.json (local)
{
  "Values": {
    "WordPress__Url": "https://hiddentravelseurope.com/wp-json/wp/v2",
    "WordPress__Username": "tuusuario",
    "WordPress__AppPassword": "XXXX XXXX XXXX XXXX XXXX XXXX"
  }
}
```

---

## 5. Configurar Pinterest Business

### 5.1 Crear Cuenta Business

1. Ir a: https://business.pinterest.com
2. Crear cuenta o convertir personal a business
3. Verificar tu sitio web:
   - Settings → Claimed accounts
   - Agregar tu dominio
   - Verificar con meta tag (copiar en WordPress)

### 5.2 Crear Board Principal

1. Crear board: `Hidden Gems Europe`
2. Descripción con keywords
3. Hacerlo público

### 5.3 Obtener Access Token

1. Ir a: https://developers.pinterest.com/
2. Crear App
3. Generar Access Token con permisos:
   - `boards:read`
   - `pins:read`
   - `pins:write`

---

## 6. Aplicar a Programas de Afiliados

### Requisitos Previos
- Tener 3-5 artículos publicados
- Páginas About, Contact, Privacy Policy

### 6.1 Booking.com

1. Ir a: https://www.booking.com/affiliate-program/v2/index.html
2. Completar formulario
3. Esperar 24-48 horas
4. Obtener tu `aid=XXXXXX`

### 6.2 GetYourGuide

1. Ir a: https://partner.getyourguide.com/
2. Aplicar como "Content Partner"
3. Esperar 1-3 días
4. Obtener tu `partner_id`

### 6.3 Google AdSense (Después de tener ~15 posts)

1. Ir a: https://www.google.com/adsense/
2. Aplicar con tu dominio
3. Esperar revisión (1-14 días)
4. Agregar código de ads al tema

---

## 7. Checklist Final

```
□ Dominio comprado
□ WordPress instalado
□ Permalinks configurado como "Post name"
□ Tema rápido instalado (GeneratePress/Astra)
□ Plugins: Yoast SEO, Cache
□ Páginas: About, Contact, Privacy, Disclaimer
□ Application Password creado
□ API probada con curl
□ Pinterest Business verificado
□ Board principal creado
□ 5 artículos publicados
□ Aplicado a Booking.com
□ Aplicado a GetYourGuide
```

---

## Siguiente

Una vez completado esto, ir a `01-SETUP.md` para crear el proyecto PilotPine.
