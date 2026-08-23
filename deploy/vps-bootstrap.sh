#!/usr/bin/env bash
set -euo pipefail

APP_USER="deploy"
APP_DIR="/opt/formaturasflow"
COMPOSE_DIR="${APP_DIR}/deploy"

log()  { printf "\033[1;34m[bootstrap]\033[0m %s\n" "$*"; }
warn() { printf "\033[1;33m[bootstrap]\033[0m %s\n" "$*"; }
fail() { printf "\033[1;31m[bootstrap]\033[0m %s\n" "$*" >&2; exit 1; }

if [[ $EUID -ne 0 ]]; then
  fail "Rode como root: sudo bash vps-bootstrap.sh"
fi

log "Atualizando o sistema (apt)..."
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get upgrade -y
apt-get install -y ca-certificates curl gnupg lsb-release ufw fail2ban unattended-upgrades

log "Habilitando atualizacoes automaticas de seguranca..."
dpkg-reconfigure -f noninteractive unattended-upgrades

if ! id -u "$APP_USER" >/dev/null 2>&1; then
  log "Criando usuario '$APP_USER'..."
  adduser --disabled-password --gecos "" "$APP_USER"
  usermod -aG sudo "$APP_USER"
fi

if [[ -d /root/.ssh ]] && [[ -f /root/.ssh/authorized_keys ]]; then
  log "Copiando authorized_keys do root para o usuario '$APP_USER'..."
  install -d -m 700 -o "$APP_USER" -g "$APP_USER" "/home/$APP_USER/.ssh"
  install -m 600 -o "$APP_USER" -g "$APP_USER" /root/.ssh/authorized_keys "/home/$APP_USER/.ssh/authorized_keys"
fi

log "Endurecendo SSH (root login off, senha off)..."
SSHD=/etc/ssh/sshd_config
sed -ri 's/^#?PermitRootLogin.*/PermitRootLogin no/' "$SSHD"
sed -ri 's/^#?PasswordAuthentication.*/PasswordAuthentication no/' "$SSHD"
sed -ri 's/^#?ChallengeResponseAuthentication.*/ChallengeResponseAuthentication no/' "$SSHD"
sed -ri 's/^#?KbdInteractiveAuthentication.*/KbdInteractiveAuthentication no/' "$SSHD"
systemctl reload ssh || systemctl reload sshd || true

log "Configurando firewall (UFW)..."
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw --force enable

log "Ativando fail2ban..."
systemctl enable --now fail2ban

if ! command -v docker >/dev/null 2>&1; then
  log "Instalando Docker Engine oficial..."
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo \
    "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
    $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -y
  apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi

usermod -aG docker "$APP_USER"

log "Preparando diretorio da aplicacao em $APP_DIR..."
install -d -m 750 -o "$APP_USER" -g "$APP_USER" "$APP_DIR"
install -d -m 750 -o "$APP_USER" -g "$APP_USER" "$COMPOSE_DIR"

if [[ ! -f "$COMPOSE_DIR/.env" ]]; then
  warn "Sem .env em $COMPOSE_DIR/.env. Copiando .env.example (edite antes de subir!)."
  if [[ -f "$COMPOSE_DIR/.env.example" ]]; then
    install -m 600 -o "$APP_USER" -g "$APP_USER" "$COMPOSE_DIR/.env.example" "$COMPOSE_DIR/.env"
  fi
fi

log "Concluido. Proximo passo (como $APP_USER):"
echo "  ssh ${APP_USER}@\$(hostname -I | awk '{print \$1}')"
echo "  cd $COMPOSE_DIR"
echo "  nano .env                         # preenche as variaveis"
echo "  docker login ghcr.io -u \$GHCR_USER -p \$GHCR_TOKEN"
echo "  docker compose pull"
echo "  docker compose up -d"
