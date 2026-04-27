# EntornExamen + AutoCo · v1.1.0

Sistema integrat en dues parts:
- **EntornExamen** — control de presència en temps real durant exàmens sobre xarxa WiFi aïllada
- **AutoCo** — autoavaluació i coavaluació entre iguals (base v2.2.3)

Salesians de Sarrià — Departament d'Informàtica · CFGS ASIX

---

## Entorn Examen

### Funcionament general

```
[Alumnes] → WiFi "Entorn_Examen" (192.168.100.0/24, sense internet)
                    │
                    ▼
     [VM Ubuntu 24.04 sobre Proxmox]
          ├── isc-dhcp-server  → /var/lib/dhcp/dhcpd.leases
          ├── BIND9 (DNS)      → /var/log/named/queries.log
          └── Docker Compose
                ├── nginx   (proxy SSL)
                ├── api     (ASP.NET Core 10 Minimal API)
                ├── web     (Blazor Server + MudBlazor)
                ├── db      (SQL Server 2022 Express)
                └── redis   (Redis 7 Alpine)
```

L'alumne accedeix a **`https://192.168.100.1/examen`** i s'identifica amb el seu email corporatiu. La seva MAC s'associa al compte automàticament. A partir d'aquí, fa check-in cada 30 s i el professor veu l'estat de tota la classe en temps real.

### Funcionalitats

**Professor (`/professor/examen`)**
- Inicia / tanca / reobre sessions d'examen per classe
- Monitoratge en temps real: estat de cada alumne (connectat / sense check-in / desconnectat / no connectat)
- Alertes de desconnexió amb **so** (Web Audio API, sense fitxers externs)
- Alertes de peticions DNS externes sospitoses
- Missatges push: apareixen com a diàleg obligatori a la pantalla de l'alumne
- Exportació CSV de la sessió
- Mode presentació (pantalla completa)

**Alumne (`/examen`)**
- Identificació per email corporatiu `@sarria.salesians.cat`
- Check-in automàtic cada 30 s
- Rebuda de missatges del professor (diàleg emergent obligatori)

**Importació**
- Alumnes: fitxer HTML/XLS d'Esfer@ (format del centre)
- Fotos: ZIP de `{DNI}.jpg`

### Arquitectura de dades (nous models)

```
Student ──< AlumneMac           (un per dispositiu; MAC normalitzada lowercase)
Class   ──< SessioExamen        (màx. 1 activa per classe)
SessioExamen ──< RegistreConnexio ──< PeticioTdns
Student      ──< RegistreConnexio   (null si MAC desconeguda)
```

### Estats de connexió

| Estat | Color | Condició |
|-------|-------|---------|
| `Connectat` | 🟢 Verd | Check-in < 30 s |
| `SenseCheckin` | 🟡 Groc | Connectat però sense check-in recent |
| `Desconnectat` | 🔴 Vermell | DHCP ha notificat desconnexió |
| `NoConnectat` | ⚫ Gris | Precarregat però mai connectat |

### Notificacions en temps real (Redis pub/sub)

| Canal Redis | Receptor | Events |
|------------|---------|--------|
| `examen:sessio:{id}` | Professor | `AlumneConnectat`, `AlumneDesconnectat`, `NouCheckin`, `NovaPeticioExterna`, `MacDesconeguda`, `MissatgeActualitzat` |
| `examen:alumne:{id}` | Alumne | `MissatgeProfessor`, `SessioTancadaGlobal` |

### Endpoints de l'API (Entorn Examen)

| Mètode | URL | Auth | Descripció |
|--------|-----|------|-----------|
| `GET` | `/api/examen/sessions` | JWT | Llista sessions |
| `POST` | `/api/examen/sessions` | JWT | Crea sessió (409 si ja n'hi ha d'activa) |
| `GET` | `/api/examen/sessions/{id}/dashboard` | JWT | Estat complet |
| `PUT` | `/api/examen/sessions/{id}/tancar` | JWT | Tanca la sessió |
| `PUT` | `/api/examen/sessions/{id}/reobrir` | JWT | Reobre la sessió |
| `PUT` | `/api/examen/sessions/{id}/missatge` | JWT | Envia missatge push |
| `DELETE` | `/api/examen/sessions/{id}/missatge` | JWT | Esborra el missatge |
| `GET` | `/api/examen/sessions/{id}/exportar` | JWT | Exporta CSV |
| `POST` | `/api/examen/checkin` | Cap | Check-in alumne |
| `POST` | `/api/examen/dhcp/event` | Cap | Event DHCP (hook) |
| `POST` | `/api/examen/dns/event` | Cap | Event DNS |
| `POST` | `/api/examen/importar-alumnes` | JWT (admin) | Importa alumnes HTML/XLS |
| `POST` | `/api/examen/importar-fotos` | JWT (admin) | Importa fotos ZIP |

---

## Instal·lació

### Requisits del servidor

- VM Ubuntu 24.04 (recomanat sobre Proxmox)
- Docker + Docker Compose v2
- `isc-dhcp-server` per a la xarxa d'examen
- `bind9` com a servidor DNS local
- Interfície de xarxa dedicada (192.168.100.0/24)

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
| `MSSQL_SA_PASSWORD` | ✓ | Contrasenya SQL Server |
| `JWT_SECRET` | ✓ | Secret JWT (≥ 32 caràcters) |
| `ADMIN_EMAIL` | ✓ | Email administrador inicial |
| `ADMIN_PASSWORD` | ✓ | Contrasenya administrador |
| `ADMIN_NOM` | ✓ | Nom administrador |
| `EXAMEN_DOMINI_EMAIL` | | Domini acceptat (per defecte: `sarria.salesians.cat`) |
| `EXAMEN_CHECKIN_INTERVAL_SECONDS` | | Interval check-in (per defecte: `30`) |
| `SMTP_HOST` | | Servidor SMTP (opcional) |

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

