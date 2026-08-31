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

## Ambiente de homologação (na mesma VPS)

Homologação existe para testar código que **ainda não entrou em `main`** — por isso ela não usa GHCR nem Watchtower: builda a branch direto no host.

```
producao                          homologacao
─────────────────────────────     ─────────────────────────────
ghcr.io/:latest via Watchtower    build local da branch
porta 80                          porta 8081
formaturas-postgres               formaturas-hml-postgres
volume postgres_data              volume postgres_hml_data
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Homologacao
watchtower.enable=true            watchtower.enable=false
```

Projeto, rede, volume, porta e banco são separados: derrubar e recriar a homologação não encosta no dado de produção, e o Watchtower nunca atualiza os containers de hml.

### Primeira vez

```bash
sudo ufw allow 8081/tcp                     # UFW só libera 22 e 80 por padrão

sudo -iu deploy
git clone https://github.com/Joaoof/formaturas-api.git ~/formaturasflow-hml
cd ~/formaturasflow-hml
cp deploy/.env.hml.example deploy/.env.hml
nano deploy/.env.hml                        # POSTGRES_PASSWORD e JWT_KEY são obrigatórios
bash deploy/hml-up.sh main
```

### Atualizar / trocar de branch

```bash
sudo -iu deploy
bash ~/formaturasflow-hml/deploy/hml-up.sh hml/roteamento-pagamentos
```

O script faz `fetch` + `reset --hard` na branch pedida, rebuilda a imagem, sobe a stack e espera o healthcheck. Se não ficar saudável em 2 min, ele despeja o log da API e sai com erro.

### Verificar

```bash
curl -fsS http://localhost:8081/health
curl -fsS http://localhost:8081/ | jq        # environment deve ser "Homologacao"
docker compose -f deploy/docker-compose.hml.yml --env-file deploy/.env.hml ps
```

### Derrubar

```bash
cd ~/formaturasflow-hml
docker compose -f deploy/docker-compose.hml.yml --env-file deploy/.env.hml down          # mantém o banco
docker compose -f deploy/docker-compose.hml.yml --env-file deploy/.env.hml down -v       # zera o banco de hml
```

### Credenciais de PSP em homologação

`Asaas__Sandbox` e `Cora__Sandbox` estão **fixados em `true`** no compose de hml — não vêm do `.env`. Mesmo colando uma chave de produção por engano, o ambiente continua batendo em `api-sandbox.asaas.com` e `matls-clients.api.stage.cora.com.br`. Sem credencial preenchida, as rotas de cobrança respondem `502 GATEWAY_INDISPONIVEL`, e o roteamento (422 de domínio × método) continua testável.

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
