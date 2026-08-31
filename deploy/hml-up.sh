#!/usr/bin/env bash
set -euo pipefail

# =====================================================================
# hml-up.sh — sobe/atualiza o ambiente de HOMOLOGACAO na VPS
#
# Roda como o usuario `deploy`, nao como root:
#   bash hml-up.sh [branch]      (default: main)
#
# Homologacao NAO usa GHCR nem Watchtower: builda a branch pedida no
# proprio host. Assim da para testar codigo que ainda nao entrou em
# main sem arriscar a imagem :latest que producao consome.
# =====================================================================

BRANCH="${1:-main}"
REPO="${HML_REPO:-https://github.com/Joaoof/formaturas-api.git}"
DIR="${HML_DIR:-$HOME/formaturasflow-hml}"
PORTA="${HML_PORT:-8081}"
COMPOSE="deploy/docker-compose.hml.yml"
ENVFILE="deploy/.env.hml"

log()  { printf "\033[1;36m[hml]\033[0m %s\n" "$*"; }
warn() { printf "\033[1;33m[hml]\033[0m %s\n" "$*"; }
fail() { printf "\033[1;31m[hml]\033[0m %s\n" "$*" >&2; exit 1; }

command -v docker >/dev/null 2>&1 || fail "Docker nao encontrado. Rode o deploy/install.sh antes."

# --- 1) codigo ---
if [[ -d "$DIR/.git" ]]; then
  log "Atualizando repositorio em $DIR (branch $BRANCH)..."
  git -C "$DIR" fetch --prune origin
  git -C "$DIR" checkout -B "$BRANCH" "origin/$BRANCH"
  git -C "$DIR" reset --hard "origin/$BRANCH"
else
  log "Clonando $REPO em $DIR (branch $BRANCH)..."
  git clone --branch "$BRANCH" "$REPO" "$DIR"
fi

cd "$DIR"
log "Commit alvo: $(git rev-parse --short HEAD) — $(git log -1 --pretty=%s)"

# --- 2) segredos ---
if [[ ! -f "$ENVFILE" ]]; then
  cp deploy/.env.hml.example "$ENVFILE"
  fail "Criei $DIR/$ENVFILE a partir do exemplo. Preencha POSTGRES_PASSWORD e JWT_KEY e rode de novo."
fi

# --- 3) build + up ---
log "Buildando e subindo a stack de homologacao..."
docker compose -f "$COMPOSE" --env-file "$ENVFILE" up -d --build --remove-orphans

# --- 4) health ---
log "Aguardando health em http://localhost:$PORTA/health ..."
for _ in $(seq 1 40); do
  sleep 3
  if curl -fsS -m 3 "http://localhost:$PORTA/health" >/dev/null 2>&1; then
    log "API de homologacao no ar."
    curl -fsS "http://localhost:$PORTA/" && echo
    log "Metodos por dominio (exige token, deve responder 401 aqui):"
    curl -s -o /dev/null -w '  GET /api/v1/pagamentos/metodos/Casamento -> %{http_code}\n' \
      "http://localhost:$PORTA/api/v1/pagamentos/metodos/Casamento"
    exit 0
  fi
done

warn "Nao ficou saudavel em 2min. Ultimas linhas do log:"
docker compose -f "$COMPOSE" --env-file "$ENVFILE" logs --tail 40 api
exit 1
