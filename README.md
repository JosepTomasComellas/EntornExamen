# EntornExamen · v3.3.0

Sistema de control de presència en temps real durant exàmens sobre xarxa WiFi aïllada.
Branding, colors, logo i xarxa DHCP configurables des del `.env`.

---

## Com funciona

```
[Alumnes] → WiFi "Entorn_Examen" (192.168.100.0/24, sense internet)
                    │
                    ▼
     [VM Ubuntu 24.04 sobre Proxmox]
          ├── isc-dhcp-server  → /var/lib/dhcp/dhcpd.leases
          ├── BIND9 (DNS)      → /var/log/named/queries.log
          └── Docker Compose
                ├── entornexamen-nginx   (proxy SSL)
                ├── entornexamen-api     (ASP.NET Core 10)
                ├── entornexamen-web     (Blazor Server + MudBlazor)
                ├── entornexamen-db      (SQL Server 2022 Express)
                └── entornexamen-redis   (Redis 7 Alpine)
```

L'alumne accedeix a **`https://<ip-servidor>:4445/examen`** i introdueix el seu correu corporatiu.
El backend detecta la seva IP real (nginx en mode host, sense NAT Docker) i la vincula al registre DHCP.
A partir d'aquí, fa check-in cada 30 s i el professor veu l'estat de tota la classe en temps real.

**Els alumnes no necessiten contrasenya.**

---

## Funcionalitats

### Professor (`/professor/examen`)
- Inicia / tanca / reobre sessions d'examen per classe
- Selecció de recursos (icones + enllaços) que es mostren als alumnes durant la sessió
- Monitoratge en temps real: estat de cada alumne (connectat / sense check-in / desconnectat / expulsat)
- Alertes de desconnexió amb so (Web Audio API, sense fitxers externs)
- Alertes de peticions DNS externes sospitoses
- Missatges push: apareixen com a diàleg obligatori a la pantalla de l'alumne
- Exportació CSV de la sessió
- Mode presentació (pantalla completa)