### 6. Actualitzar (desplegaments posteriors)

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
├── api/
│   ├── Data/
│   │   ├── AppDbContext.cs
│   │   └── Models/
│   │       ├── AlumneMac.cs          # Nou: MAC ↔ Alumne
│   │       ├── SessioExamen.cs       # Nou: sessió d'examen
│   │       ├── RegistreConnexio.cs   # Nou: registre connexió + EstatConnexio
│   │       └── PeticioTdns.cs        # Nou: peticions DNS
│   ├── Hubs/
│   │   └── ExamenHub.cs              # Nou: publicador Redis
│   ├── Services/
│   │   ├── ExamenService.cs          # Nou: lògica de negoci
│   │   ├── DhcpMonitorService.cs     # Nou: monitor dhcpd.leases
│   │   └── DnsMonitorService.cs      # Nou: monitor dns-queries.log
│   └── Program.cs
├── web/
│   ├── Services/
│   │   ├── ExamenNotificationService.cs  # Nou: bus intern notificacions
│   │   └── ExamenRedisSubscriber.cs      # Nou: subscriptor Redis
│   └── Components/Pages/
│       ├── Examen/Portal.razor           # Nou: /examen (alumne)
│       ├── Examen/Portal.razor           # /examen (alumne)
│       ├── Professor/Examen.razor        # /professor/examen
│       └── Admin/ExamenMacs.razor        # Nou v1.1.0: /admin/examen-macs
├── shared/Dtos.cs                    # + DTOs Entorn Examen
├── scripts/examen/                   # Configuració DHCP + DNS
│   ├── dhcp-hook.sh
│   ├── dhcpd.conf
│   ├── named.conf.local
│   └── db.examen.local
├── AutoCo.Tests/
│   └── ExamenServiceTests.cs         # Nou: 10 tests ExamenService
└── docker-compose.yml
```

---

## Tests

```bash
dotnet test AutoCo.Tests/
# Passed: 33 — 16 de ResultsService + 17 de ExamenService
```

---

## AutoCo (base)

### Funcionalitats

**Professor / Admin**
- Gestió de classes, alumnes i mòduls amb edició inline
- Activitats d'avaluació amb criteris personalitzats i plantilles
- Grups per drag & drop, importació/exportació CSV, duplicació creuada
- Resultats amb filtres avançats, gràfiques, exportació CSV/Excel, informes PDF
- Indicador de participació en temps real (Redis pub/sub)
- Recordatoris per correu, notes per alumne, registre d'activitat
- Codis QR per classe, mode fosc, selector de tema de color
- Còpies de seguretat JSON, estadístiques d'ús (Admin)

**Alumne**
- Avaluació de tots els membres del grup (inclosa autoavaluació)
- Escala E/D/C/B/A (1 / 3.5 / 5 / 7.5 / 10)
- Desat parcial i barra de progrés

### Criteris globals per defecte

| Clau | Descripció |
|------|-----------|
| `probitat` | Probitat |
| `autonomia` | Autonomia |
| `responsabilitat` | Responsabilitat i Treball de qualitat |
| `collaboracio` | Col·laboració i treball en equip |
| `comunicacio` | Comunicació |

### Model de dades AutoCo

```
Professor ──< Module ──< Activity ──< Group ──< GroupMember (Student)
              │               ├──< ActivityCriteria
              │               ├──< Evaluation ──< EvaluationScore
              │               ├──< ProfessorNote
              │               └──< ActivityLog
Class ────────┘
  ├──< Student ──< AlumneMac         (Entorn Examen)
  └──< ModuleExclusion
ActivityTemplate
```

---

## Changelog

### v1.1.0 (2026-04-26)
- Internacionalització completa del plafó professor i portal alumne (català / castellà)
- Pàgina d'administració `/admin/examen-macs` per gestionar dispositius registrats
- Foto de l'alumne al drawer del plafó professor
- Filtre per nom i estat al plafó professor
- Botó "REOBRIR SESSIÓ" al plafó professor
- Correcció `FotoUrl`: comprova existència del fitxer abans de retornar la URL
- Correcció timer portal: `Dispose()` síncron, no `DisposeAsync()`
- Correcció dades obsoletes al drawer (`_alumneSeleccionat` actualitzat en temps real)
- `IntervalSegons` dinàmic des del servidor (no codificat al portal)
- `AlumneMacDto` nou al shared Dtos
- Endpoint `GET /api/examen/macs` i `DELETE /api/examen/macs/{id}` (admin)
- 7 nous tests (17 en total per ExamenService, 33 en total)

### v1.0.0 (2026-04-26)
- Implementació inicial de l'Entorn Examen sobre la base AutoCo v2.2.3
- Nous models: `AlumneMac`, `SessioExamen`, `RegistreConnexio`, `PeticioTdns`
- Portal alumne `/examen` i plafó professor `/professor/examen`
- Serveis background: `DhcpMonitorService`, `DnsMonitorService`
- Notificacions Redis pub/sub en temps real
- Alertes sonores via Web Audio API
- Importació d'alumnes (HTML/XLS) i fotos (ZIP per DNI)
- Scripts de configuració DHCP i BIND9
- 10 tests unitaris nous (26 en total)

---

## Llicència

Ús intern — Salesians de Sarrià, Departament d'Informàtica.
