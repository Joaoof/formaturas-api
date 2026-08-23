# Subir uma API .NET numa VPS Ubuntu com auto-deploy

De `git push` no seu Windows até a API atualizada no ar em <2 minutos, sem tocar na VPS.

## O que você precisa

- **VPS** Ubuntu 24.04 com senha do root (~R$25/mês em qualquer provedor)
- **Conta no GitHub** com repositório do projeto criado

---

## Só 5 passos

### 1️⃣ Gerar chave SSH no Windows e enviar pra VPS

```powershell
ssh-keygen -t ed25519 -f $env:USERPROFILE\.ssh\id_ed25519 -N '""'
type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@SEU_IP "mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys"
```

Depois disso, `ssh root@SEU_IP` entra sem senha.

### 2️⃣ Rodar UM script na VPS que instala tudo

No PowerShell, **dentro da pasta do projeto** que você clonou localmente:

```powershell
Get-Content deploy/install.sh | ssh root@SEU_IP "bash"
```

Esse script instala Docker, firewall, cria usuário `deploy`, escreve o `docker-compose.yml` e `.env.example`, e desabilita SSH por senha. Leva ~2 minutos.

### 3️⃣ Preencher os segredos

```powershell
ssh deploy@SEU_IP
nano /home/deploy/app/deploy/.env
```

Edita os 3 valores obrigatórios:

```env
POSTGRES_PASSWORD=escolha_uma_senha_forte
JWT_KEY=escolha_uma_chave_de_32_bytes
GHCR_TOKEN=cole_seu_github_token
```

O `GHCR_TOKEN` você cria em https://github.com/settings/tokens/new — marca **só** o escopo `read:packages` e copia o `ghp_...`.

### 4️⃣ Ligar

Ainda como `deploy`:

```bash
cd /home/deploy/app/deploy
echo "$GHCR_TOKEN" | docker login ghcr.io -u SEU_USER_GITHUB --password-stdin
docker compose up -d
```

Aguarda 30s e testa:

```bash
curl http://localhost/health
# Healthy ✅
```

### 5️⃣ Auto-deploy funcionando

Do seu Windows, edita algum arquivo, `git commit`, `git push origin main`.

Em ~2 minutos a nova versão sobe sozinha. Você **nunca mais precisa acessar a VPS**.

---

## Como saber que está funcionando

```
GET http://SEU_IP/           → info da API
GET http://SEU_IP/health     → Healthy
GET http://SEU_IP/scalar/v1  → documentação clicável
```

## Como olhar o que está acontecendo

```bash
ssh deploy@SEU_IP

# Ver os containers
cd /home/deploy/app/deploy && docker compose ps

# Ver log da API
docker logs -f formaturas-api

# Ver o Watchtower puxando update
docker logs -f formaturas-watchtower
```

---

## Deu problema?

- **Não conecta na VPS por chave** → chave pública não foi pra `authorized_keys`. Refaz o passo 1.
- **`docker login` falha** → PAT errado ou sem `read:packages`. Gera outro.
- **API não sobe** → `docker logs formaturas-api` mostra o erro real. 90% dos casos é `.env` com valor faltando.
- **`/scalar` dá 404** → normal. É `/scalar/v1`.
