# build_docx.ps1 — Genera EntornExamen_Documentacio.docx des de Plantilla.docx
Add-Type -AssemblyName 'System.IO.Compression.FileSystem'
Add-Type -AssemblyName 'System.IO.Compression'

$ErrorActionPreference = 'Stop'
$docsDir   = 'D:\Claude\EntornExamen\docs'
$templatePath = Join-Path $docsDir 'Plantilla.docx'
$outputPath   = Join-Path $docsDir 'EntornExamen_Documentacio.docx'

Copy-Item $templatePath $outputPath -Force
Write-Host "Base copiada: $outputPath"

# ── Helpers ──────────────────────────────────────────────────────────────────
function X([string]$t) { $t -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;' }

function pN([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:jc w:val=""both""/><w:spacing w:after=""160""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pNi([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:jc w:val=""both""/><w:spacing w:after=""80""/><w:ind w:left=""720""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pCenter([string]$text, [string]$sz='24', [string]$color='444444') {
    $x = X $text
    "<w:p><w:pPr><w:jc w:val=""center""/><w:spacing w:before=""120"" w:after=""120""/></w:pPr><w:r><w:rPr><w:color w:val=""$color""/><w:sz w:val=""$sz""/><w:szCs w:val=""$sz""/><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pBig([string]$text, [string]$sz='96', [string]$color='C80014') {
    $x = X $text
    "<w:p><w:pPr><w:jc w:val=""center""/><w:spacing w:before=""200"" w:after=""200""/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=""$color""/><w:sz w:val=""$sz""/><w:szCs w:val=""$sz""/><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pH1([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Ttol1""/><w:spacing w:before=""480"" w:after=""240""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pH2([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Ttol2""/><w:spacing w:before=""360"" w:after=""160""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pH3([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Ttol3""/><w:spacing w:before=""240"" w:after=""120""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pBreak() { "<w:p><w:r><w:br w:type=""page""/></w:r></w:p>" }
function pE([int]$before=0, [int]$after=200) {
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:spacing w:before=""$before"" w:after=""$after""/></w:pPr></w:p>"
}
function pBullet([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:jc w:val=""both""/><w:spacing w:after=""80""/><w:ind w:left=""720"" w:hanging=""360""/></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">• $x</w:t></w:r></w:p>"
}
function pCode([string]$text) {
    $x = X $text
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:spacing w:before=""60"" w:after=""60""/><w:ind w:left=""720""/><w:shd w:val=""clear"" w:color=""auto"" w:fill=""F2F2F2""/></w:pPr><w:r><w:rPr><w:rFonts w:ascii=""Courier New"" w:hAnsi=""Courier New"" w:cs=""Courier New""/><w:sz w:val=""18""/><w:szCs w:val=""18""/><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r></w:p>"
}
function pKV([string]$key, [string]$val) {
    $xk = X $key; $xv = X $val
    "<w:p><w:pPr><w:pStyle w:val=""Normal""/><w:jc w:val=""both""/><w:spacing w:after=""100""/></w:pPr><w:r><w:rPr><w:b/><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$xk</w:t></w:r><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve""> — $xv</w:t></w:r></w:p>"
}
function pTOC1([string]$title, [string]$page) {
    $x = X $title
    "<w:p><w:pPr><w:pStyle w:val=""IDC1""/><w:tabs><w:tab w:val=""right"" w:leader=""dot"" w:pos=""8640""/></w:tabs></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>$page</w:t></w:r></w:p>"
}
function pTOC2([string]$title, [string]$page) {
    $x = X $title
    "<w:p><w:pPr><w:pStyle w:val=""IDC2""/><w:tabs><w:tab w:val=""right"" w:leader=""dot"" w:pos=""8640""/></w:tabs></w:pPr><w:r><w:rPr><w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$x</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>$page</w:t></w:r></w:p>"
}
function pTbl([string]$c1,[string]$c2,[string]$c3,[switch]$Header) {
    $x1=X $c1; $x2=X $c2; $x3=X $c3
    $shade = if ($Header) { '<w:shd w:val="clear" w:color="auto" w:fill="C80014"/>' } else { '' }
    $bold  = if ($Header) { '<w:b/><w:color w:val="FFFFFF"/>' } else { '' }
    $cell = {
        param([string]$txt, [int]$wid)
        $xs = X $txt
        "<w:tc><w:tcPr><w:tcW w:w=""$wid"" w:type=""dxa""/><w:tcBorders><w:top w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:left w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:bottom w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:right w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/></w:tcBorders>$shade</w:tcPr><w:p><w:pPr><w:pStyle w:val=""Normal""/><w:spacing w:after=""60""/></w:pPr><w:r><w:rPr>$bold<w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$xs</w:t></w:r></w:p></w:tc>"
    }
    $tc1 = & $cell $x1 2800; $tc2 = & $cell $x2 2100; $tc3 = & $cell $x3 3600
    "<w:tr>$tc1$tc2$tc3</w:tr>"
}

# ── Section properties ────────────────────────────────────────────────────────
$pgSz  = '<w:pgSz w:w="11906" w:h="16838"/>'
$pgMar = '<w:pgMar w:top="1440" w:right="1080" w:bottom="1440" w:left="1080" w:header="708" w:footer="708" w:gutter="0"/>'

$coverSectXml = "<w:sectPr>$pgSz$pgMar<w:pgNumType w:fmt=""decimal"" w:start=""0""/></w:sectPr>"
$mainSectXml  = '<w:sectPr><w:headerReference w:type="default" r:id="rId10"/><w:footerReference w:type="default" r:id="rId11"/>' + $pgSz + $pgMar + '<w:pgNumType w:fmt="decimal" w:start="1"/></w:sectPr>'

# ── Build body ────────────────────────────────────────────────────────────────
$b = [System.Text.StringBuilder]::new(65536)
function A([string]$s) { $null = $b.Append($s) }

# ════════════════════════════════════════════════════════════════════════════
# PORTADA
# ════════════════════════════════════════════════════════════════════════════
A (pE 2000 0)
A (pBig 'EntornExamen' '96' 'C80014')
A (pCenter 'Sistema de control de presència en exàmens en temps real' '30' '555555')
A (pE 600 0)
A (pCenter '────────────────────────────────────────────────' '20' 'C80014')
A (pE 600 0)
A (pCenter 'Salesians de Sarrià' '26' '666666')
A (pCenter 'Departament d''Informàtica · CFGS ASIX' '22' '777777')
A (pE 400 0)
A (pCenter 'Versió 3.5.9  ·  Abril 2026' '20' '999999')
A (pE 2000 0)
A "<w:p><w:pPr>$coverSectXml</w:pPr></w:p>"

# ════════════════════════════════════════════════════════════════════════════
# ÍNDEX
# ════════════════════════════════════════════════════════════════════════════
A (pH1 'Índex de continguts')
A (pTOC1 '1. Introducció' '3')
A (pTOC2 '1.1  Objectiu del sistema' '3')
A (pTOC2 '1.2  Requisits del sistema' '3')
A (pTOC2 '1.3  Tecnologies principals' '4')
A (pTOC1 '2. Infraestructura tècnica' '4')
A (pTOC2 '2.1  Arquitectura general' '4')
A (pTOC2 '2.2  Serveis Docker' '5')
A (pTOC2 '2.3  Xarxa d''examen' '5')
A (pTOC2 '2.4  Comunicació en temps real' '6')
A (pTOC1 '3. Instal·lació i desplegament' '6')
A (pTOC2 '3.1  Prerequisits' '6')
A (pTOC2 '3.2  Configuració inicial (.env)' '7')
A (pTOC2 '3.3  Desplegament amb Docker' '7')
A (pTOC2 '3.4  Actualització del sistema' '8')
A (pTOC1 '4. Configuració' '8')
A (pTOC2 '4.1  Variables d''entorn obligatòries' '8')
A (pTOC2 '4.2  Variables d''entorn opcionals' '9')
A (pTOC2 '4.3  Branding personalitzat' '10')
A (pTOC1 '5. Manual del professor' '11')
A (pTOC2 '5.1  Accés al sistema' '11')
A (pTOC2 '5.2  Gestió de classes i alumnes' '11')
A (pTOC2 '5.3  Creació d''una sessió d''examen' '12')
A (pTOC2 '5.4  Plafó de control en temps real' '12')
A (pTOC2 '5.5  Comunicació i expulsió' '13')
A (pTOC2 '5.6  Tancament de sessió i exportació CSV' '14')
A (pTOC1 '6. Manual de l''alumne' '14')
A (pTOC2 '6.1  Accés a l''examen' '14')
A (pTOC2 '6.2  Identificació i check-in' '15')
A (pTOC2 '6.3  Estats de connexió' '15')
A (pTOC2 '6.4  Sortida i situacions especials' '15')
A (pTOC1 '7. Referència tècnica i glossari' '16')
A (pTOC2 '7.1  Serveis Docker — resum' '16')
A (pTOC2 '7.2  API REST — endpoints principals' '16')
A (pTOC2 '7.3  Variables d''entorn — referència' '17')
A (pTOC2 '7.4  Glossari de termes' '18')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 1. INTRODUCCIÓ
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '1. Introducció')
A (pH2 '1.1  Objectiu del sistema')
A (pN 'EntornExamen és un sistema de control de presència en temps real dissenyat per al Departament d''Informàtica del centre Salesians de Sarrià (Barcelona). Permet als professors supervisar la connexió dels alumnes durant exàmens que es realitzen sobre una xarxa WiFi aïllada, sense accés a Internet.')
A (pN 'El sistema detecta automàticament quan un alumne s''identifica, quan la seva connexió és estable, quan es desconnecta i quan intenta accedir a recursos no autoritzats. Tota aquesta informació es presenta al professor en un plafó de control actualitzat en temps real, sense necessitat de recarregar la pàgina.')

A (pH2 '1.2  Requisits del sistema')
A (pN 'Requisits del servidor:')
A (pBullet 'Sistema operatiu Linux (Ubuntu 22.04 LTS o similar)')
A (pBullet 'Docker Engine 24+ i Docker Compose v2+')
A (pBullet 'Mínim 2 GB de RAM disponibles per als contenidors')
A (pBullet 'Accés a la xarxa WiFi 192.168.100.0/24 (SSID: Entorn_Examen)')
A (pBullet 'Servidor DHCP configurat (ISC dhcpd) amb el fitxer dhcpd.leases accessible')
A (pN 'Requisits del client alumne:')
A (pBullet 'Qualsevol navegador modern (Chrome, Firefox, Edge, Safari)')
A (pBullet 'Connexió a la WiFi "Entorn_Examen"')
A (pBullet 'JavaScript activat (necessari per a Blazor Server / SignalR)')

A (pH2 '1.3  Tecnologies principals')
A (pBullet 'Backend API: ASP.NET Core 10 Minimal API (C#, .NET 10)')
A (pBullet 'Frontend: Blazor Server amb MudBlazor UI components')
A (pBullet 'Base de dades: SQL Server 2022 Express (EF Core, sense migracions formals)')
A (pBullet 'Caché i missatgeria: Redis 7 Alpine (pub/sub per a temps real)')
A (pBullet 'Proxy invers: nginx Alpine (SSL autosignat, WebSocket Blazor/SignalR)')
A (pBullet 'Orquestració: Docker Compose')
A (pBullet 'Autenticació: JWT per a professors; alumnes sense contrasenya — identificació per IP')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 2. INFRAESTRUCTURA TÈCNICA
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '2. Infraestructura tècnica')
A (pH2 '2.1  Arquitectura general')
A (pN 'El sistema s''estructura en cinc contenidors Docker interconnectats, tots dins la mateixa xarxa interna de Docker. El servidor físic actua simultàniament com a punt d''accés WiFi, servidor DHCP, servidor DNS i host dels contenidors.')
A (pN 'Flux de comunicació principal:')
A (pBullet 'L''alumne es connecta a la WiFi i rep una IP del rang 192.168.100.50–200 via DHCP')
A (pBullet 'Obre el navegador i accedeix a https://192.168.100.1:4445 (nginx SSL)')
A (pBullet 'nginx redirigeix el tràfic HTTPS i WebSocket cap al contenidor web (Blazor)')
A (pBullet 'Blazor es comunica amb l''API via HTTP intern (xarxa Docker)')
A (pBullet 'L''API llegeix la BD (SQL Server) i publica events via Redis pub/sub')
A (pBullet 'El plafó del professor rep els events Redis en temps real')

A (pH2 '2.2  Serveis Docker')
A (pKV 'entornexamen-db' 'SQL Server 2022 Express. Emmagatzema professors, classes, alumnes, MACs, sessions i registres. Volum persistent: ./data/mssql.')
A (pKV 'entornexamen-redis' 'Redis 7 Alpine. Bus de missatges pub/sub per a notificacions en temps real. Caché DI injectada als serveis.')
A (pKV 'entornexamen-api' 'ASP.NET Core 10 Minimal API. Lògica de negoci: check-in, sessions, JWT, monitoratge DHCP/DNS/nginx.')
A (pKV 'entornexamen-web' 'Blazor Server + MudBlazor. Interfície professor i alumne. Es comunica amb l''API via HTTP intern. Port intern: 8080.')
A (pKV 'entornexamen-nginx' 'nginx Alpine. Proxy invers SSL (port 443 intern → 4445 extern), WebSocket Blazor/SignalR.')

A (pH2 '2.3  Xarxa d''examen')
A (pN 'La xarxa WiFi d''examen (SSID: Entorn_Examen) és completament aïllada d''Internet:')
A (pBullet 'Rang de xarxa: 192.168.100.0/24')
A (pBullet 'IP del servidor: 192.168.100.1 (configurable via EXAMEN_SERVER_IP)')
A (pBullet 'DHCP: isc-dhcp-server, fitxer /var/lib/dhcp/dhcpd.leases')
A (pBullet 'DNS intern: BIND9, resol el domini examen.local i noms interns')
A (pBullet 'No hi ha ruta a Internet (gateway isolat)')
A (pN 'El sistema monitoritza el fitxer dhcpd.leases per detectar noves assignacions d''IP i les associa a les MACs dels alumnes registrats. El monitor DNS registra les peticions que fan els alumnes durant l''examen.')

A (pH2 '2.4  Comunicació en temps real')
A (pN 'Quan l''API detecta un canvi d''estat (check-in, desconnexió, expulsió, etc.), publica un event JSON al canal Redis corresponent. El component ExamenRedisSubscriber del frontend escolta aquests canals i actualitza la interfície immediatament sense polling.')
A (pN 'Canals Redis principals:')
A (pBullet 'examen:sessio:{id}  —  AlumneConnectat, NouCheckin, AlumneDesconnectat, AlumneSenseCheckin, AlumneExpulsat, MissatgeActualitzat, SessioTancadaGlobal, RecursosActualitzats')
A (pBullet 'examen:alumne:{id}  —  MissatgeProfessor, AlumneExpulsat, SessioTancadaGlobal')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 3. INSTAL·LACIÓ I DESPLEGAMENT
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '3. Instal·lació i desplegament')
A (pH2 '3.1  Prerequisits')
A (pN 'Verificar que el servidor disposa de les eines necessàries:')
A (pCode 'curl -fsSL https://get.docker.com | sh       # Docker Engine')
A (pCode 'apt install git isc-dhcp-server bind9        # eines del servidor')
A (pN 'Docker Compose v2 s''inclou a Docker Engine 24+. Verificar versió:')
A (pCode 'docker compose version   # ha de mostrar v2.x o superior')

A (pH2 '3.2  Configuració inicial (.env)')
A (pN 'Clonar el repositori i crear el fitxer de variables d''entorn:')
A (pCode 'git clone <URL_REPOSITORI> /docker/EntornExamen')
A (pCode 'cd /docker/EntornExamen')
A (pCode 'cp .env.example .env')
A (pCode 'nano .env   # configurar les variables obligatòries')
A (pN 'Cal configurar com a mínim les cinc variables obligatòries (veure apartat 4.1) abans de continuar.')

A (pH2 '3.3  Desplegament amb Docker')
A (pN 'Primera construcció i arrencada de tots els contenidors:')
A (pCode 'docker compose up --build -d')
A (pN 'Verificar que tots els serveis han arrencat correctament (estat "healthy"):')
A (pCode 'docker compose ps')
A (pCode 'docker compose logs -f entornexamen-api   # seguir logs de l''API')
A (pN 'L''aplicació serà accessible a: https://192.168.100.1:4445  (xarxa d''examen)')
A (pN 'La primera vegada, cal acceptar l''excepció de certificat autosignat al navegador.')

A (pH2 '3.4  Actualització del sistema')
A (pN 'Per actualitzar a una nova versió sense perdre dades, utilitzar l''script de desplegament:')
A (pCode 'bash /docker/EntornExamen/deploy/server-update.sh')
A (pN 'L''script executa automàticament: git pull → docker compose build → docker compose up -d. Les dades de la base de dades es conserven gràcies al volum persistent (./data/mssql).')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 4. CONFIGURACIÓ
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '4. Configuració')
A (pN 'Totes les opcions de configuració es defineixen al fitxer .env a l''arrel del projecte. El fitxer .env.example conté totes les variables disponibles amb valors per defecte i comentaris explicatius.')

A (pH2 '4.1  Variables d''entorn obligatòries')
A (pN 'Aquestes variables han d''estar definides per arrencar correctament el sistema:')
A (pKV 'MSSQL_SA_PASSWORD' 'Contrasenya de l''administrador de SQL Server. Mínim 8 caràcters amb majúscules, minúscules i números. Exemple: ExamSA_2026!.')
A (pKV 'JWT_SECRET' 'Secret per a la signatura dels tokens JWT dels professors. Mínim 32 caràcters aleatoris.')
A (pKV 'ADMIN_EMAIL' 'Adreça de correu de l''administrador inicial del sistema.')
A (pKV 'ADMIN_PASSWORD' 'Contrasenya de l''administrador inicial.')
A (pKV 'ADMIN_NOM' 'Nom complet de l''administrador inicial.')

A (pH2 '4.2  Variables d''entorn opcionals')
A (pKV 'EXAMEN_DOMINI_EMAIL' 'Domini dels alumnes (per defecte: sarria.salesians.cat). Permet identificar-se sense escriure el @domini.')
A (pKV 'EXAMEN_CHECKIN_INTERVAL_SECONDS' 'Interval en segons entre check-ins automàtics (per defecte: 30).')
A (pKV 'EXAMEN_SERVER_IP' 'IP del servidor a la xarxa d''examen (per defecte: 192.168.100.1).')
A (pKV 'EXAMEN_CIRCUIT_GRACE_PERIOD_SECONDS' 'Espera en segons abans de marcar un alumne com a desconnectat quan el circuit SignalR cau (per defecte: 90).')
A (pKV 'EXAMEN_SENSE_CHECKIN_FACTOR' 'Factor × interval per detectar mancança de check-in (per defecte: 2.0). Pot ser decimal.')
A (pKV 'EXAMEN_DESCONNECTAT_FACTOR' 'Factor × interval per detectar desconnexió definitiva (per defecte: 4.0). Pot ser decimal.')
A (pKV 'EXAMEN_CLEANUP_RETENTION_DAYS' 'Dies de retenció de sessions tancades (per defecte: 30). Sessions antigues s''eliminen automàticament.')
A (pKV 'EXAMEN_INTERNAL_API_TOKEN' 'Token de seguretat per a crides internes web→API (X-Internal-Token). Si es deixa buit, no es valida.')
A (pKV 'EXAMEN_MODE_PRO' 'Si val true, permet múltiples connexions des de la mateixa IP. Útil per a proves en local.')
A (pKV 'EXAMEN_DHCP_NETWORK_PREFIX' 'Prefix de xarxa DHCP acceptat (per defecte: buit — accepta qualsevol IP).')
A (pKV 'LOG_LEVEL' 'Nivell de registre del framework (per defecte: Warning). Els logs propis sempre s''emeten en Information.')
A (pKV 'TZ' 'Zona horària dels contenidors (per defecte: Europe/Madrid).')
A (pKV 'SMTP_HOST / SMTP_PORT / SMTP_USER / SMTP_PASS / SMTP_FROM' 'Configuració SMTP per a l''enviament de correus als professors (recuperació de contrasenya, etc.).')

A (pH2 '4.3  Branding personalitzat')
A (pN 'El sistema permet personalitzar l''aparença sense recompilar el codi. Les imatges (logo, fons) es col·loquen al directori ./branding/, muntat com a volum de lectura al contenidor web.')
A (pKV 'BRAND_NOM' 'Nom del centre que apareix a la barra superior.')
A (pKV 'BRAND_COLOR_PRIMARY' 'Color principal de la interfície (format hex sense #). Exemple: C80014.')
A (pKV 'BRAND_COLOR_APPBAR' 'Color de la barra superior (AppBar).')
A (pKV 'BRAND_LOGO_URL' 'URL o ruta relativa del logotip. Exemple: /branding/logo.png.')
A (pKV 'BRAND_BG_IMAGE_URL' 'URL d''una imatge de fons per a la pàgina d''inici.')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 5. MANUAL DEL PROFESSOR
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '5. Manual del professor')

A (pH2 '5.1  Accés al sistema')
A (pN 'Des d''un dispositiu connectat a la xarxa d''examen o al servidor local:')
A (pBullet 'Obrir un navegador web')
A (pBullet 'Accedir a: https://192.168.100.1:4445 (des de la xarxa d''examen)')
A (pBullet 'O bé: https://localhost:4445 (des del servidor directament)')
A (pN 'El navegador mostrarà un avís de certificat autosignat. Fer clic a "Continua de totes maneres" (o similar) per acceptar l''excepció. Seleccionar "Accés professor" i introduir el correu electrònic i la contrasenya.')

A (pH2 '5.2  Gestió de classes i alumnes')
A (pN 'Accedir a "Classes" al menú lateral per gestionar les classes assignades. Des de cada classe és possible:')
A (pBullet 'Veure la llista completa d''alumnes amb les seves dades')
A (pBullet 'Afegir, editar o eliminar alumnes individualment')
A (pBullet 'Associar adreces MAC a cada alumne (per a identificació automàtica)')
A (pN 'Les MACs permeten al sistema identificar l''alumne en el moment que es connecta a la WiFi, abans fins i tot que el propi alumne ompli el formulari d''identificació. Si una MAC és desconeguda, el sistema ho notifica al professor al plafó en temps real.')

A (pH2 '5.3  Creació d''una sessió d''examen')
A (pN 'Per iniciar el control d''un examen:')
A (pBullet 'Navegar a "Examen" al menú lateral')
A (pBullet 'Seleccionar la classe a examinar del desplegable')
A (pBullet 'Opcionalment, afegir un nom descriptiu per a la sessió')
A (pBullet 'Fer clic al botó "Nova sessió"')
A (pN 'Un cop creada la sessió, els alumnes poden accedir a /examen i identificar-se. El plafó de control apareix immediatament i comença a rebre actualitzacions en temps real.')

A (pH2 '5.4  Plafó de control en temps real')
A (pN 'El plafó mostra tots els alumnes de la classe amb un codi de colors per a l''estat de connexió:')
A (pBullet 'Verd — Connectat. L''alumne fa check-in regularment dins l''interval esperat.')
A (pBullet 'Taronja — Sense check-in. No s''ha rebut check-in en el temps esperat (factor × interval).')
A (pBullet 'Vermell — Desconnectat. Sense activitat durant un temps llarg (factor × interval × 2).')
A (pBullet 'Porpra — Expulsat. El professor ha expulsat l''alumne de la sessió.')
A (pBullet 'Gris — Sense connexió. L''alumne no s''ha identificat durant aquesta sessió.')
A (pN 'El plafó s''actualitza automàticament via Redis sense recarregar la pàgina, amb un refresc de seguretat addicional cada 30 segons. El mode compacte col·loca els alumnes problemàtics al capdavant per facilitar la identificació ràpida.')
A (pN 'El panell lateral de detall (columna dreta) mostra per a l''alumne seleccionat: IP assignada, MAC, darrer check-in, bytes enviats, nombre de peticions HTTP i llista de peticions DNS realitzades durant la sessió.')

A (pH2 '5.5  Comunicació i expulsió')
A (pN 'Per enviar un missatge privat a un alumne concret:')
A (pBullet 'Fer clic sobre l''alumne al plafó per seleccionar-lo')
A (pBullet 'Escriure el missatge al camp de text del panell lateral')
A (pBullet 'Prémer "Enviar" — el missatge apareix immediatament a la pantalla de l''alumne')
A (pN 'Per enviar un missatge a tots els alumnes alhora, fer servir el camp de missatge global de la sessió (part superior del plafó).')
A (pN 'Per expulsar un alumne:')
A (pBullet 'Seleccionar l''alumne al plafó')
A (pBullet 'Fer clic al botó "Expulsar" al panell lateral')
A (pBullet 'Confirmar l''acció al diàleg emergent')
A (pN 'L''alumne expulsat rep un avís emergent i és redirigit al formulari d''identificació. No podrà tornar a entrar a la sessió mentre el registre d''expulsió estigui actiu.')

A (pH2 '5.6  Tancament de sessió i exportació CSV')
A (pN 'Per tancar la sessió d''examen:')
A (pBullet 'Fer clic a "Tancar sessió" a la part superior del plafó')
A (pBullet 'Confirmar el tancament al diàleg de confirmació')
A (pN 'Tots els alumnes connectats rebran la notificació de fi d''examen i la seva pàgina es restablirà automàticament.')
A (pN 'Exportar els registres de connexió en format CSV:')
A (pBullet 'Accedir a l''historial de sessions tancades')
A (pBullet 'Seleccionar la sessió desitjada')
A (pBullet 'Fer clic a "Exportar CSV"')
A (pN 'El fitxer generat (RFC 4180) inclou per a cada alumne: nom i cognoms, correu electrònic, IP assignada, MAC, estat final de connexió, hora d''entrada, hora darrer check-in, bytes enviats i nombre de peticions HTTP. Compatible amb Excel (separador punt i coma, capçalera sep=;).')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 6. MANUAL DE L'ALUMNE
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '6. Manual de l''alumne')

A (pH2 '6.1  Accés a l''examen')
A (pN 'Durant l''examen, el professor indicarà als alumnes que han de:')
A (pBullet 'Connectar l''ordinador a la xarxa WiFi "Entorn_Examen" (sense contrasenya WiFi)')
A (pBullet 'Obrir un navegador web (Chrome, Firefox, Edge o Safari)')
A (pBullet 'Navegar a: https://192.168.100.1:4445/examen')
A (pN 'El navegador pot mostrar un avís de certificat de seguretat. Fer clic a "Continua de totes maneres" per acceptar l''excepció i accedir a la pàgina d''examen.')

A (pH2 '6.2  Identificació i check-in')
A (pN 'A la pàgina d''examen, l''alumne ha d''introduir el correu electrònic corporatiu al camp corresponent. No cal cap contrasenya. Si s''escriu només el nom d''usuari (sense @domini), el sistema afegeix automàticament el domini configurat.')
A (pN 'Exemple: escriure "joan.puig" és equivalent a "joan.puig@sarria.salesians.cat".')
A (pN 'Un cop identificat correctament:')
A (pBullet 'La pàgina mostra l''estat "Connectat" en verd')
A (pBullet 'Es mostren la IP assignada i l''ID de sessió')
A (pBullet 'El check-in es realitza automàticament cada 30 segons (per defecte)')
A (pBullet 'Una barra de progrés indica el temps fins al proper check-in')
A (pN 'L''alumne ha de mantenir la pàgina oberta i visible durant tot l''examen.')

A (pH2 '6.3  Estats de connexió')
A (pKV 'Connectat (verd)' 'L''alumne fa check-in regularment. Tot funciona correctament.')
A (pKV 'Sense check-in (taronja)' 'El sistema no ha rebut check-in en el temps esperat. Pot indicar un problema de connexió temporal o que l''alumne ha tancat la pàgina.')
A (pKV 'Desconnectat (vermell)' 'L''alumne no ha fet check-in durant un temps llarg. El professor ho veu destacat al plafó.')
A (pKV 'Expulsat (porpra)' 'El professor ha expulsat l''alumne de la sessió. L''alumne veu un missatge d''avís.')
A (pN 'Si la connexió WiFi es talla momentàniament, el sistema espera fins a 90 segons (configurable) abans de marcar l''alumne com a desconnectat. Si la connexió es recupera dins d''aquest temps, l''estat torna automàticament a "Connectat" en menys d''un segon.')

A (pH2 '6.4  Sortida i situacions especials')
A (pN 'Sortida voluntària: l''alumne pot fer clic al botó "Sortir de l''examen" i confirmar al diàleg. Aquesta acció notifica al professor immediatament.')
A (pN 'Si el professor tanca la sessió, tots els alumnes veuen una notificació de fi d''examen i la pàgina es restableix automàticament al formulari d''identificació.')
A (pN 'Si un alumne intenta identificar-se des de dues estacions de treball alhora, el sistema rebutja la segona connexió amb un missatge d''error (aquesta restricció es pot desactivar al servidor amb EXAMEN_MODE_PRO=true per a entorns de proves).')
A (pBreak)

# ════════════════════════════════════════════════════════════════════════════
# 7. REFERÈNCIA TÈCNICA I GLOSSARI
# ════════════════════════════════════════════════════════════════════════════
A (pH1 '7. Referència tècnica i glossari')

A (pH2 '7.1  Serveis Docker — resum')
# Table header
A '<w:tbl><w:tblPr><w:tblW w:w="8500" w:type="dxa"/><w:tblBorders><w:insideH w:val="single" w:sz="4" w:color="CCCCCC"/><w:insideV w:val="single" w:sz="4" w:color="CCCCCC"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="2800"/><w:gridCol w:w="2100"/><w:gridCol w:w="3600"/></w:tblGrid>'
A (pTbl 'Servei' 'Imatge' 'Descripció' -Header)
A (pTbl 'entornexamen-db'    'sqlserver:2022-express' 'Base de dades SQL Server. Volum: ./data/mssql.')
A (pTbl 'entornexamen-redis' 'redis:7-alpine'         'Caché i bus pub/sub per a temps real.')
A (pTbl 'entornexamen-api'   'ASP.NET Core 10'        'API REST + JWT + monitor DHCP/DNS.')
A (pTbl 'entornexamen-web'   'ASP.NET Core 10'        'Blazor Server + MudBlazor. Port intern 8080.')
A (pTbl 'entornexamen-nginx' 'nginx:alpine'           'Proxy SSL. Port extern 4445 → intern 443.')
A '</w:tbl>'
A (pE 0 200)

A (pH2 '7.2  API REST — endpoints principals')
A (pKV 'POST /api/examen/checkin' 'Check-in d''un alumne. Detecta la IP, la vincula al DHCP i actualitza l''estat. Rate limit: 5 peticions / 30 s per IP.')
A (pKV 'POST /api/examen/sortida' 'Sortida voluntària de l''alumne. Identificat per IP. Sense autenticació.')
A (pKV 'POST /api/examen/sortida-circuit/{studentId}' 'Sortida per circuit SignalR tancat. Crida interna web→API protegida per X-Internal-Token.')
A (pKV 'POST /api/examen/alerta-circuit/{studentId}' 'Alerta immediata de circuit caigut → estat SenseCheckin. Crida interna.')
A (pKV 'POST /api/examen/sessions/{id}/alumnes/{id}/expulsar' 'Expulsa un alumne d''una sessió. Requereix JWT de professor.')
A (pKV 'DELETE /api/examen/sessions/{id}' 'Elimina una sessió tancada. Requereix JWT de professor.')
A (pKV 'GET /api/admin/diagnostic' 'Estat dels serveis DHCP, DNS i base de dades. Requereix JWT d''administrador.')
A (pKV 'POST /api/auth/professor/login' 'Autenticació de professor. Retorna un token JWT amb 8 h de validesa.')
A (pKV 'GET /api/professor/sessions' 'Llista les sessions actives i tancades del professor autenticat.')
A (pKV 'GET /api/examen/sessions/{id}/exportar-csv' 'Exporta els registres de la sessió en format CSV RFC 4180.')

A (pH2 '7.3  Variables d''entorn — referència')
A (pN 'Resum de totes les variables disponibles al fitxer .env. Les variables amb (*) són obligatòries.')
# Table
A '<w:tbl><w:tblPr><w:tblW w:w="8500" w:type="dxa"/><w:tblBorders><w:insideH w:val="single" w:sz="4" w:color="CCCCCC"/><w:insideV w:val="single" w:sz="4" w:color="CCCCCC"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="3400"/><w:gridCol w:w="5100"/></w:tblGrid>'

function pTbl2([string]$c1,[string]$c2,[switch]$Header) {
    $x1=X $c1; $x2=X $c2
    $shade = if ($Header) { '<w:shd w:val="clear" w:color="auto" w:fill="C80014"/>' } else { '' }
    $bold  = if ($Header) { '<w:b/><w:color w:val="FFFFFF"/>' } else { '' }
    $cell = {param($t,$w) $xs=X $t; "<w:tc><w:tcPr><w:tcW w:w=""$w"" w:type=""dxa""/><w:tcBorders><w:top w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:left w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:bottom w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/><w:right w:val=""single"" w:sz=""4"" w:color=""CCCCCC""/></w:tcBorders>$shade</w:tcPr><w:p><w:pPr><w:pStyle w:val=""Normal""/><w:spacing w:after=""60""/></w:pPr><w:r><w:rPr>$bold<w:lang w:val=""ca-ES""/></w:rPr><w:t xml:space=""preserve"">$xs</w:t></w:r></w:p></w:tc>"}
    $tc1 = & $cell $x1 3400; $tc2 = & $cell $x2 5100
    "<w:tr>$tc1$tc2</w:tr>"
}
A (pTbl2 'Variable' 'Descripció' -Header)
A (pTbl2 'MSSQL_SA_PASSWORD (*)' 'Contrasenya SQL Server (8+ car., majúscules + números)')
A (pTbl2 'JWT_SECRET (*)'         'Secret JWT professors (32+ car.)')
A (pTbl2 'ADMIN_EMAIL (*)'        'Correu administrador inicial')
A (pTbl2 'ADMIN_PASSWORD (*)'     'Contrasenya administrador inicial')
A (pTbl2 'ADMIN_NOM (*)'          'Nom complet administrador inicial')
A (pTbl2 'EXAMEN_DOMINI_EMAIL'    'Domini alumnes (def: sarria.salesians.cat)')
A (pTbl2 'EXAMEN_CHECKIN_INTERVAL_SECONDS' 'Interval check-in en segons (def: 30)')
A (pTbl2 'EXAMEN_SERVER_IP'       'IP servidor xarxa examen (def: 192.168.100.1)')
A (pTbl2 'EXAMEN_CIRCUIT_GRACE_PERIOD_SECONDS' 'Espera reconexió SignalR (def: 90 s)')
A (pTbl2 'EXAMEN_SENSE_CHECKIN_FACTOR' 'Factor × interval per a SenseCheckin (def: 2.0)')
A (pTbl2 'EXAMEN_DESCONNECTAT_FACTOR' 'Factor × interval per a Desconnectat (def: 4.0)')
A (pTbl2 'EXAMEN_CLEANUP_RETENTION_DAYS' 'Dies retenció sessions (def: 30)')
A (pTbl2 'EXAMEN_INTERNAL_API_TOKEN' 'Token intern web→API (def: buit = no validat)')
A (pTbl2 'EXAMEN_MODE_PRO'        'true = permet IPs duplicades (per a proves)')
A (pTbl2 'LOG_LEVEL'              'Nivell log framework (def: Warning)')
A (pTbl2 'TZ'                     'Zona horària contenidors (def: Europe/Madrid)')
A (pTbl2 'BRAND_NOM'              'Nom del centre a la capçalera')
A (pTbl2 'BRAND_COLOR_PRIMARY'    'Color principal UI (hex sense #)')
A (pTbl2 'BRAND_LOGO_URL'         'URL del logotip')
A (pTbl2 'SMTP_HOST / SMTP_PORT'  'Configuració SMTP per a correus professors')
A '</w:tbl>'
A (pE 0 200)

A (pH2 '7.4  Glossari de termes')
A (pKV 'Check-in' 'Petició periòdica que envia l''ordinador de l''alumne per confirmar la connexió activa.')
A (pKV 'Circuit SignalR' 'Connexió persistent entre el navegador i el servidor Blazor per a comunicació en temps real.')
A (pKV 'DHCP' 'Dynamic Host Configuration Protocol. Assigna adreces IP automàticament als dispositius de la xarxa.')
A (pKV 'DNS' 'Domain Name System. Tradueix noms de domini a adreces IP. BIND9 al servidor d''examen.')
A (pKV 'JWT' 'JSON Web Token. Mecanisme d''autenticació sense estat que utilitzen els professors.')
A (pKV 'MAC' 'Media Access Control. Adreça física de la targeta de xarxa. Identifica el dispositiu de l''alumne.')
A (pKV 'Minimal API' 'Estil de programació d''ASP.NET Core 10 que defineix endpoints sense controladors MVC.')
A (pKV 'MudBlazor' 'Biblioteca de components UI per a Blazor, basada en Material Design.')
A (pKV 'nginx' 'Servidor web i proxy invers. Gestiona connexions SSL i redirigeix el tràfic als contenidors.')
A (pKV 'pub/sub' 'Patró publicació/subscripció de Redis. Permet notificacions en temps real sense polling.')
A (pKV 'PWA' 'Progressive Web App. Permet instal·lar l''aplicació com si fos una app nativa.')
A (pKV 'Rate limiting' 'Limitació del nombre de peticions per IP (5 check-ins / 30 s) per evitar abús.')
A (pKV 'Redis' 'Base de dades en memòria utilitzada com a bus de missatges per a notificacions en temps real.')
A (pKV 'SignalR' 'Biblioteca .NET per a comunicació bidireccional en temps real via WebSocket.')
A (pKV 'SQL Server' 'Sistema de gestió de BD relacionals de Microsoft. Allotjat en un contenidor Docker.')
A (pKV 'X-Internal-Token' 'Capçalera HTTP per autenticar crides internes del contenidor web cap a l''API.')

# Final section properties (with header + footer)
A $mainSectXml

# ── Assemble document ────────────────────────────────────────────────────────
$ns  = 'xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" '
$ns += 'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" '
$ns += 'xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" '
$ns += 'xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml" '
$ns += 'mc:Ignorable="w14"'

$doc = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
       "<w:document $ns>" +
       '<w:body>' +
       $b.ToString() +
       '</w:body></w:document>'

# ── Inject into ZIP ──────────────────────────────────────────────────────────
$zip = [System.IO.Compression.ZipFile]::Open($outputPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('word/document.xml')
    if ($entry) { $entry.Delete() }
    $ne     = $zip.CreateEntry('word/document.xml', [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $ne.Open()
    $enc    = New-Object System.Text.UTF8Encoding($false)  # no BOM
    $writer = New-Object System.IO.StreamWriter($stream, $enc)
    $writer.Write($doc)
    $writer.Flush(); $writer.Close(); $stream.Close()
    Write-Host "word/document.xml reemplacat"

    # Update core.xml metadata
    $coreEntry = $zip.GetEntry('docProps/core.xml')
    if ($coreEntry) {
        $sr = New-Object System.IO.StreamReader($coreEntry.Open())
        $coreXml = $sr.ReadToEnd(); $sr.Close()
        $coreXml = $coreXml -replace '<dc:creator>[^<]*</dc:creator>','<dc:creator>Departament d''Informatica - CFGS ASIX</dc:creator>'
        $coreXml = $coreXml -replace '<dc:title>[^<]*</dc:title>','<dc:title>EntornExamen - Documentacio del sistema</dc:title>'
        $coreXml = $coreXml -replace '<dc:description>[^<]*</dc:description>','<dc:description>Sistema de control de presencia en examens en temps real</dc:description>'
        $coreEntry.Delete()
        $nc2 = $zip.CreateEntry('docProps/core.xml', [System.IO.Compression.CompressionLevel]::Optimal)
        $s2  = $nc2.Open()
        $w2  = New-Object System.IO.StreamWriter($s2, $enc)
        $w2.Write($coreXml)
        $w2.Flush(); $w2.Close(); $s2.Close()
        Write-Host "docProps/core.xml actualitzat"
    }
} finally {
    $zip.Dispose()
}

Write-Host "`nDocumentacio generada a: $outputPath"
$size = (Get-Item $outputPath).Length
Write-Host "Mida: $([Math]::Round($size/1024,1)) KB"
