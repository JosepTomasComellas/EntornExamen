#!/bin/bash
# =============================================================================
# EntornExamen — Configuració DNS (BIND9) i DHCP
# Executa com a root: sudo bash setup-dns-dhcp.sh [zona] [ip-servidor] [prefix-dhcp]
#
# Exemples:
#   sudo bash setup-dns-dhcp.sh sds.int 172.26.1.1 172.26.1.
#   sudo bash setup-dns-dhcp.sh                          # usa valors per defecte
# =============================================================================

set -euo pipefail

# ─── PARÀMETRES (modifica aquí o passa'ls com a arguments) ───────────────────
DNS_ZONE="${1:-sds.int}"
SERVER_IP="${2:-172.26.1.1}"
DHCP_PREFIX="${3:-172.26.1.}"

# ─── Derivats automàticament ─────────────────────────────────────────────────
DHCP_NETWORK="${DHCP_PREFIX}0"
RANGE_START="${DHCP_PREFIX}100"
RANGE_END="${DHCP_PREFIX}200"
ZONE_FILE="/etc/bind/db.${DNS_ZONE}"
LOG_DIR="/var/log/named"
LOG_FILE="${LOG_DIR}/queries.log"

# ─── Colors ──────────────────────────────────────────────────────────────────
OK='\033[0;32m'   # verd
WARN='\033[1;33m' # groc
ERR='\033[0;31m'  # vermell
NC='\033[0m'      # reset
INFO='\033[0;36m' # cian

ok()   { echo -e "${OK}[OK]${NC}  $*"; }
warn() { echo -e "${WARN}[!]${NC}   $*"; }
err()  { echo -e "${ERR}[ERR]${NC} $*"; }
info() { echo -e "${INFO}[·]${NC}   $*"; }
step() { echo -e "\n${INFO}━━━ $* ━━━${NC}"; }

# ─── Comprova que s'executa com a root ───────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
    err "Executa amb: sudo bash $0"
    exit 1
fi

echo ""
echo "  EntornExamen — Configuració DNS + DHCP"
echo "  Zona DNS  : $DNS_ZONE"
echo "  IP servidor: $SERVER_IP"
echo "  Rang DHCP : ${RANGE_START} – ${RANGE_END}"
echo ""

# =============================================================================
# PART 1: BIND9 — Log de consultes
# =============================================================================
step "BIND9 — Log de consultes DNS"

# 1a. Directori de logs
if [[ ! -d "$LOG_DIR" ]]; then
    mkdir -p "$LOG_DIR"
    ok "Creat $LOG_DIR"
else
    ok "$LOG_DIR ja existeix"
fi
chown bind:bind "$LOG_DIR"

# 1b. Comprova si category queries ja està configurada
if named-checkconf -p 2>/dev/null | grep -q "category queries"; then
    ok "category queries ja configurada a BIND9 — no cal modificar res"
else
    # Localitza el fitxer que conté el bloc logging {}
    LOGGING_FILE=""
    for f in /etc/bind/named.conf /etc/bind/named.conf.options /etc/bind/named.conf.local; do
        if [[ -f "$f" ]] && grep -q "^logging" "$f" 2>/dev/null; then
            LOGGING_FILE="$f"
            break
        fi
        if [[ -f "$f" ]] && grep -q "logging {" "$f" 2>/dev/null; then
            LOGGING_FILE="$f"
            break
        fi
    done

    CHANNEL_AND_CAT='
    channel dns_queries {
        file "/var/log/named/queries.log" versions 3 size 5m;
        severity dynamic;
        print-time yes;
    };
    category queries { dns_queries; };'

    if [[ -n "$LOGGING_FILE" ]]; then
        info "Bloc logging trobat a: $LOGGING_FILE"
        # Insereix el channel i category just abans del tancament del bloc logging
        cp "$LOGGING_FILE" "${LOGGING_FILE}.bak"
        python3 - "$LOGGING_FILE" "$CHANNEL_AND_CAT" <<'PYEOF'