### Alumne (`/examen`)
- Identificació per correu corporatiu (sense contrasenya). Si s'introdueix l'usuari sense domini, s'auto-completa amb el domini configurat (`EXAMEN_DOMINI_EMAIL`, per defecte `sarria.salesians.cat`)
- Check-in automàtic cada 30 s
- Rebuda de missatges del professor (diàleg emergent obligatori)
- Mostra IP real i ID de sessió a la pantalla de seguiment
- Accés als recursos habilitats pel professor (icones d'enllaços, visibles per sessió)

### Admin
- Gestió de professors, classes i alumnes
- Importació d'alumnes des d'EPSS (XLS/HTML auto-detectat)
- Importació de fotos (ZIP amb `{DNI_numèric}.jpg`)
- Gestió de recursos d'examen (icones + enllaços configurables globalment)
- Còpies de seguretat JSON i ZIP complet (JSON + fotos + recursos) amb export/import
- Còpies automàtiques al servidor en format ZIP
- Estadístiques d'accessos de professors
- Gestió de dispositius registrats (`/admin/examen-macs`)

---

## Instal·lació des de zero

### Requisits del servidor

- VM Ubuntu 24.04 (recomanat sobre Proxmox)
- Docker + Docker Compose v2
- `isc-dhcp-server` (xarxa d'examen 192.168.100.0/24)
- `bind9` (DNS local)
- Interfície de xarxa dedicada per a l'Entorn Examen

### 1. Clonar el repositori

```bash
git clone https://github.com/JosepTomasComellas/EntornExamen.git /docker/EntornExamen
cd /docker/EntornExamen
```

### 2. Configurar les variables d'entorn

```bash
cp .env.example .env
nano .env
```

| Variable | Obligatori | Descripció |
|----------|:----------:|-----------|
| `MSSQL_SA_PASSWORD` | ✓ | Contrasenya SQL Server (mínim 8 car., majúscules + números) |
| `JWT_SECRET` | ✓ | Secret JWT (mínim 32 caràcters) |
| `ADMIN_EMAIL` | ✓ | Correu de l'administrador inicial |
| `ADMIN_PASSWORD` | ✓ | Contrasenya de l'administrador |
| `ADMIN_NOM` | ✓ | Nom de l'administrador |
| `ADMIN_COGNOMS` | | Cognoms de l'administrador |
| `TZ` | | Zona horària dels contenidors (per defecte: `Europe/Madrid`) |
| `LOG_LEVEL` | | Nivell de log framework (per defecte: `Warning`). Els logs propis sempre en `Information`. |
| `EXAMEN_DOMINI_EMAIL` | | Domini de correu acceptat dels alumnes |
| `EXAMEN_CHECKIN_INTERVAL_SECONDS` | | Interval check-in en segons (per defecte: `30`) |
| `EXAMEN_SENSE_CHECKIN_FACTOR` | | Factor ×interval per passar a "Sense check-in" (per defecte: `2`) |
| `EXAMEN_DESCONNECTAT_FACTOR` | | Factor ×interval per marcar com a desconnectat (per defecte: `4`) |
| `EXAMEN_MODE_PRO` | | `true` permet IPs duplicades (per a proves locals, per defecte: `false`) |
| `EXAMEN_DHCP_NETWORK_PREFIX` | | Prefix de la xarxa DHCP dels alumnes (ex: `192.168.100.`) |
| `SMTP_HOST` | | Servidor SMTP (opcional, per a correu de professors) |
| `BRAND_NOM` | | Nom de l'aplicació (per defecte: `Entorn d'Examens`) |
| `BRAND_SHORT_NOM` | | Nom curt per al manifest PWA (per defecte: `Examens`) |
| `BRAND_ORGANITZACIO` | | Text de l'organització al peu de pàgina |
| `BRAND_COLOR_PRIMARY` | | Color principal en hex (per defecte: `#CC0000`) |
| `BRAND_COLOR_APPBAR` | | Color de la barra superior en hex (per defecte: `#1e293b`) |
| `BRAND_LOGO_URL` | | URL del logo (per defecte: `/images/logo2.png`) |
| `BRAND_BG_IMAGE_URL` | | URL de la imatge de fons (buit = sense fons) |

### 3. Configurar el servidor DHCP

```bash
sudo cp scripts/examen/dhcpd.conf /etc/dhcp/dhcpd.conf
sudo cp scripts/examen/dhcp-hook.sh /etc/dhcp/dhcpd-enter-hooks.d/notifica-api
sudo chmod +x /etc/dhcp/dhcpd-enter-hooks.d/notifica-api
sudo systemctl restart isc-dhcp-server
```

### 4. Configurar BIND9

```bash
sudo cat scripts/examen/named.conf.local >> /etc/bind/named.conf.local
sudo cp scripts/examen/db.examen.local /etc/bind/db.examen.local
sudo mkdir -p /var/log/named
sudo chown bind:bind /var/log/named
sudo systemctl restart bind9
```

### 5. Construir i arrencar

```bash
docker compose up --build -d
```

---

## Neteja completa i reinici

Útil quan cal partir d'un estat completament net (canvi de versió major, base de dades corrupta, etc.).

> ⚠️ **Esborrarà totes les dades**: classes, alumnes, sessions, connexions.

```bash
cd /docker/EntornExamen

# 1. Atura i elimina tots els contenidors
docker compose down

# 2. Elimina els volums (BD, Redis, claus DataProtection, còpies de seguretat)
docker volume rm \
  entornexamen_db-data \
  entornexamen_redis-data \
  entornexamen_dp-keys \
  entornexamen_api-backups \
  entornexamen_fotos-alumnes \
  entornexamen_nginx-logs

# 3. Reconstrueix les imatges des de zero
docker compose build --no-cache

# 4. Arrenca
docker compose up -d

# 5. Comprova que tot ha arrencat
docker compose ps
docker compose logs -f
```

En arrencar per primera vegada, la BD `EntornExamen` es crea automàticament i l'admin es configura amb les credencials del `.env`.

---

## Actualització normal (sense esborrar dades)

```bash
bash /docker/EntornExamen/deploy/server-update.sh
```

O manualment:

```bash
cd /docker/EntornExamen
git pull
docker compose up --build -d
```

---

## SSL

- **Sense certificat:** nginx genera automàticament un certificat auto-signat vàlid 10 anys.
- **Amb certificat propi:** col·loca `server.crt` i `server.key` a `nginx/ssl/` abans d'arrencar.

---

## Estructura del projecte

```
EntornExamen/
├── api/                          # ASP.NET Core 10 Minimal API
│   ├── Data/
│   │   ├── AppDbContext.cs       # EF Core (EnsureCreated — sense migracions)
│   │   ├── SeedData.cs           # Admin inicial des de .env
│   │   └── Models/
│   │       ├── Professor.cs
│   │       ├── Class.cs
│   │       ├── Student.cs        # PasswordHash nullable (alumnes sense contrasenya)
│   │       ├── AlumneMac.cs      # MAC ↔ Alumne
│   │       ├── SessioExamen.cs   # Sessió d'examen per classe
│   │       ├── RegistreConnexio.cs  # Connexió per IP/MAC
│   │       └── PeticioTdns.cs   # Peticions DNS sospitoses
│   ├── Hubs/
│   │   └── ExamenHub.cs          # Publicador Redis (temps real)
│   ├── Services/
│   │   ├── ExamenService.cs         # Lògica sessions + check-in per IP + recursos per sessió
│   │   ├── DhcpMonitorService.cs    # Monitor dhcpd.leases (IHostedService)
│   │   ├── DnsMonitorService.cs     # Monitor dns-queries.log (IHostedService)
│   │   ├── CheckinTimeoutService.cs # Detecta check-ins aturats → Desconnectat
│   │   ├── NginxLogMonitorService.cs# Acumula bytes/requestes per IP des del log nginx
│   │   ├── SessioCleanupService.cs  # Neteja sessions tancades fa >30 dies
│   │   ├── AuthService.cs           # Login professors (JWT)
│   │   ├── ClassService.cs          # Classes + alumnes
│   │   ├── BackupService.cs         # Export/import JSON + ZIP complet (amb fotos i recursos)
│   │   └── EmailService.cs          # SMTP (professors)
│   └── Program.cs                   # Endpoints + DI
├── web/                             # Blazor Server + MudBlazor
│   ├── Components/Pages/
│   │   ├── Examen/Portal.razor      # /examen (alumne, sense auth)
│   │   ├── Professor/
│   │   │   ├── Dashboard.razor      # /professor/dashboard
│   │   │   ├── Examen.razor         # /professor/examen (plafó professor)
│   │   │   ├── Classes.razor        # /professor/classes
│   │   │   └── Alumnes.razor        # /professor/alumnes/{id}
│   │   └── Admin/
│   │       ├── Professors.razor     # /admin/professors
│   │       ├── ExamenMacs.razor     # /admin/examen-macs
│   │       ├── Backup.razor         # /admin/backup
│   │       └── Estadistiques.razor  # /admin/estadistiques
│   ├── Services/
│   │   ├── ApiClient.cs             # Client HTTP cap a l'API
│   │   ├── UserStateService.cs      # Sessió Blazor (JWT professors)
│   │   ├── ExamenCircuitState.cs    # Estat alumne per circuit Blazor
│   │   ├── ExamenCircuitHandler.cs  # Detecta tancament del navegador (CircuitHandler)
│   │   ├── ExamenNotificationService.cs  # Bus intern notificacions
│   │   └── ExamenRedisSubscriber.cs      # Subscriptor Redis
│   └── Resources/
│       └── DictionaryLocalizer.cs   # i18n estàtica (ca/es)
├── shared/
│   ├── Dtos.cs                      # Tots els DTOs compartits
│   └── AppVersion.cs                # Versió actual
├── EntornExamen.Tests/
│   └── ExamenServiceTests.cs        # Tests unitaris ExamenService
├── scripts/examen/               # Configuració DHCP + DNS
│   ├── dhcp-hook.sh
│   ├── dhcpd.conf
│   ├── named.conf.local
│   └── db.examen.local
├── nginx/                        # Proxy invers SSL
├── deploy/                       # Scripts de desplegament
│   └── server-update.sh
├── .env.example
└── docker-compose.yml
```

---

## Model de dades

```
Professor ──< ProfessorLogin     (registre d'accessos)
Class ────< Student ──< AlumneMac         (MAC ↔ correu)
Class ────< SessioExamen ──< RegistreConnexio ──< PeticioTdns
                              └── Student (nullable, vinculat per IP)
```

---

## Notificacions en temps real (Redis pub/sub)

| Canal Redis | Receptor | Events |
|------------|---------|--------|
| `examen:sessio:{id}` | Professor | `AlumneConnectat`, `AlumneDesconnectat`, `NouCheckin`, `NovaPeticioExterna`, `MacDesconeguda`, `MissatgeActualitzat`, `SessioTancadaGlobal` |
| `examen:alumne:{id}` | Alumne | `MissatgeProfessor`, `SessioTancadaGlobal` |

---

## Endpoints principals de l'API

| Mètode | URL | Auth | Descripció |
|--------|-----|------|-----------|
| `POST` | `/api/auth/professor` | — | Login professor |
| `GET` | `/api/examen/sessions` | JWT | Llista sessions |
| `POST` | `/api/examen/sessions` | JWT | Crea sessió |
| `GET` | `/api/examen/sessions/{id}/dashboard` | JWT | Estat complet en temps real |
| `PUT` | `/api/examen/sessions/{id}/tancar` | JWT | Tanca sessió |
| `PUT` | `/api/examen/sessions/{id}/reobrir` | JWT | Reobre sessió |
| `PUT` | `/api/examen/sessions/{id}/missatge` | JWT | Envia missatge push |
| `DELETE` | `/api/examen/sessions/{id}/missatge` | JWT | Esborra missatge |
| `GET` | `/api/examen/sessions/{id}/exportar` | JWT | Exporta CSV |
| `POST` | `/api/examen/checkin` | — | Check-in alumne (per IP) |
| `POST` | `/api/examen/sortida` | — | Sortida voluntària alumne |
| `POST` | `/api/examen/sortida-circuit/{studentId}` | — | Sortida per circuit tancat (intern web) |
| `POST` | `/api/examen/dhcp/event` | — | Event DHCP (hook) |
| `POST` | `/api/examen/dns/event` | — | Event DNS |
| `GET/DELETE` | `/api/examen/macs` | JWT admin | Gestió dispositius |
| `GET` | `/api/admin/stats` | JWT admin | Estadístiques |
| `GET/POST` | `/api/admin/backup/*` | JWT admin | Còpies de seguretat |

---

## Tests

```bash
dotnet test AutoCo.Tests/
```

---

## Changelog

### v3.0.0 (2026-04-28)
- **Nou estat `Expulsat`** — l'alumne expulsat pel professor queda en un estat permanent (porpra) que impedeix que el check-in automàtic el torni a connectar. Fins ara, el timer de Portal.razor enviava un nou check-in pocs segons després de l'expulsió i el registre es reactivava a la BD.
- `EstatConnexio.Expulsat` (API) i `EstatConnexioDto.Expulsat` (web) — valor 4 (compatible amb BD existent sense canvi de schema)
- Totes les queries que excloïen `Desconnectat` ara també exclouen `Expulsat`: check-in, sortida voluntària, sortida per circuit, DHCP connected/disconnected, DNS
- Plafó professor: color porpra `#7c3aed`, text "Expulsat/Expulsado", filtre per estat, botó "Expulsar" ocult per a alumnes ja expulsats

### v2.9.9 (2026-04-28)
- **Branding complet a Portal.razor**: les capçaleres de les targetes (`Fase.Email` i `Fase.Connectat`) usen `var(--appbar)` en lloc del color `#1e293b` hardcoded

### v2.9.8 (2026-04-28)
- **Fix IP real de l'alumne (DI scope)**: `App.razor` escriu la IP real en `window.__entornClientIp` durant el render HTTP inicial; `Portal.razor` la llegeix via JS interop a `OnAfterRenderAsync(firstRender)` i la desa a `CircuitState.ClientIp` del circuit actiu. Necessari perquè el circuit Blazor crea un DI scope diferent del de la petició HTTP — el camp `CircuitState.ClientIp` escrit a App.razor (scope HTTP) no és el mateix que llegeix Portal.razor (scope circuit).
- **Login alumne sense domini auto-completat**: si l'alumne introdueix `usuari` sense `@domini`, es completa automàticament amb el domini configurat (`EXAMEN_DOMINI_EMAIL`) i es fa el check-in directament, sense pas intermedi de suggeriment
- **IP i sessió a la pantalla de l'alumne**: la fase `Connectat` de Portal.razor mostra la IP real del dispositiu i l'ID de sessió sota la barra de progrés del check-in
- `PageTitle` de Portal.razor ara usa `@Brand.Nom` (era `Salesians de Sarrià` hardcoded)
- `appsettings.json`: afegit `Examen:DominiEmail` amb el valor per defecte `sarria.salesians.cat`

### v2.9.7 (2026-04-28)
- **Període de gràcia de desconnexió ampliat a 90 s** — el `ExamenCircuitHandler` ara espera 90 s (era 15 s) abans de marcar l'alumne com a desconnectat, evitant falses desconnexions per salvapantalles o suspensió breu de WiFi. `DisconnectedCircuitRetentionPeriod` actualitzat a 120 s per garantir que el circuit no es destrueixi abans que el handler actuï.
- README actualitzat a v2.9.x

### v2.9.6 (2026-04-28)
- Eliminades totes les dependències estàtiques a la xarxa `192.168.100.` del codi i la UI
- `appsettings.json`: `DhcpNetworkPrefix` buit per defecte (valor ve del `.env`)
- `Examen.razor`: l'icona i el missatge "IP fora del rang DHCP" usen el prefix configurat dinàmicament
- `Diagnostic.razor`: fallback sense xarxa hardcoded

### v2.9.5 (2026-04-28)
- **nginx `network_mode: host`** — elimina el SNAT de Docker que feia que nginx veiés la IP del bridge (172.x.x.x) en lloc de la IP real de l'alumne (192.168.x.x o la que sigui). nginx escolta directament al port 4445 del servidor i veu IPs reals.
- `web` exposa port `127.0.0.1:8081:8080` al loopback del host perquè nginx hi pugui arribar
- nginx.conf: `listen 4445 ssl`, `proxy_pass http://127.0.0.1:8081`, eliminat bloc `real_ip` innecessari

### v2.9.4 (2026-04-28)
- Kestrel força IPv4 pur (`ASPNETCORE_URLS=http://0.0.0.0:8080`) a api i web — elimina adreces `::ffff:` als logs
- `UseQuerySplittingBehavior(SplitQuery)` global al DbContext — elimina warning EF Core per queries amb múltiples col·leccions incloses (`Registres` + `PeticiosDns`)
- Fix `InterceptorsNamespaces` al `.csproj` de l'API per al generador OpenAPI de .NET 10

### v2.9.3 (2026-04-28)
- **Branding completament configurable des del `.env`**: nom, nom curt, organització, color primari, color AppBar, URL del logo, URL del fons
- `BrandConfig`: servei singleton que llegeix `Brand__*` de la configuració
- `manifest.json` i `offline.html` generats dinàmicament amb colors i nom de marca
- `App.razor`: injecta variables CSS (`:root { --primary, --appbar, --bg-image }`) al `<head>`
- `MainLayout.razor`, `Index.razor`: logo, AppBar, footer i tema MudBlazor via BrandConfig
- `site.css`: usa `var(--primary)`, `var(--appbar)`, `var(--bg-image)` en lloc de colors hardcoded
- Volum `./branding:/app/wwwroot/branding:ro` per a logos i imatges de fons personalitzats

### v2.9.2 (2026-04-28)
- **Enter envia el missatge del professor** al diàleg de missatge (`Shift+Enter` per salt de línia)

### v2.9.1 (2026-04-28)
- **Fix IP real de l'alumne** — `App.razor` captura `X-Real-IP` de nginx i l'emmagatzema al `ExamenCircuitState`. `ApiClient` afegeix `X-Forwarded-For` amb la IP real a les crides de check-in i sortida cap a l'API, resolent el problema de doble registre per IP de Docker vs IP real.

### v2.9.0 (2026-04-28)
- **Fix crític: detecció de tancament de navegador** — el timer de check-in corria al servidor (Blazor Server) fins que el circuit expirava (~3 min), impedint que `CheckinTimeoutService` detectés la desconnexió. Implementat `ExamenCircuitHandler`: en detectar la pèrdua de connexió SignalR, espera 15 s per reconnexions breus i llavors marca l'alumne com a desconnectat a la BD i notifica el professor. Temps total fins a notificació: ~15 s.
- `DisconnectedCircuitRetentionPeriod = 40 s` — xarxa de seguretat addicional per aturar el timer de Portal.razor
- Nou endpoint intern `POST /api/examen/sortida-circuit/{studentId}` per al CircuitHandler (opera per studentId, no per IP)
- **Logs nginx JSON estructurats** (`log_format json_exam`) escrits a fitxer real al volum `nginx-logs`
- `NginxLogMonitorService`: cua el log nginx cada 5 s i acumula bytes enviats i nombre de requestes per IP als `RegistreConnexio` actius
- Panell de detall de l'alumne mostra **Tràfic** (bytes en format B/KB/MB + nombre de requestes)
- **Timezone configurable**: `TZ=Europe/Madrid` al `.env`, propagat a tots els contenidors Docker
- **Nivell de log configurable**: `LOG_LEVEL=Warning` al `.env`; els logs propis (`EntornExamen.*`) sempre en `Information`
- **Mode proves** (`EXAMEN_MODE_PRO=true`): permet múltiples alumnes amb la mateixa IP per a proves locals; desactiva la comprovació d'una sola estació i cerca el registre per studentId en lloc d'IP
- `ExamenAlumneDto`: afegit `RegistreId` (int, identificador únic de connexió) i `BytesEnviats`/`NumRequestes` (nullable)
- `RegistreConnexio`: nous camps `BytesEnviats (BIGINT NULL)` i `NumRequestes (INT NULL)`; DDL idempotent a l'arrencada
- `real_ip` nginx: `set_real_ip_from` per a xarxes internes 10.x, 172.16.x, 192.168.x; `real_ip_recursive on`

### v2.8.x (2026-04-28)
- v2.8.3: snackbar de desconnexió al plafó del professor (`ISnackbar`, `SnackbarDuplicatesBehavior.Allow`); factors `SenseCheckin`/`Desconnectat` configurables via `.env` com a `double`
- v2.8.2: alumne visible immediatament al plafó del professor en fer check-in (sense esperar el primer auto-refresh)
- v2.8.1: correcció factors timeout (s'accepten decimals amb `CultureInfo.InvariantCulture`)
- v2.8.0: `CheckinTimeoutService` (background service): `Connectat → SenseCheckin` i `SenseCheckin → Desconnectat` per check-ins aturats. Factors configurables via `.env`; historial de connexions en format compacte scrollable; alumne expulsat de la sessió quan el professor la tanca; `DhcpNetworkPrefix` configurable via `.env`

### v2.7.2 (2026-04-28)
- Fix crític: la icona de sortida voluntària al plafó professor **mai apareixia** — l'event `AlumneDesconnectatVoluntari` no estava al deserialitzador Redis i arribava com a `object` buit, fent fallar el pattern match
- Fix: l'endpoint d'expulsió d'alumne llegia les claims de forma diferent a la resta d'endpoints (podia llançar `NullReferenceException`); ara usa els helpers `GetUserId`/`IsAdmin` com tots els altres
- Fix: el check-in d'alumnes tenia el rate limit "auth" (10 req/min global) que bloquearia check-ins legítims en una classe de >10 alumnes; eliminat (la xarxa d'examen és una WiFi aïllada)
- Fix: `/api/health` requeria autenticació — ara és públic per a monitoratge extern
- Fix: `OnEmailKeyDown` descartava el `Task` de `ValidarEmail` (fire-and-forget); ara és `async Task`

### v2.7.1 (2026-04-28)
- Fix: botó "Eliminar sessió" de la barra no assignava `_sessioAEliminar` — l'eliminació no es feia
- Fix: l'historial de connexions no s'esborrava en canviar de sessió (entrades de sessions anteriors persistien)
- Fix: el botó "Netejar" de l'historial no buidava el camp de cerca; ara fa ambdues coses
- Fix: el temps endarrerit ("fa Xs") apareixia en targetes d'alumnes desconnectats; ara només per a Connectat/SenseCheckin
- Fix: `EliminarSessio` no netejar l'historial quan s'elimina la sessió seleccionada
- Fix: DNS del dashboard retornava 10 entrades però la UI mostrava fins a 15; ara el servidor envia 15
- Fix: `CheckinAsync` re-parsejava la variable de config de l'interval en lloc d'usar la propietat compartida
- Millora: clicar la suggerència de correu al portal d'alumne valida directament sense necessitat de prémer Continuar
- Millora: `SeleccionarSessio` amb la mateixa sessió ja seleccionada recarrega el dashboard sense re-subscriure

### v2.7.0 (2026-04-28)
- Llista de sessions recents visible a tots els professors (substitueix el MudSelect d'admin): estat, classe, títol, data d'inici/tancament; botó d'eliminació directa a les sessions tancades
- Historial de connexions cercat i filtrable per nom d'alumne o text d'event (fins a 200 entrades, ordre cronològic invers)
- Temps endarreriment check-in en temps real (timer 5 s): cada targeta i el pannell de detall mostren "fa Xs / fa Xm Ys" amb codi de colors (verd / taronja / vermell) en funció de l'interval de sessió
- `IntervalSegons` propagat des de la configuració del servidor fins a `SessioExamenDto` i usat per al codi de colors d'endarreriment
- Portal alumne (`/examen`) redissenyat per ser visualment idèntic al login de professor (capçalera fosca, `MudPaper` arrodonit, format compact)
- Fase "Connectat" del portal amb capçalera fosca (avatar + nom + classe), barra de progrés i botó de sortida

### v2.6.0 (2026-04-27)
- Fix: desubscripció de notificacions Redis en sortida/expulsió de l'alumne
- Fix: thread safety del timer d'auto-refresh (Blazor Server `InvokeAsync`)
- Fix: comprovació d'una sola estació sempre activa (amb IP de l'altra estació al missatge)
- Fix: error compilació `ForwardedHeadersOptions` — migrat a `Configure<ForwardedHeadersOptions>()` + `using`
- Icona d'avís a la graella si la IP de l'alumne no pertany al rang DHCP configurat
- Icona diferenciada a la graella per sortida voluntària vs desconnexió involuntària
- Pàgina de diagnòstic `/admin/diagnostic`: estat dels fitxers DHCP i DNS, dades de BD, guia de validació
- Neteja automàtica de sessions tancades fa més de 30 dies (servei en segon pla)

### v2.5.0 (2026-04-27)
- Alumne: botó "Sortir de l'examen" amb diàleg de confirmació; el professor ho veu com a sortida voluntària
- Professor: botó "Expulsar alumne" al panell de detall (amb confirmació); l'alumne rep un avís i és desconnectat
- Un alumne només pot connectar-se des d'una estació alhora
- Auto-refresh del dashboard del professor cada 30 s (fallback si Redis no publica)
- Camp MAC al panell de detall: ara distingeix correctament MAC real vs IP de fallback (sense DHCP)
- Sessions tancades: botó "Eliminar sessió" per esborrar-les del registre
- Sonoritat de desconnexió voluntària diferenciada de la involuntària al panell del professor

### v2.4.0 (2026-04-27)
- Plafó professor: pannell de detall d'estació ara apareix **al costat** de la graella (layout 2 columnes) en lloc d'un drawer que la comprimia
- L'estació seleccionada es ressalta amb un contorn de color
- Detall complet: foto, estat, correu, classe, núm. llista, MAC, IP, hora d'entrada (nova), últim check-in, DNS recents (fins a 15)
- `ExamenAlumneDto` ara inclou `ConnectatAt` (hora primera connexió a la sessió)
- Estacions no identificades mostren MAC/IP en lloc de nom

### v2.3.0 (2026-04-27)
- **Fix crític**: check-in crash per `IpAssignada` truncada — columna ampliada a `NVARCHAR(45)` + normalització IPv4-mapped IPv6 (`::ffff:x.x.x.x → x.x.x.x`)
- Textos de la pàgina d'inici actualitzats: "Control de Presència en Examens" (eliminades referències AutoCo)
- Icons d'inici actualitzats: Monitor / WiFi / Campaign
- Port HTTPS nginx canviat a 4445 (`docker-compose.yml`)
- Portal alumne: botó "Tornar a l'inici" a la pantalla de login
- Gestió alumnes: importació normal simplificada a CSV (XLS → secció EPSS); botons de fila alineats horitzontalment

### v2.0.0 (2026-04-27)
- **Desvinculació total d'AutoCo** — sistema independent i net
- Alumnes sense contrasenya: `PasswordHash` nullable, sense generació ni enviament
- Check-in per IP: backend llegeix `HttpContext.Connection.RemoteIpAddress`
- Portal alumne simplificat: només demana el correu electrònic
- Eliminades totes les funcionalitats AutoCo: activitats, mòduls, grups, avaluacions, resultats, criteris, plantilles, notes
- Backup simplificat: només Professors / Classes / Alumnes
- Dashboard professor reescrit centrat en sessions d'examen
- Nomenclatura Docker: `autoco-*` → `entornexamen-*`
- BD: `AutoCoAvaluacio` → `EntornExamen`
- Redis prefix i LocalStorage: `autoco:` → `entornexamen:`
- `RootNamespace` tots els projectes: `AutoCo.*` → `EntornExamen.*`

### v1.4.0 (2026-04-27)
- Importació EPSS integrada a la pàgina de gestió d'alumnes (XLS + ZIP fotos)
- Foto de l'alumne carregable directament des de la taula d'alumnes

### v1.3.1 (2026-04-27)
- Fix importació EPSS: suport HTML amb extensió `.xls`
- Fix fotos: `Regex.Replace` fora de LINQ SQL

### v1.0.0 – v1.3.0 (2026-04-26)
- Implementació inicial de l'Entorn Examen
- Models: `AlumneMac`, `SessioExamen`, `RegistreConnexio`, `PeticioTdns`
- Portal alumne i plafó professor en temps real
- Serveis background DHCP + DNS
- Importació alumnes (HTML/XLS) i fotos (ZIP per DNI)
- Internacionalització completa (ca/es)
- Scripts de configuració DHCP i BIND9

---

## Llicència

Ús intern — Salesians de Sarrià, Departament d'Informàtica.
