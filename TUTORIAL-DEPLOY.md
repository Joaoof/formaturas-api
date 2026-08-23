# Tutorial passo a passo — Subir a API .NET 10 numa VPS Ubuntu com CI/CD automático

> Este é o passo a passo real e detalhado que usei para colocar o **FormaturasFlow.Api** no ar em `http://191.101.78.252`, com deploy automático toda vez que a gente dá `git push`.
>
> Pra alguém que nunca subiu API em VPS: **leia do começo ao fim**. Cada passo explica **o que faz** e **por que faz**.

---

## O que você vai construir

```
Seu terminal Windows
        │  git push origin main
        ▼
┌────────────────────────┐
│   GitHub (repo)        │  Actions builda a imagem Docker (~1 min)
└──────────┬─────────────┘
           │  publica em ghcr.io/seu-user/sua-api:latest
           ▼
┌────────────────────────┐
│   GitHub Container     │  imagem Docker versionada
│   Registry (GHCR)      │
└──────────┬─────────────┘
           │  Watchtower na VPS puxa em ≤60 seg
           ▼
┌────────────────────────┐
│   VPS Ubuntu 24.04     │  Docker + API + Postgres
│   191.101.78.252       │
└────────────────────────┘
```

**Total de tempo estimado:** ~90 minutos numa primeira vez.

**Custo mensal:** VPS (~R$25-40), domínio opcional (~R$40/ano). GHCR e Actions são grátis pra repositórios privados pequenos.

---

## Pré-requisitos

1. **VPS Ubuntu 24.04 LTS** com acesso root e senha inicial
   - Hostinger, Contabo, Vultr, DigitalOcean, Hetzner — qualquer um serve
   - Anota o IP público e a senha do root
2. **Conta no GitHub** (grátis)
3. **Windows 10/11 com PowerShell** (ou Linux/Mac — os comandos são quase iguais)
4. **Git** instalado no Windows: https://git-scm.com/download/win
5. **GitHub CLI (`gh`)** instalado: `winget install GitHub.cli`

---

## Parte 1 — Preparar sua máquina Windows

### 1.1 Gerar uma chave SSH (se ainda não tem)

Chave SSH é a "senha" que você vai usar pra logar na VPS. Diferente de senha comum, ela é criptograficamente segura e ninguém consegue chutar por força bruta.

Abre o PowerShell e roda:

```powershell
ssh-keygen -t ed25519 -C "meu-nome-vps" -f $env:USERPROFILE\.ssh\id_ed25519
```

- `-t ed25519` → algoritmo moderno (mais seguro que RSA e mais rápido)
- `-C` → comentário livre para identificar a chave depois
- `-f` → onde salvar o par de chaves
- **Deixa a passphrase em branco** (aperta Enter duas vezes) — para automação funcionar

Isso cria dois arquivos:
- `~/.ssh/id_ed25519` → **chave privada** (NUNCA compartilha, é sua senha)
- `~/.ssh/id_ed25519.pub` → **chave pública** (essa você compartilha)

Ver a chave pública:

```powershell
Get-Content $env:USERPROFILE\.ssh\id_ed25519.pub
```

Vai aparecer algo tipo:
```
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIF6JO/4IJvMILvVgdo+EscqRWXfsFzM+Fz35jOruQtTA meu-nome-vps
```

Copia essa linha inteira, você vai usar já já.

### 1.2 Fazer login no GitHub via CLI

```powershell
gh auth login
```

Escolhe:
1. `GitHub.com`
2. `HTTPS`
3. `Y` (autenticar Git)
4. `Login with a web browser`

Copia o código que aparece (tipo `ABCD-1234`), abre o navegador em https://github.com/login/device, cola, autoriza. Volta pro terminal e vê `✓ Logged in`.

---

## Parte 2 — Preparar o repositório GitHub

### 2.1 Criar o repositório

Se ainda não existe:

```powershell
cd C:\caminho\do\seu\projeto
git init
git add .
git commit -m "primeiro commit"
gh repo create sua-api --private --source=. --remote=origin --push
```

Se já existe, garante que o `origin` está configurado:

```powershell
git remote -v
# deve mostrar https://github.com/seu-user/sua-api.git
```

### 2.2 Criar os arquivos de Docker

**Dockerfile** (raiz do projeto):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SuaApi/SuaApi.csproj src/SuaApi/
RUN dotnet restore src/SuaApi/SuaApi.csproj

