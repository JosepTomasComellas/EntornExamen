# EntornExamen — Control de Presència en Examens

Sistema de control de presència en temps real durant exàmens sobre xarxa WiFi aïllada. Tot el text de la interfície és en **català** (selector ca/es disponible).

**Salesians de Sarrià — Departament d'Informàtica · CFGS ASIX**

## Estructura del projecte

```
EntornExamen/
├── api/                          # API REST (ASP.NET Core 10 Minimal API)
│   ├── Data/
│   │   ├── AppDbContext.cs       # EF Core DbContext (EnsureCreated, sense migracions formals)
│   │   ├── SeedData.cs           # Admin inicial des de .env
│   │   └── Models/               # Professor, Class, Student, AlumneMac,
│   │                             #   SessioExamen, RegistreConnexio, PeticioTdns, ProfessorLogin
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
│   ├── Program.cs                # Endpoints + DI + DDL (CREATE/ALTER TABLE inline)
│   └── Dockerfile
├── web/                          # Frontend (Blazor Server + MudBlazor)
│   ├── Components/
│   │   ├── App.razor             # Arrel HTML: manifest PWA, service worker, i18n
│   │   ├── Layout/
│   │   │   └── MainLayout.razor  # Navbar, mode fosc, selector idioma
│   │   └── Pages/
│   │       ├── Index.razor       # Pàgina d'inici (accés professor / accés alumne)
│   │       ├── Auth/             # LoginProfessor, LoginAlumne (redirecció → /examen)
│   │       ├── Examen/
│   │       │   └── Portal.razor  # /examen — identificació alumne per email, check-in
│   │       ├── Professor/
│   │       │   ├── Dashboard.razor   # /professor/dashboard
│   │       │   ├── Examen.razor      # /professor/examen — plafó temps real
│   │       │   ├── Classes.razor     # /professor/classes
│   │       │   └── Alumnes.razor     # /professor/alumnes/{id}
│   │       └── Admin/
│   │           ├── Professors.razor  # /admin/professors
│   │           ├── ExamenMacs.razor  # /admin/examen-macs
│   │           ├── Backup.razor      # /admin/backup
│   │           └── Estadistiques.razor # /admin/estadistiques
│   ├── Resources/
│   │   └── DictionaryLocalizer.cs  # i18n estàtica (ca/es), evita ResourceManager a Docker
│   ├── Services/
│   │   ├── ApiClient.cs              # Client HTTP cap a l'API
│   │   ├── UserStateService.cs       # Sessió Blazor (JWT professors)
│   │   ├── ExamenNotificationService.cs  # Bus intern notificacions alumne/professor
│   │   └── ExamenRedisSubscriber.cs      # Subscriptor Redis → Blazor
│   ├── wwwroot/
│   │   ├── css/site.css          # Estils globals (dark mode, print, responsive)
│   │   ├── js/                   # app.js (utilitats), charts.js (Chart.js interop)
│   │   ├── manifest.json         # PWA manifest
│   │   ├── service-worker.js     # PWA: cache-first assets estàtics, pàgina offline
│   │   └── offline.html          # Pàgina offline en català
│   ├── Program.cs                # Configuració (Redis, MudBlazor, i18n, DataProtection)
│   └── Dockerfile
├── shared/                       # DTOs i AppVersion compartits (api + web)
│   ├── Dtos.cs                   # Tots els records de request/response
│   └── AppVersion.cs             # Versió actual
├── EntornExamen.Tests/           # Tests unitaris xUnit (ExamenService, EF Core InMemory)
├── nginx/                        # Proxy invers SSL (auto-signat o certificat propi)
│   └── nginx.conf                # Escolta 443 intern; publicat al port 4445 (docker-compose)
├── deploy/
│   └── server-update.sh          # git pull + docker compose build + up
├── scripts/examen/               # Configuració DHCP + DNS del servidor
└── docker-compose.yml            # Orquestració: db + redis + api + web + nginx
```

## Desplegament

