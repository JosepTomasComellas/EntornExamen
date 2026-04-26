#!/bin/bash
# Ubicació al servidor: /etc/dhcp/dhcpd-enter-hooks.d/notifica-api
# Notifica l'API quan un client DHCP es connecta o desconnecta.

API_URL="http://localhost:5000"

normalitza_mac() {
    echo "$1" | tr '[:upper:]' '[:lower:]'
}

if [ "$reason" = "BOUND" ] || [ "$reason" = "RENEW" ]; then
    MAC=$(normalitza_mac "$new_hardware_address")
    curl -s -X POST "$API_URL/api/examen/dhcp/event" \
         -H "Content-Type: application/json" \
         -d "{\"mac\":\"$MAC\",\"ip\":\"$new_ip_address\",\"event\":\"connected\"}" &
fi

if [ "$reason" = "EXPIRE" ] || [ "$reason" = "RELEASE" ] || [ "$reason" = "RESET" ]; then
    MAC=$(normalitza_mac "$old_hardware_address")
    curl -s -X POST "$API_URL/api/examen/dhcp/event" \
         -H "Content-Type: application/json" \
         -d "{\"mac\":\"$MAC\",\"event\":\"disconnected\"}" &
fi