COPY src/ src/
RUN dotnet publish src/SuaApi/SuaApi.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=app:app /app/publish .
USER app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "SuaApi.dll"]
```

> **Detalhe importante**: a imagem `aspnet:10.0` **já vem com um usuário `app`** de fábrica. Não crie ele de novo com `groupadd/useradd` — vai dar erro "group 'app' already exists" e o build morre.

**`.dockerignore`** (raiz):

```
**/bin
**/obj
**/.git
**/appsettings.Development.json
**/.env
```

### 2.3 Criar `deploy/docker-compose.yml`

Esse arquivo vai orquestrar os três containers na VPS: API + Postgres + Watchtower.

```yaml
name: minhastack

networks:
  edge:
  internal:
    internal: true    # postgres não escuta na internet, só na rede interna

volumes:
  postgres_data:

services:
  postgres:
    image: postgres:16-alpine
    restart: unless-stopped
    networks: [internal]
    environment:
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}"]
      interval: 10s
    labels:
      com.centurylinklabs.watchtower.enable: "false"   # nunca auto-atualiza o DB

  api:
    image: ghcr.io/${GHCR_OWNER}/${GHCR_IMAGE}:${API_TAG:-latest}
    restart: unless-stopped
    depends_on:
      postgres: { condition: service_healthy }
    networks: [edge, internal]
    ports:
      - "80:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: "Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
      # ... outras variáveis do app
    healthcheck:
      test: ["CMD", "curl", "-fsS", "http://localhost:8080/health"]
      interval: 30s
    labels:
      com.centurylinklabs.watchtower.enable: "true"    # esse SIM auto-atualiza

  watchtower:
    image: nickfedor/watchtower:latest
    restart: unless-stopped
    environment:
      WATCHTOWER_LABEL_ENABLE: "true"
      WATCHTOWER_CLEANUP: "true"
      WATCHTOWER_POLL_INTERVAL: "60"
      REPO_USER: ${GHCR_USER}
      REPO_PASS: ${GHCR_TOKEN}
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
```

> **Cuidado com o Watchtower**: a imagem original `containrrr/watchtower` está **abandonada** (última release 2023) e usa uma API do Docker antiga que já não funciona no Docker moderno. Use o fork **`nickfedor/watchtower`** que é ativamente mantido.

### 2.4 Criar `deploy/.env.example`

Esse arquivo é um **modelo** do `.env` que vai ficar na VPS. Você commita o `.example`; o `.env` real (com senhas) fica no `.gitignore`.

```env
GHCR_OWNER=seu-user-github
GHCR_IMAGE=sua-api
API_TAG=latest

GHCR_USER=seu-user-github
GHCR_TOKEN=ghp_troque_por_um_PAT_de_verdade

POSTGRES_DB=minha_db
POSTGRES_USER=minha_user
POSTGRES_PASSWORD=troque_por_uma_senha_forte
```

### 2.5 Criar o workflow do CI — `.github/workflows/build-and-push.yml`

Esse é o coração da automação: toda vez que você faz `git push` na `main`, esse workflow **builda a imagem Docker** e **publica no GHCR**.

```yaml
name: build-and-push

on:
  push:
    branches: [main]
  workflow_dispatch:

env:
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}

jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=ref,event=branch
            type=sha,format=short
            type=raw,value=latest,enable={{is_default_branch}}

      - name: Build and push
        uses: docker/build-push-action@v6
        with:
          context: .
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

Faz `git add`, `git commit -m "add ci/cd"`, `git push`.

**Verifica no GitHub** que o Actions rodou e a imagem apareceu em https://github.com/seu-user?tab=packages.

---

## Parte 3 — Primeira conexão na VPS (ainda com senha)

### 3.1 Conectar pela primeira vez (usando a senha que o provedor te deu)

```powershell
ssh root@SEU_IP_AQUI
```

Cola a senha. Vai entrar como `root`.

### 3.2 Instalar sua chave pública no `authorized_keys`

Ainda dentro da VPS, cola:

```bash
mkdir -p ~/.ssh && chmod 700 ~/.ssh
cat >> ~/.ssh/authorized_keys << 'EOF'
COLA_AQUI_A_LINHA_DA_SUA_CHAVE_PUBLICA
EOF
chmod 600 ~/.ssh/authorized_keys
```

### 3.3 Trocar a senha do root (a inicial provavelmente está no e-mail do provedor)

```bash
passwd
```

Escolhe uma senha nova bem forte, **guarda no gerenciador de senhas**.

### 3.4 Sair e testar login por chave

```bash
exit
```

Do seu PowerShell:

```powershell
ssh root@SEU_IP -o PasswordAuthentication=no
```

Se entrou **sem pedir senha**, sua chave está funcionando. ✅

Se pediu senha, alguma coisa deu errado — repete o passo 3.2 e confere que `~/.ssh/authorized_keys` está com a chave certa.