```bash
# Actualitzar servidor (recomanat)
bash /docker/EntornExamen/deploy/server-update.sh

# Construcció completa des de zero
docker compose up --build -d
```

**URL d'accés (via nginx):**
- HTTPS: `https://192.168.100.1:4445` (xarxa d'examen) o `https://localhost:4445` (local)

## Serveis Docker

| Servei | Imatge | Descripció |
|--------|--------|------------|
| `entornexamen-db` | SQL Server 2022 Express | Base de dades principal |
| `entornexamen-redis` | Redis 7 Alpine | Caché, pub/sub temps real |
| `entornexamen-api` | ASP.NET Core 10 | API REST + JWT |
| `entornexamen-web` | ASP.NET Core 10 | Blazor Server + MudBlazor |
| `entornexamen-nginx` | nginx Alpine | Proxy SSL, WebSocket Blazor |

L'API espera que `db` i `redis` estiguin healthy abans d'arrencar.

## Configuració (variables d'entorn via .env)

Variables obligatòries:
- `MSSQL_SA_PASSWORD` — contrasenya SQL Server (mínim 8 car., majúscules + números)
- `JWT_SECRET` — secret JWT (mínim 32 caràcters)
- `ADMIN_EMAIL` / `ADMIN_PASSWORD` / `ADMIN_NOM` — credencials admin inicial

Variables opcionals:
- `EXAMEN_DOMINI_EMAIL` — domini acceptat (per defecte: `sarria.salesians.cat`)
- `EXAMEN_CHECKIN_INTERVAL_SECONDS` — interval check-in en segons (per defecte: `30`)
- `SMTP_*` — configuració SMTP per a correus de professors

## Model de dades

```
Professor ──< ProfessorLogin     (registre d'accessos)
Class ────< Student ──< AlumneMac         (MAC ↔ correu)
Class ────< SessioExamen ──< RegistreConnexio ──< PeticioTdns
                              └── Student (nullable, vinculat per IP en el check-in)
```

## Flux principal

1. Professor crea una sessió d'examen per la seva classe
2. L'alumne accedeix a `/examen`, introdueix el correu corporatiu (sense contrasenya)
3. L'API detecta la IP via `HttpContext.Connection.RemoteIpAddress` (normalitzada IPv4-mapped IPv6)
4. La IP es vincula al registre DHCP corresponent (MAC ↔ alumne)
5. L'alumne fa check-in automàtic cada N segons
6. El professor veu l'estat de tota la classe en temps real via Redis pub/sub

## Rols i autenticació

- **Admin** — professor amb `IsAdmin=true`. Gestió global.
- **Professor** — JWT. Veu i gestiona les seves classes i sessions.
- **Alumne** — sense contrasenya. Identificat per correu + IP de la xarxa d'examen.

## Notificacions Redis

| Canal | Receptor | Events |
|-------|---------|--------|
| `examen:sessio:{id}` | Professor | `AlumneConnectat`, `AlumneDesconnectat`, `NouCheckin`, `NovaPeticioExterna`, `MacDesconeguda`, `MissatgeActualitzat`, `SessioTancadaGlobal` |
| `examen:alumne:{id}` | Alumne | `MissatgeProfessor`, `SessioTancadaGlobal` |

## Convencions

- Tot el text de la UI en **català** (selector ca/es via `DictionaryLocalizer`)
- Noms de fitxers i classes en anglès, text visible en català
- API: Minimal API (no controllers), ASP.NET Core 10
- Web: **Blazor Server + MudBlazor** (no Razor Pages, no MVC, no JS frameworks)
- i18n: `DictionaryLocalizer` estàtic — evita problemes amb `ResourceManager` a Docker
- BD: `EnsureCreated` + DDL inline a `Program.cs` (no migracions EF formals)
- Temps real: Redis pub/sub → `ExamenRedisSubscriber` → `ExamenNotificationService` → Blazor
- Solució VS: `EntornExamen.sln` (Api, Web, Shared, Tests)