import sys, re

path   = sys.argv[1]
insert = sys.argv[2]
text   = open(path).read()

# Cerca el bloc logging { ... } i insereix abans del darrer };
# Assumeix que el bloc logging és el primer bloc que comença per "logging"
pattern = r'(logging\s*\{)(.*?)(\n\s*\};)'
def replace(m):
    return m.group(1) + m.group(2) + insert + m.group(3)

new_text, n = re.subn(pattern, replace, text, count=1, flags=re.DOTALL)
if n == 0:
    print("ERROR: no s'ha trobat el bloc logging per modificar", file=sys.stderr)
    sys.exit(1)
open(path, 'w').write(new_text)
PYEOF
        ok "Afegit channel dns_queries i category queries a $LOGGING_FILE"
        ok "Còpia de seguretat: ${LOGGING_FILE}.bak"
    else
        # No existeix bloc logging: crea'n un al named.conf.local
        LOGGING_FILE="/etc/bind/named.conf.local"
        info "No s'ha trobat bloc logging existent. Creant-ne un a $LOGGING_FILE"
        cat >> "$LOGGING_FILE" << EOF

logging {
    channel dns_queries {
        file "/var/log/named/queries.log" versions 3 size 5m;
        severity dynamic;
        print-time yes;
    };
    category queries { dns_queries; };
};
EOF
        ok "Bloc logging afegit a $LOGGING_FILE"
    fi
fi

# 1c. Valida la configuració
if named-checkconf; then
    ok "named-checkconf: configuració vàlida"
else
    err "Errors a la configuració de BIND9. Revisa els fitxers modificats."
    warn "Còpia de seguretat disponible a: ${LOGGING_FILE}.bak"
    exit 1
fi

# =============================================================================
# PART 2: BIND9 — Zona DNS per a $DNS_ZONE
# =============================================================================
step "BIND9 — Zona $DNS_ZONE"

# 2a. Comprova si la zona ja existeix
if named-checkconf -p 2>/dev/null | grep -q "\"${DNS_ZONE}\""; then
    ok "Zona $DNS_ZONE ja configurada — no cal modificar res"
else
    # Afegeix la zona a named.conf.local
    SERIAL=$(date +%Y%m%d01)
    cat >> /etc/bind/named.conf.local << EOF

zone "${DNS_ZONE}" {
    type master;
    file "${ZONE_FILE}";
};
EOF
    ok "Zona $DNS_ZONE afegida a /etc/bind/named.conf.local"

    # Crea el fitxer de zona
    cat > "$ZONE_FILE" << EOF
\$TTL 604800
@   IN  SOA ${DNS_ZONE}. root.${DNS_ZONE}. (
            ${SERIAL} ; Serial
            604800    ; Refresh
            86400     ; Retry
            2419200   ; Expire
            604800 )  ; Negative Cache TTL
@   IN  NS  ${DNS_ZONE}.
@   IN  A   ${SERVER_IP}
www IN  A   ${SERVER_IP}
EOF
    ok "Fitxer de zona creat: $ZONE_FILE"
fi

# 2b. Valida la zona
if named-checkzone "$DNS_ZONE" "$ZONE_FILE" > /dev/null 2>&1; then
    ok "named-checkzone: zona vàlida"
else
    warn "named-checkzone ha detectat problemes a $ZONE_FILE:"
    named-checkzone "$DNS_ZONE" "$ZONE_FILE" || true
fi

# 2c. Reinicia BIND9
if systemctl restart bind9 2>/dev/null || systemctl restart named 2>/dev/null; then
    ok "BIND9 reiniciat correctament"
else
    err "No s'ha pogut reiniciar BIND9"
    journalctl -xe -u bind9 --no-pager | tail -20
    exit 1
fi

# =============================================================================
# PART 3: DHCP — Detecta servidor i fitxer de leases
# =============================================================================
step "DHCP — Detecció del servidor"

DHCP_SERVER=""
LEASES_PATH=""

