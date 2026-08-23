# Deploy — FormaturasFlow.Api

Objetivo: **push no `main` do GitHub → em ~1 min a VPS já está rodando a nova versão sem downtime** e sem SSH no pipeline.

## Como funciona

```
Você commita em main
        │
        ▼
GitHub Actions (.github/workflows/build-and-push.yml)
    · builda imagem multi-stage
    · publica em ghcr.io/<owner>/<repo>:latest (+ tag :sha)
        │
        ▼
Watchtower na VPS (poll de 60s)
    · vê a nova digest no GHCR
    · pull da imagem nova
    · sobe container novo
    · derruba o velho (rolling restart)
        │
        ▼
API atualizada, banco intocado, zero SSH manual
```

## Por que essa stack

- **GHCR (GitHub Container Registry)** — grátis, autenticação com `GITHUB_TOKEN`, integrado ao repo.
- **Watchtower** — daemon simples, faz polling do registry e rolling restart. Sem plugin, sem Kubernetes.
- **Rolling restart** — cria container novo antes de matar o velho: usuário conectado quase não percebe.
- **Postgres em container separado** com volume persistente e `com.centurylinklabs.watchtower.enable: "false"` para **nunca ser atualizado automaticamente** (banco só sobe versão em janela controlada).
- **Sem porta pública no Postgres** — a network `internal` é `internal: true`, o banco só é alcançável pela API.
- **Usuário não-root no container** da API (`USER app`).
- **Healthchecks** em API e Postgres — se a API não passar em healthcheck, o Watchtower não considera o deploy bem-sucedido.

## Pré-requisitos

1. Repositório no GitHub (owner + nome).
2. **Personal Access Token** com escopo `read:packages` — o Watchtower usa isso para puxar do GHCR privado.
   - Se o pacote for público, `GHCR_TOKEN` pode ficar vazio e o Watchtower puxa sem auth.
3. VPS Ubuntu 24.04 com acesso root.

## Passo a passo (primeira vez)

### 1) Rotacionar credenciais e trocar login por senha por chave SSH

Na sua máquina Windows:

```powershell
ssh-keygen -t ed25519 -C "formaturasflow-vps"
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@191.101.78.252 "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys"
ssh root@191.101.78.252 "passwd"
```

### 2) Rodar o bootstrap na VPS

```bash
ssh root@191.101.78.252
apt-get update && apt-get install -y git
git clone https://github.com/<owner>/formaturas-api.git /opt/formaturasflow
cd /opt/formaturasflow
bash deploy/vps-bootstrap.sh
```

O script instala Docker, configura UFW/fail2ban, cria usuário `deploy`, desativa senha e root no SSH.

### 3) Configurar `.env` de produção

```bash
sudo -iu deploy
cd /opt/formaturasflow/deploy
cp .env.example .env
nano .env
```

Preencha tudo. **Nunca commite o `.env`** (o `.gitignore` já bloqueia).

### 4) Autenticar no GHCR e subir

```bash
docker login ghcr.io -u <seu-usuario> -p <PAT-com-read-packages>
docker compose pull
docker compose up -d
```

A partir daqui, cada `git push` no `main` faz o CI publicar `:latest`, o Watchtower detecta em até 60s e faz rolling update sozinho.

## Verificar deploy

```bash
docker compose ps                    # todos "healthy"?
docker compose logs -f api           # startup + migrations aplicadas
curl -fsS http://localhost/health    # deve retornar 200
docker logs formaturas-watchtower    # confirma polling
```

## Rollback

```bash
export API_TAG=sha-abc1234
docker compose up -d api
```

Cada build gera tag `sha-<7chars>` — basta apontar o compose para uma tag antiga e subir. Para congelar (parar auto-update):

```bash
docker compose down
sed -i 's/watchtower.enable: "true"/watchtower.enable: "false"/' docker-compose.yml
docker compose up -d
```

## Backup do Postgres

```bash
docker exec formaturas-postgres pg_dump -U $POSTGRES_USER $POSTGRES_DB \
  | gzip > /opt/formaturasflow/backups/$(date +%F_%H%M).sql.gz
```

Sugestão: cron diário às 3h.

## Segurança que já está aplicada

- SSH: root off, senha off, só chave.
- UFW aberto só em 22 e 80.
- fail2ban ativo.
- unattended-upgrades para patches de kernel/openssl.
- Postgres em rede `internal` (sem porta pública).
- API rodando como UID 1000 dentro do container.
- Segredos só em `.env` fora do repositório.
- Watchtower autentica no GHCR com token dedicado.
- SBOM + provenance attestation em cada imagem publicada (`build-and-push.yml`).

## O que ainda falta (backlog DevOps)

- [ ] Domínio + Traefik/Caddy + Let's Encrypt (HTTPS).
- [ ] Backups automáticos empurrados para S3/Backblaze.
- [ ] Métricas — Prometheus + Grafana ou Uptime Kuma.
- [ ] Alertas do Watchtower em Slack/Discord (`WATCHTOWER_NOTIFICATIONS`).
- [ ] Blue/green real com Traefik se o SLA exigir zero downtime absoluto.
