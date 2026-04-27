#!/usr/bin/env bash
# =============================================================================
# EntornExamen - Actualització directa des de GitHub
# Us: bash /docker/EntornExamen/deploy/server-update.sh
# =============================================================================
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

echo ""
echo "========================================================"
echo "  EntornExamen - Actualització des de GitHub"
echo "  Directori: $DEPLOY_DIR"
echo "========================================================"

cd "$DEPLOY_DIR"

# Comprova que és un repositori git
if [ ! -d ".git" ]; then
    echo ""
    echo "[ERROR] $DEPLOY_DIR no és un repositori git."
    echo "        Clona el repo primer:"
    echo "        git clone https://github.com/JosepTomasComellas/EntornExamen.git $DEPLOY_DIR"
    exit 1
fi

# Comprova que existeix el .env
if [ ! -f ".env" ]; then
    echo ""
    echo "[ERROR] No s'ha trobat .env a $DEPLOY_DIR"
    echo "        Copia .env.example a .env i edita les variables."
    exit 1
fi

# ── Versions: actual i nova ───────────────────────────────────────────────────
extract_version() {
    grep 'Current' "$1" 2>/dev/null | sed 's/.*"\(.*\)".*/\1/' | tr -d '[:space:]'
}

VERSIO_ACTUAL=$(extract_version "shared/AppVersion.cs")

echo ""
echo "[1/4] Descarregant canvis de GitHub..."
git fetch --tags origin

VERSIO_NOVA=$(git show origin/main:shared/AppVersion.cs 2>/dev/null \
    | grep 'Current' | sed 's/.*"\(.*\)".*/\1/' | tr -d '[:space:]')

echo ""
echo "  Versió actual desplegada : ${VERSIO_ACTUAL:-desconeguda}"
echo "  Versió al repositori     : ${VERSIO_NOVA:-desconeguda}"

# Mostra els commits nous si n'hi ha
COMMITS_NOUS=$(git log HEAD..origin/main --oneline 2>/dev/null)
if [ -n "$COMMITS_NOUS" ]; then
    echo ""
    echo "  Commits nous:"
    git log HEAD..origin/main --oneline | sed 's/^/    /'
else
    echo ""
    echo "  Ja és la versió més recent. Continuant igualment..."
fi

echo ""
git pull --ff-only

VERSIO_DESPLEGADA=$(extract_version "shared/AppVersion.cs")

echo ""
echo "[2/4] Aturant contenidors..."
docker compose down

echo ""
echo "[3/4] Reconstruint imatges Docker..."
docker compose build --no-cache

echo ""
echo "[4/4] Arrencant contenidors..."
docker compose up -d

echo ""
echo "  Esperant que els serveis arranquin..."
for i in $(seq 1 20); do
    docker compose ps | grep -q "autoco-nginx.*Up" && break
    printf "." && sleep 3
done
echo ""

echo ""
echo "========================================================"
if [ "$VERSIO_ACTUAL" != "$VERSIO_DESPLEGADA" ]; then
    echo "  Actualització completada: v$VERSIO_ACTUAL → v$VERSIO_DESPLEGADA"
else
    echo "  Actualització completada: v$VERSIO_DESPLEGADA (sense canvi de versió)"
fi
echo ""
docker compose ps
echo ""
echo "  Logs en temps real: docker compose logs -f"
echo "========================================================"