if systemctl is-active --quiet isc-dhcp-server 2>/dev/null; then
    DHCP_SERVER="isc-dhcp-server"
    LEASES_PATH="/var/lib/dhcp/dhcpd.leases"
    ok "Servidor detectat: isc-dhcp-server"
elif systemctl is-active --quiet dnsmasq 2>/dev/null; then
    DHCP_SERVER="dnsmasq"
    for p in /var/lib/misc/dnsmasq.leases /tmp/dnsmasq.leases /var/lib/dnsmasq/dnsmasq.leases; do
        if [[ -f "$p" ]]; then LEASES_PATH="$p"; break; fi
    done
    warn "Servidor detectat: dnsmasq"
    warn "El parser de DhcpMonitorService suporta format isc-dhcp-server."
    warn "Si uses dnsmasq, el format del fitxer de leases és diferent — contacta amb el desenvolupador."
else
    warn "No s'ha detectat cap servidor DHCP actiu (isc-dhcp-server o dnsmasq)."
fi

if [[ -n "$LEASES_PATH" ]]; then
    ok "Fitxer de leases: $LEASES_PATH"
else
    warn "Fitxer de leases no localitzat automàticament."
    LEASES_PATH="/var/lib/dhcp/dhcpd.leases"
    warn "Assumint ruta per defecte: $LEASES_PATH"
fi

# =============================================================================
# PART 4: Resum de passos manuals restants
# =============================================================================
step "Passos manuals restants"

echo ""
echo "  ┌─────────────────────────────────────────────────────────────────┐"
echo "  │  1. docker-compose.yml — munta el fitxer de leases a l'API     │"
echo "  └─────────────────────────────────────────────────────────────────┘"
echo ""
echo "  Afegeix al servei 'api' a docker-compose.yml:"
echo ""
echo "    api:"
echo "      volumes:"
echo "        - ${LEASES_PATH}:/data/dhcpd.leases:ro"
echo ""
echo "  ┌─────────────────────────────────────────────────────────────────┐"
echo "  │  2. .env — variables d'entorn                                   │"
echo "  └─────────────────────────────────────────────────────────────────┘"
echo ""
echo "  Afegeix o actualitza al fitxer .env:"
echo ""
echo "    EXAMEN_DHCP_NETWORK_PREFIX=${DHCP_PREFIX}"
echo "    EXAMEN_DOMINI_EMAIL=${DNS_ZONE}"
echo ""
echo "  ┌─────────────────────────────────────────────────────────────────┐"
echo "  │  3. DHCP — subxarxa (via Webmin o dhcpd.conf)                  │"
echo "  └─────────────────────────────────────────────────────────────────┘"
echo ""
echo "  Assegura't que dhcpd.conf conté:"
echo ""
echo "    subnet ${DHCP_NETWORK} netmask 255.255.255.0 {"
echo "        range ${RANGE_START} ${RANGE_END};"
echo "        option domain-name-servers ${SERVER_IP};"
echo "        option domain-name \"${DNS_ZONE}\";"
echo "        default-lease-time 3600;"
echo "        max-lease-time 7200;"
echo "        # sense default-router = sense accés a internet"
echo "    }"
echo ""
echo "  ┌─────────────────────────────────────────────────────────────────┐"
echo "  │  4. Reconstrueix els contenidors Docker                         │"
echo "  └─────────────────────────────────────────────────────────────────┘"
echo ""
echo "    cd /docker/EntornExamen"
echo "    docker compose up --build -d"
echo ""
echo "  ┌─────────────────────────────────────────────────────────────────┐"
echo "  │  5. Verificació                                                  │"
echo "  └─────────────────────────────────────────────────────────────────┘"
echo ""
echo "    # El log DNS s'omple quan els alumnes fan consultes:"
echo "    tail -f ${LOG_FILE}"
echo ""
echo "    # L'API detecta connexions DHCP (espera fins a 5s):"
echo "    docker compose logs -f api | grep -i dhcp"
echo ""
ok "Script completat."
echo ""
