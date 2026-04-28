#!/bin/sh
set -e

CERT="/etc/nginx/ssl/server.crt"
KEY="/etc/nginx/ssl/server.key"

# Genera certificat autosignat si no existeix
if [ ! -f "$CERT" ] || [ ! -f "$KEY" ]; then
    echo "[nginx] Certificat SSL no trobat. Generant certificat autosignat..."
    mkdir -p /etc/nginx/ssl
    openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
        -keyout "$KEY" \
        -out    "$CERT" \
        -subj   "/C=ES/ST=Catalunya/L=Barcelona/O=Salesians de Sarria/OU=Departament Informatica/CN=localhost"
    echo "[nginx] Certificat autosignat generat (vàlid 10 anys)."
else
    echo "[nginx] Certificat SSL trobat. Usant el certificat existent."
fi

# Assegura que el directori de logs existeix i el fitxer és accessible
# (el volum nginx-logs s'ha muntat a /var/log/nginx)
mkdir -p /var/log/nginx
touch /var/log/nginx/access.log
# error.log apunta a stderr (per veure errors de nginx a docker logs)
ln -sf /dev/stderr /var/log/nginx/error.log 2>/dev/null || true

exec nginx -g "daemon off;"
