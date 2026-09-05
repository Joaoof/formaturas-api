#!/usr/bin/env bash
set -euo pipefail

# =====================================================================
# install.sh — provisiona uma VPS Ubuntu 24.04 do zero para rodar a API
# Uso:
#   curl -fsSL https://raw.githubusercontent.com/Joaoof/formaturas-api/main/deploy/install.sh | bash
# =====================================================================

APP_USER="deploy"
APP_DIR="/home/${APP_USER}/app"
COMPOSE_DIR="${APP_DIR}/deploy"

log()  { printf "\033[1;36m[install]\033[0m %s\n" "$*"; }
warn() { printf "\033[1;33m[install]\033[0m %s\n" "$*"; }
fail() { printf "\033[1;31m[install]\033[0m %s\n" "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || fail "Rode como root: ssh root@ip \"bash install.sh\""

# --- 1) sistema base ---
log "Atualizando pacotes e instalando dependencias..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -y >/dev/null
apt-get upgrade -y >/dev/null
apt-get install -y ca-certificates curl gnupg lsb-release ufw fail2ban unattended-upgrades >/dev/null
dpkg-reconfigure -f noninteractive unattended-upgrades >/dev/null

# --- 2) usuario deploy (nao-root) ---
if ! id -u "$APP_USER" >/dev/null 2>&1; then
  log "Criando usuario $APP_USER..."
  adduser --disabled-password --gecos "" "$APP_USER" >/dev/null
  usermod -aG sudo "$APP_USER"
fi

if [[ -f /root/.ssh/authorized_keys ]]; then
  log "Copiando authorized_keys do root para $APP_USER..."
  install -d -m 700 -o "$APP_USER" -g "$APP_USER" "/home/$APP_USER/.ssh"
  install -m 600 -o "$APP_USER" -g "$APP_USER" \
    /root/.ssh/authorized_keys "/home/$APP_USER/.ssh/authorized_keys"
fi

# --- 3) firewall e fail2ban ---
log "Configurando UFW (libera 22 SSH, 80 HTTP)..."
ufw --force reset >/dev/null
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null
ufw allow 22/tcp >/dev/null
ufw allow 80/tcp >/dev/null
ufw --force enable >/dev/null

log "Habilitando fail2ban..."
systemctl enable --now fail2ban >/dev/null

# --- 4) docker engine oficial ---
if ! command -v docker >/dev/null 2>&1; then
  log "Instalando Docker Engine..."
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -y >/dev/null
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin >/dev/null
fi
usermod -aG docker "$APP_USER"

# --- 5) arquivos de deploy (compose + env exemplo) ---
log "Escrevendo docker-compose.yml e .env.example em $COMPOSE_DIR..."
install -d -m 750 -o "$APP_USER" -g "$APP_USER" "$COMPOSE_DIR"

cat > "$COMPOSE_DIR/docker-compose.yml" <<'YAML'
name: formaturasflow

networks:
  edge:
  internal:
    internal: true

volumes:
  postgres_data:

services:
  postgres:
    image: postgres:16-alpine
    container_name: formaturas-postgres
    restart: unless-stopped
    networks: [internal]
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      TZ: America/Sao_Paulo
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 10s
    labels:
      com.centurylinklabs.watchtower.enable: "false"

  api:
    image: ghcr.io/${GHCR_OWNER}/${GHCR_IMAGE}:${API_TAG:-latest}
    container_name: formaturas-api
    restart: unless-stopped
    depends_on:
      postgres: { condition: service_healthy }
    networks: [edge, internal]
    ports:
      - "80:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:8080
      ConnectionStrings__Default: "Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      Jwt__Issuer: ${JWT_ISSUER}
      Jwt__Audience: ${JWT_AUDIENCE}
      Jwt__Key: ${JWT_KEY}
      Jwt__AccessTokenMinutes: ${JWT_ACCESS_TOKEN_MINUTES:-60}
      Asaas__Sandbox: ${ASAAS_SANDBOX:-true}
      Asaas__ApiKey: ${ASAAS_API_KEY}
      Asaas__WebhookToken: ${ASAAS_WEBHOOK_TOKEN}
      Cora__Sandbox: ${CORA_SANDBOX:-true}
      Cora__ClientId: ${CORA_CLIENT_ID}
      Cora__CertificateBase64: ${CORA_CERTIFICATE_BASE64}
      Cora__CertificatePassword: ${CORA_CERTIFICATE_PASSWORD}
      Cora__WebhookToken: ${CORA_WEBHOOK_TOKEN}
      TZ: America/Sao_Paulo
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
      interval: 30s
    labels:
      com.centurylinklabs.watchtower.enable: "true"

  watchtower:
    image: nickfedor/watchtower:latest
    container_name: formaturas-watchtower
    restart: unless-stopped
    networks: [edge]
    environment:
      TZ: America/Sao_Paulo
      WATCHTOWER_LABEL_ENABLE: "true"
      WATCHTOWER_CLEANUP: "true"
      WATCHTOWER_POLL_INTERVAL: "60"
      REPO_USER: ${GHCR_USER}
      REPO_PASS: ${GHCR_TOKEN}
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
    labels:
      com.centurylinklabs.watchtower.enable: "false"
YAML

cat > "$COMPOSE_DIR/.env.example" <<'ENV'
GHCR_OWNER=joaoof
GHCR_IMAGE=formaturas-api
API_TAG=latest

GHCR_USER=Joaoof
GHCR_TOKEN=cole_um_PAT_com_read_packages

POSTGRES_DB=formaturas
POSTGRES_USER=formaturas
POSTGRES_PASSWORD=troque_por_senha_forte

JWT_ISSUER=http://SEU_IP_OU_DOMINIO
JWT_AUDIENCE=formaturasflow-front
JWT_KEY=troque_por_chave_de_32_bytes_ou_mais
JWT_ACCESS_TOKEN_MINUTES=60

ASAAS_SANDBOX=true
ASAAS_API_KEY=
ASAAS_WEBHOOK_TOKEN=

CORA_SANDBOX=true
CORA_CLIENT_ID=
CORA_CERTIFICATE_BASE64=
CORA_CERTIFICATE_PASSWORD=
CORA_WEBHOOK_TOKEN=
ENV

if [[ ! -f "$COMPOSE_DIR/.env" ]]; then
  install -m 600 -o "$APP_USER" -g "$APP_USER" "$COMPOSE_DIR/.env.example" "$COMPOSE_DIR/.env"
fi
chown -R "$APP_USER:$APP_USER" "$APP_DIR"

# --- 6) hardening SSH (por ultimo, para nao trancar em caso de erro antes) ---
log "Desabilitando login SSH como root e por senha..."
SSHD=/etc/ssh/sshd_config
sed -ri 's/^#?PermitRootLogin.*/PermitRootLogin no/' "$SSHD"
sed -ri 's/^#?PasswordAuthentication.*/PasswordAuthentication no/' "$SSHD"
sed -ri 's/^#?KbdInteractiveAuthentication.*/KbdInteractiveAuthentication no/' "$SSHD"
systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || true

# --- final ---
IP=$(hostname -I | awk '{print $1}')
cat <<FINAL

\033[1;32m✓ Instalacao concluida!\033[0m

Proximos passos (agora como o usuario $APP_USER):

  1) Reconecte como $APP_USER (root SSH esta desabilitado):
     ssh $APP_USER@$IP

  2) Edite os segredos:
     nano $COMPOSE_DIR/.env

  3) Login no GHCR + suba os containers:
     cd $COMPOSE_DIR
     echo "\$GHCR_TOKEN" | docker login ghcr.io -u SEU_USER --password-stdin
     docker compose up -d

  4) Teste:
     curl http://localhost/health

FINAL
