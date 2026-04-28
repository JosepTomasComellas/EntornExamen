# EntornExamen · v2.7.2

Sistema de control de presència en temps real durant exàmens sobre xarxa WiFi aïllada.

**Salesians de Sarrià — Departament d'Informàtica · CFGS ASIX**

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

L'alumne accedeix a **`https://192.168.100.1/examen`** i introdueix el seu correu corporatiu.
El backend detecta la seva IP des de `HttpContext` i la vincula automàticament al registre DHCP corresponent.
A partir d'aquí, fa check-in cada 30 s i el professor veu l'estat de tota la classe en temps real.

**Els alumnes no necessiten contrasenya.**

---

## Funcionalitats

### Professor (`/professor/examen`)
- Inicia / tanca / reobre sessions d'examen per classe
- Monitoratge en temps real: estat de cada alumne (connectat / sense check-in / desconnectat)
- Alertes de desconnexió amb so (Web Audio API, sense fitxers externs)
- Alertes de peticions DNS externes sospitoses
- Missatges push: apareixen com a diàleg obligatori a la pantalla de l'alumne
- Exportació CSV de la sessió
- Mode presentació (pantalla completa)

### Alumne (`/examen`)
- Identificació per correu corporatiu `@sarria.salesians.cat` (sense contrasenya)
- Check-in automàtic cada 30 s
- Rebuda de missatges del professor (diàleg emergent obligatori)

### Admin
- Gestió de professors, classes i alumnes
- Importació d'alumnes des d'EPSS (XLS/HTML auto-detectat)
- Importació de fotos (ZIP amb `{DNI_numèric}.jpg`)
- Còpies de seguretat JSON (export/import)
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
| `EXAMEN_DOMINI_EMAIL` | | Domini acceptat (per defecte: `sarria.salesians.cat`) |
| `EXAMEN_CHECKIN_INTERVAL_SECONDS` | | Interval check-in en segons (per defecte: `30`) |
| `SMTP_HOST` | | Servidor SMTP (opcional, per a correu de professors) |

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
  entornexamen_fotos-alumnes

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
│   │   ├── ExamenService.cs      # Lògica sessions + check-in per IP
│   │   ├── DhcpMonitorService.cs # Monitor dhcpd.leases (IHostedService)
│   │   ├── DnsMonitorService.cs  # Monitor dns-queries.log (IHostedService)
│   │   ├── AuthService.cs        # Login professors (JWT)
│   │   ├── ClassService.cs       # Classes + alumnes
│   │   ├── BackupService.cs      # Export/import JSON
│   │   └── EmailService.cs       # SMTP (professors)
│   └── Program.cs                # Endpoints + DI
├── web/                          # Blazor Server + MudBlazor
│   ├── Components/Pages/
│   │   ├── Examen/Portal.razor   # /examen (alumne, sense auth)
│   │   ├── Professor/
│   │   │   ├── Dashboard.razor   # /professor/dashboard
│   │   │   ├── Examen.razor      # /professor/examen (plafó professor)
│   │   │   ├── Classes.razor     # /professor/classes
│   │   │   └── Alumnes.razor     # /professor/alumnes/{id}
│   │   └── Admin/
│   │       ├── Professors.razor  # /admin/professors
│   │       ├── ExamenMacs.razor  # /admin/examen-macs
│   │       ├── Backup.razor      # /admin/backup
│   │       └── Estadistiques.razor # /admin/estadistiques
│   ├── Services/
│   │   ├── ApiClient.cs          # Client HTTP cap a l'API
│   │   ├── UserStateService.cs   # Sessió Blazor (JWT professors)
│   │   ├── ExamenNotificationService.cs  # Bus intern notificacions
│   │   └── ExamenRedisSubscriber.cs      # Subscriptor Redis
│   └── Resources/
│       └── DictionaryLocalizer.cs  # i18n estàtica (ca/es)
├── shared/
│   ├── Dtos.cs                   # Tots els DTOs compartits
│   └── AppVersion.cs             # Versió actual
├── AutoCo.Tests/
│   └── ExamenServiceTests.cs     # Tests unitaris ExamenService
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
- Icona d'avís a la graella si la IP de l'alumne no pertany al rang DHCP (192.168.100.x)
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
