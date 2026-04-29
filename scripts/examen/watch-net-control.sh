#!/bin/bash
# =============================================================================
# EntornExamen — Monitor de control de xarxa (BIND9 + iptables + policy routing)
# Executa com a root al servidor: sudo bash watch-net-control.sh
#
# Llegeix els fitxers del volum Docker net-control i aplica:
#   1. Zones BIND9 blocades   (blocked-zones.conf + blocked-zone.db)
#   2. Intercepció DNS iptables (dns-intercept: 1=actiu, 0=inactiu)
#   3. Policy routing per gateway per alumne (gateway-rules: IP_ALUMNE GATEWAY)
#
# Requisits: inotifywait (apt install inotify-tools), bind9, iptables, iproute2
# =============================================================================

set -euo pipefail

# ─── Configuració ─────────────────────────────────────────────────────────────
DOCKER_VOLUME_PATH="${1:-/var/lib/docker/volumes/entornexamen_net-control/_data}"
BIND_DIR="/etc/bind/entornexamen"
BIND_NAMED_CONF="/etc/bind/named.conf.local"
EXAMEN_INCLUDE_LINE='include "/etc/bind/entornexamen/blocked-zones.conf";'
DNS_INTERCEPT_IFACE="${2:-$(ip route | grep default | awk '{print $5}' | head -1)}"
SERVER_IP="${3:-$(hostname -I | awk '{print $1}')}"

# Colors
OK='\033[0;32m'; WARN='\033[1;33m'; ERR='\033[0;31m'; NC='\033[0m'; INFO='\033[0;36m'
ok()   { echo -e "${OK}[OK]${NC}  $*"; }
warn() { echo -e "${WARN}[!]${NC}   $*"; }
err()  { echo -e "${ERR}[ERR]${NC} $*"; }
info() { echo -e "${INFO}[·]${NC}   $*"; }

# ─── Comprova requisits ───────────────────────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
    err "Executa amb sudo"
    exit 1
fi

if ! command -v inotifywait &>/dev/null; then
    err "inotify-tools no instal·lat. Executa: sudo apt install inotify-tools"
    exit 1
fi

if [[ ! -d "$DOCKER_VOLUME_PATH" ]]; then
    err "Volum Docker no trobat: $DOCKER_VOLUME_PATH"
    err "Assegura't que el contenidor API ha generat almenys un fitxer de control."
    exit 1
fi

# ─── Prepara directori BIND9 ──────────────────────────────────────────────────
mkdir -p "$BIND_DIR"
chown root:bind "$BIND_DIR"

# Afegeix l'include al named.conf.local si no existeix
if ! grep -qF "$EXAMEN_INCLUDE_LINE" "$BIND_NAMED_CONF"; then
    echo "" >> "$BIND_NAMED_CONF"
    echo "$EXAMEN_INCLUDE_LINE" >> "$BIND_NAMED_CONF"
    ok "Afegit include a $BIND_NAMED_CONF"
fi

# ─── Funció d'aplicació ───────────────────────────────────────────────────────
aplicar() {
    local src="$DOCKER_VOLUME_PATH"

    info "Aplicant canvis net-control..."

    # Zones BIND9
    if [[ -f "$src/blocked-zones.conf" ]]; then
        cp "$src/blocked-zones.conf" "$BIND_DIR/blocked-zones.conf"
        cp "$src/blocked-zone.db"    "$BIND_DIR/blocked-zone.db" 2>/dev/null || true
        chown root:bind "$BIND_DIR/"*

        if named-checkconf 2>/dev/null; then
            if rndc reload 2>/dev/null; then
                ok "BIND9 recarregat: $(grep -c '^zone' "$BIND_DIR/blocked-zones.conf" 2>/dev/null || echo 0) zones blocades"
            else
                warn "rndc reload ha fallat. Intenta: systemctl reload bind9"
                systemctl reload bind9 2>/dev/null || systemctl restart bind9 2>/dev/null || true
            fi
        else
            err "named-checkconf ha detectat errors. BIND9 no recarregat."
        fi
    fi

    # Intercepció DNS via iptables
    if [[ -f "$src/dns-intercept" ]]; then
        local intercept
        intercept=$(cat "$src/dns-intercept" 2>/dev/null || echo "0")

        # Elimina regles existents d'intercepció
        iptables -t nat -D PREROUTING -i "$DNS_INTERCEPT_IFACE" -p udp --dport 53 \
            -j DNAT --to-destination "${SERVER_IP}:53" 2>/dev/null || true
        iptables -t nat -D PREROUTING -i "$DNS_INTERCEPT_IFACE" -p tcp --dport 53 \
            -j DNAT --to-destination "${SERVER_IP}:53" 2>/dev/null || true

        if [[ "$intercept" == "1" ]]; then
            # Afegeix regles iptables DNAT per interceptar DNS extern
            iptables -t nat -I PREROUTING -i "$DNS_INTERCEPT_IFACE" -p udp --dport 53 \
                -j DNAT --to-destination "${SERVER_IP}:53"
            iptables -t nat -I PREROUTING -i "$DNS_INTERCEPT_IFACE" -p tcp --dport 53 \
                -j DNAT --to-destination "${SERVER_IP}:53"
            ok "Intercepció DNS activada (interfície: $DNS_INTERCEPT_IFACE → $SERVER_IP)"
        else
            ok "Intercepció DNS desactivada"
        fi
    fi

    # Policy routing per gateway per alumne
    # Taula 200: una ruta per defecte per cada alumne amb gateway específic
    if [[ -f "$src/gateway-rules" ]]; then
        # Neteja regles anteriors de la taula 200
        while ip rule | grep -q "lookup 200"; do
            local rule_src
            rule_src=$(ip rule | grep "lookup 200" | head -1 | awk '{print $3}')
            ip rule del from "$rule_src" table 200 2>/dev/null || break
        done
        ip route flush table 200 2>/dev/null || true

        local gw_count=0
        while IFS=' ' read -r student_ip gateway_ip; do
            [[ -z "$student_ip" || "$student_ip" == \#* ]] && continue
            ip route add default via "$gateway_ip" table 200 2>/dev/null || true
            ip rule add from "$student_ip" table 200 prio 200 2>/dev/null || true
            (( gw_count++ )) || true
        done < "$src/gateway-rules"

        if (( gw_count > 0 )); then
            ok "Policy routing aplicat: $gw_count alumnes amb gateway personalitzat"
        else
            ok "Policy routing: cap alumne amb gateway personalitzat"
        fi
    fi

    info "Canvis aplicats. $(date '+%Y-%m-%d %H:%M:%S')"
}

# ─── Aplicació inicial ────────────────────────────────────────────────────────
info "EntornExamen watch-net-control iniciat"
info "Volum: $DOCKER_VOLUME_PATH"
info "Interfície: $DNS_INTERCEPT_IFACE | IP servidor: $SERVER_IP"
echo ""

aplicar

# ─── Bucle de monitoratge ─────────────────────────────────────────────────────
info "Escoltant canvis a $DOCKER_VOLUME_PATH ..."
echo "(Atura amb Ctrl+C)"
echo ""

inotifywait -m "$DOCKER_VOLUME_PATH" -e modify -e create -e moved_to \
    --include "reload-trigger" --format '%e %f' |
while read -r _event _file; do
    sleep 1  # petit retard per assegurar que tots els fitxers s'han escrit
    aplicar
done
