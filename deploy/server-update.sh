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

# ── Validació del .env ────────────────────────────────────────────────────────
validar_env() {
    local errors=0

    # Carrega les variables del .env (sense exportar-les a l'entorn)
    # Ignora línies buides i comentaris
    while IFS= read -r line; do
        [[ "$line" =~ ^[[:space:]]*#.*$ || -z "${line// /}" ]] && continue
        key="${line%%=*}"
        value="${line#*=}"
        key=$(echo "$key" | tr -d '[:space:]')
        [[ -z "$key" ]] && continue
        eval "ENV_${key}=$(printf '%q' "$value")"
    done < .env

    # Variables obligatòries
    for var in MSSQL_SA_PASSWORD JWT_SECRET ADMIN_EMAIL ADMIN_PASSWORD ADMIN_NOM; do
        val=$(eval echo "\${ENV_${var}:-}")
        if [ -z "$val" ]; then
            echo "  [ERROR .env] $var és obligatòria i no està definida."
            errors=$((errors + 1))
        fi
    done

    # JWT_SECRET: mínim 32 caràcters
    jwt=$(eval echo "\${ENV_JWT_SECRET:-}")
    if [ -n "$jwt" ] && [ ${#jwt} -lt 32 ]; then
        echo "  [ERROR .env] JWT_SECRET ha de tenir almenys 32 caràcters (ara en té ${#jwt})."
        errors=$((errors + 1))
    fi

    # JWT_SECRET: avís si és el valor d'exemple
    if [[ "$jwt" == *"CanviaAquest"* ]]; then
        echo "  [AVÍS  .env] JWT_SECRET sembla el valor d'exemple. Canvia'l per un secret aleatori."
    fi

    # MSSQL_SA_PASSWORD: mínim 8 caràcters + majúscula + número (requisit SQL Server)
    mssql=$(eval echo "\${ENV_MSSQL_SA_PASSWORD:-}")
    if [ -n "$mssql" ]; then
        if [ ${#mssql} -lt 8 ]; then
            echo "  [ERROR .env] MSSQL_SA_PASSWORD ha de tenir almenys 8 caràcters."
            errors=$((errors + 1))
        fi
        if ! echo "$mssql" | grep -qP '[A-Z]'; then
            echo "  [ERROR .env] MSSQL_SA_PASSWORD ha de contenir almenys una majúscula."
            errors=$((errors + 1))
        fi
        if ! echo "$mssql" | grep -qP '[0-9]'; then
            echo "  [ERROR .env] MSSQL_SA_PASSWORD ha de contenir almenys un número."
            errors=$((errors + 1))
        fi
        if [[ "$mssql" == *"CanviaAquesta"* ]]; then
            echo "  [AVÍS  .env] MSSQL_SA_PASSWORD sembla el valor d'exemple. Canvia'l."
        fi
    fi

    # ADMIN_EMAIL: format bàsic
    email=$(eval echo "\${ENV_ADMIN_EMAIL:-}")
    if [ -n "$email" ] && [[ "$email" != *"@"* ]]; then
        echo "  [ERROR .env] ADMIN_EMAIL no sembla una adreça de correu vàlida: $email"
        errors=$((errors + 1))
    fi
    if [[ "$email" == *"CorreuElectronic"* || "$email" == *"domini"* ]]; then
        echo "  [AVÍS  .env] ADMIN_EMAIL sembla el valor d'exemple. Canvia'l."
    fi

    # ADMIN_PASSWORD: avís si és el valor d'exemple
    admpwd=$(eval echo "\${ENV_ADMIN_PASSWORD:-}")
    if [[ "$admpwd" == *"CanviaAquesta"* ]]; then
        echo "  [AVÍS  .env] ADMIN_PASSWORD sembla el valor d'exemple. Canvia'l."
    fi

    if [ $errors -gt 0 ]; then
        echo ""
        echo "  S'han trobat $errors error(s) al .env. Corregeix-los abans de continuar."
        echo "  Consulta .env.example per a la documentació de cada variable."
        exit 1
    fi

    echo "  .env validat correctament."
}

echo ""
echo "[0/4] Validant configuració .env..."
validar_env

# ── Versions: actual i nova ───────────────────────────────────────────────────
extract_version() {
    grep 'Current' "$1" 2>/dev/null | sed 's/.*"\(.*\)".*/\1/' | tr -d '[:space:]'
}

VERSIO_ACTUAL=$(extract_version "shared/AppVersion.cs")

echo ""
echo "[1/5] Descarregant canvis de GitHub..."
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
echo "[2/5] Aturant contenidors..."
docker compose down

echo ""
echo "[3/5] Reconstruint imatges Docker..."
docker compose build --no-cache

echo ""
echo "[4/5] Arrencant contenidors..."
docker compose up -d

echo ""
echo "[5/5] Esperant que els serveis arranquin..."
for i in $(seq 1 20); do
    docker compose ps | grep -q "entornexamen-nginx.*Up" && break
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