---

## Parte 4 — Hardening da VPS (script de bootstrap)

Cria um script `deploy/vps-bootstrap.sh` no repo que vai fazer TODO o setup da VPS de uma vez só. Depois é só rodar `bash vps-bootstrap.sh` como root na VPS.

O que ele faz:
1. Atualiza o sistema (`apt update && upgrade`)
2. Habilita atualizações automáticas de segurança (`unattended-upgrades`)
3. Cria um usuário `deploy` (não-root) para rodar os containers
4. Copia sua chave SSH do root pro `deploy`
5. Instala Docker Engine oficial (não o do apt padrão, que é antigo)
6. Adiciona `deploy` ao grupo `docker` (pra rodar sem `sudo`)
7. Configura firewall UFW (só libera 22 SSH e 80 HTTP)
8. Ativa fail2ban (banir IPs que tentam brute force)
9. **Desabilita login SSH como root** e **desabilita autenticação por senha** — de agora em diante só entra por chave

O script inteiro está em `deploy/vps-bootstrap.sh` neste repo — leia lá pra entender cada comando.

### 4.1 Copiar o script pra VPS e rodar

Do seu Windows (mais fácil se você clona o repo direto na VPS):

```bash
# na VPS como root
apt-get install -y git
git clone https://github.com/seu-user/sua-api.git /opt/sua-api
cd /opt/sua-api
bash deploy/vps-bootstrap.sh
```

Quando o script terminar, **teu login como `root` vai ser desabilitado**. A partir daqui você entra como `deploy`:

```powershell
ssh deploy@SEU_IP
```

Deve entrar sem senha (a chave foi copiada).

---

## Parte 5 — Deploy Key (para a VPS puxar código do GitHub sem PAT)

O jeito ruim: colocar seu PAT no `git config` da VPS. Se a VPS for comprometida, seu PAT vaza.

O jeito bom: **Deploy Key** — uma chave SSH gerada NA VPS, cadastrada NO REPO com permissão só-leitura. Se a VPS for comprometida, você apaga essa chave e problema resolvido.

### 5.1 Gerar deploy key NA VPS (como usuário deploy)

```bash
ssh-keygen -t ed25519 -N '' -f ~/.ssh/deploy_key -C deploy@vps
cat ~/.ssh/deploy_key.pub
```

Copia a linha `ssh-ed25519 ...` que apareceu.

### 5.2 Cadastrar a deploy key no repo

Do seu Windows:

```powershell
gh api -X POST "repos/seu-user/sua-api/keys" `
  -f title=vps-1 `
  -f key="COLA_AQUI_A_PUBLIC_DA_DEPLOY_KEY" `
  -F read_only=true
```

### 5.3 Configurar o SSH da VPS pra usar essa chave com o GitHub

Na VPS como `deploy`:

```bash
cat > ~/.ssh/config << 'EOF'
Host github-suaapi
  HostName github.com
  User git
  IdentityFile /home/deploy/.ssh/deploy_key
  IdentitiesOnly yes
  StrictHostKeyChecking accept-new
EOF
chmod 600 ~/.ssh/config
```

### 5.4 Clonar o repo (agora sim, como deploy, sem PAT)

```bash
git clone github-suaapi:seu-user/sua-api.git /home/deploy/sua-api
```

---

## Parte 6 — Configurar `.env` e subir os containers

### 6.1 Criar `deploy/.env` a partir do exemplo

```bash
cd /home/deploy/sua-api/deploy
cp .env.example .env
nano .env
```

Preenche cada variável:
- `POSTGRES_PASSWORD` → senha forte (32+ chars)
- `GHCR_TOKEN` → um PAT novo, com escopo **APENAS `read:packages`**
  - Cria em https://github.com/settings/tokens/new → escopo `read:packages` → generate
- `POSTGRES_DB`, `POSTGRES_USER` → como você quiser
- Segredos do seu app (JWT key, chaves de terceiros, etc.)

Salva com `Ctrl+O`, `Enter`, `Ctrl+X`. Blinda o arquivo:

```bash
chmod 600 .env
```

### 6.2 Fazer login no GHCR na VPS

```bash
echo "SEU_PAT_AQUI" | docker login ghcr.io -u seu-user --password-stdin
```

### 6.3 Subir a stack

```bash
docker compose pull
docker compose up -d
```

Aguarda uns 20 segundos e confere:

```bash
docker compose ps
```

Deve mostrar 3 containers **`healthy`**.

### 6.4 Testar

```bash
curl http://localhost/health
# Healthy
```

Do seu Windows:

```powershell
curl http://SEU_IP/health
# Healthy
```

🎉 **API no ar!**

---

## Parte 7 — Como o deploy automático funciona daqui pra frente

Agora você **nunca mais precisa acessar a VPS pra atualizar código**. O fluxo é:

1. Edita código no seu Windows
2. `git add . && git commit -m "..." && git push origin main`
3. GitHub Actions builda a imagem nova (~1-2min) e publica no GHCR como `latest`
4. Watchtower na VPS faz polling a cada 60 segundos, detecta digest nova
5. Watchtower puxa a imagem, para o container antigo, sobe o novo
6. Total: código no ar em ~2-3 minutos, sem downtime perceptível

**Você acompanha assim:**

```powershell
# Ver o status do CI
gh run list --limit 5

# Assistir o log em tempo real
gh run watch

# Ver que o Watchtower atualizou (na VPS)
ssh deploy@SEU_IP "docker logs formaturas-watchtower --tail 20"

# Confirmar a versão nova
curl http://SEU_IP/
```

---

## Parte 8 — Erros comuns que aconteceram comigo (e como resolvi)

### "Group 'app' already exists" no build do Docker
A imagem `aspnet:10.0` já vem com o usuário `app`. Não crie ele de novo com `groupadd`. Use direto `USER app` no Dockerfile.

### Watchtower em restart loop com "client version 1.25 is too old"
A imagem `containrrr/watchtower` está abandonada. Use `nickfedor/watchtower:latest`.

### `WATCHTOWER_ROLLING_RESTART: "true"` faz o Watchtower falhar
Rolling restart não funciona quando o container tem `depends_on`. Remove essa variável — deploy padrão é bom o suficiente.

### `Rolling restart compatibility validation failed`
Mesmo problema acima.

### `relation "AspNetUsers" does not exist` no primeiro register
As migrations não rodaram. No `Program.cs`, tire o `if (app.Environment.IsDevelopment())` do redor de `db.Database.MigrateAsync()` para migrar em todos os ambientes:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

### `/scalar` retorna 404 em produção
Mesma coisa: o `MapScalarApiReference()` estava dentro de `if (IsDevelopment())`. Tira o `if`.

### Fail2ban baniu meu IP
Se você tentar login como root **depois** que rodou o bootstrap (que desabilita root), o fail2ban vai te ver como brute-force. **Padrão do ban é 10 minutos** — vai jogar tênis e volta.

### `dubious ownership in repository`
Se você clonou o repo como `root` e depois tentou usar como `deploy`, git dá esse erro. Solução: clonar de novo já como `deploy` num diretório onde ele tem `write` (tipo `/home/deploy/`).

### Segredos vazando (GitGuardian)
Placeholders em `appsettings.Development.json` tipo `"Password=postgres"` e strings de teste em `tests/*.cs` são flagados como "generic password" pelo scanner. Solução:
1. **Nunca commite `appsettings.Development.json`** — só o `.example`
2. Cria um `.gitleaks.toml` com `allowlist` para paths `tests/**`

---

## Checklist final antes de considerar "pronto para o mundo"

- [ ] Domínio apontando pra VPS (evita HTTP puro)
- [ ] HTTPS via Caddy ou Traefik com Let's Encrypt
- [ ] Backup automático do Postgres (cron + push pra S3/Backblaze)
- [ ] Rate limiting em `/auth/*` (evita abuso)
- [ ] Rotação de segredos: PAT dedicado com só `read:packages`, senha do banco forte
- [ ] Monitoramento: Uptime Kuma ou pinger externo
- [ ] Log estruturado (Serilog) e envio pra Grafana Loki / Seq

---

## Custos reais deste setup

| Item | Custo |
|---|---|
| VPS Hostinger 4GB | ~R$25-40/mês |
| Domínio `.com.br` no Registro.br | ~R$40/ano |
| GitHub Actions (repo privado) | 2000 min/mês grátis |
| GHCR (repo privado) | 500 MB grátis |
| Backblaze B2 (backup) | R$1-2/mês |
| **Total** | ~R$30/mês |

Rodei tudo dentro dessa faixa. Sem stress.

---

## Referências

- [Docker docs](https://docs.docker.com/)
- [ASP.NET Core no Linux](https://learn.microsoft.com/aspnet/core/host-and-deploy/linux)
- [Watchtower (fork mantido)](https://github.com/nickfedor/watchtower)
- [GitHub Container Registry](https://docs.github.com/packages/working-with-a-github-packages-registry/working-with-the-container-registry)
- [Ubuntu Server Guide](https://ubuntu.com/server/docs)

---

Boa sorte pro teu amigo! Qualquer dúvida, abre uma issue no repo.
