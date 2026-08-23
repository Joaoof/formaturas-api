# FormaturasFlow.Api — Documentação Técnica

> API backend do sistema de gestão financeira para formaturas.
> Stack: **.NET 10 · ASP.NET Core Minimal APIs · Entity Framework Core · PostgreSQL · JWT · Efí Bank (PSP)**.

Este documento explica **o que cada peça faz** e **por que cada decisão foi tomada**, no nível certo para que meu parceiro consiga entrar no projeto e enxergar a lógica sem precisar ler linha por linha.

---

## 1. Visão geral do sistema

O sistema serve para uma empresa de formaturas gerenciar:

1. **Turmas** (grupos de formandos de uma instituição/curso).
2. **Alunos** dentro dessas turmas.
3. **Contratos** que cada aluno assina (valor total, entrada, número de parcelas).
4. **Parcelas** geradas automaticamente a partir do contrato.
5. **Cobranças reais** (boleto ou PIX) emitidas via **Efí Bank** para cada parcela.
6. **Webhooks** da Efí que dão baixa automática na parcela quando o cliente paga.
7. **Despesas** operacionais da empresa (mensalidades da equipe, brindes, etc.).

Existe também controle de acesso por papéis: **super_admin**, **funcionario** e **aluno**.

O fluxo canônico é:

```
Turma criada → Aluno cadastrado na turma → Contrato criado
        ↓
Parcelas geradas automaticamente
        ↓
Staff emite cobrança na Efí (boleto ou PIX)
        ↓
Aluno paga → Efí dispara webhook → Parcela vira "Pago" sozinha
```

---

## 2. Estrutura de pastas

```
src/FormaturasFlow.Api/
├── Program.cs                # Composition root: DI, middleware, mapeamento das rotas
├── appsettings*.json         # Configuração (connection string, JWT, Efí, CORS)
│
├── Auth/                     # Autenticação e autorização
│   ├── JwtOptions.cs         # POCO de config para o JWT
│   ├── JwtTokenService.cs    # Gera o access token assinado
│   └── AuthEndpoints.cs      # /auth/register, /auth/login, /auth/me
│
├── Data/                     # Camada de persistência
│   ├── ApplicationUser.cs    # Usuário Identity + roles constantes
│   └── AppDbContext.cs       # DbContext + mapeamento das tabelas
│
├── Domain/                   # Entidades de negócio (POCOs)
│   ├── Turma.cs
│   ├── Aluno.cs
│   ├── Contrato.cs
│   ├── Parcela.cs
│   ├── Despesa.cs
│   └── WebhookEvent.cs
│
├── Endpoints/                # Rotas REST agrupadas por recurso
│   ├── TurmaEndpoints.cs
│   ├── AlunoEndpoints.cs
│   ├── ContratoEndpoints.cs
│   └── ParcelaEndpoints.cs
│
├── Efi/                      # Integração com o PSP (Efí Bank)
│   ├── EfiOptions.cs         # Config (client_id, secret, cert, chave PIX...)
│   ├── EfiHttpHandler.cs     # Injeta o certificado mTLS em cada request
│   ├── EfiClient.cs          # HTTP client tipado (boleto + PIX)
│   └── EfiEndpoints.cs       # Emissão de cobrança + webhook público
│
└── Migrations/               # Migrations geradas pelo EF Core
```

A separação segue o princípio: **Domain** só tem os dados, **Data** conhece o banco, **Endpoints** conhecem HTTP, **Auth** e **Efi** são módulos verticais isolados.

---

## 3. `Program.cs` — como tudo começa

Esse é o **composition root**. Aqui a gente monta a árvore de dependências e diz para o ASP.NET quais rotas existem. Ordem lógica do arquivo:

1. **Bind de opções tipadas** (`JwtOptions`, `EfiOptions`) lendo o `appsettings.json`.
   Ganho: em qualquer lugar do código, pedir `IOptions<JwtOptions>` e receber o objeto já validado.

2. **EF Core + PostgreSQL** (Npgsql) usando a connection string `ConnectionStrings:Default`. Se não existir, o app aborta com erro claro na hora do boot.

3. **Identity + JWT**:
   - `AddIdentityCore` — força senha ≥ 8 caracteres, e-mail único, lockout depois de 5 falhas.
   - `AddJwtBearer` — o token só é aceito se **issuer, audience, validade e assinatura HMAC-SHA256** estiverem OK.

4. **HttpClient tipado da Efí** com um handler custom (`EfiHttpHandler`) que anexa o certificado mTLS. Isso é obrigatório porque a Efí não aceita chamada sem certificado.

5. **OpenAPI + Scalar** (docs interativas), **CORS** dinâmico (origens vem do `appsettings`) e **Health Check** que também checa o banco.

6. **ProblemDetails (RFC 7807)** — erros da API saem padronizados em JSON, o front consegue tratar de forma genérica.

7. Em **Development**, roda `db.Database.MigrateAsync()` automaticamente e chama `SeedRolesAsync` para garantir que os papéis existem.

8. Registra as rotas: `/auth/*` no root, o resto dentro de `/api/v1/*`.

**Por que Minimal APIs e não Controllers?**
Menos ruído, menos boilerplate e o time é pequeno. Cada arquivo `*Endpoints.cs` é uma classe estática com um método de extensão `MapXEndpoints`. Fica direto.

---

## 4. Autenticação — como o login funciona

### 4.1 `ApplicationUser.cs`
Herda de `IdentityUser<Guid>` (chave GUID em vez de string). Adicionamos `NomeCompleto` e `CriadoEm`. `Roles` é uma classe estática só com as **três strings de papel** (super_admin, funcionario, aluno) — evita erro de digitação.

### 4.2 `JwtOptions.cs`
POCO com `Issuer`, `Audience`, `Key` (segredo HMAC) e `AccessTokenMinutes`. Todos vêm do `appsettings`.

### 4.3 `JwtTokenService.cs`
Serviço `Scoped` com um único método: `CreateAccessTokenAsync(user)`.

Ele:
1. Busca os papéis do usuário via `UserManager`.
2. Monta a lista de claims: `sub` (id), `jti` (id único do token, para futura revogação), `email`, `nome` e um `role` para cada papel.
3. Assina com `HmacSha256` usando `JwtOptions.Key`.
4. Retorna `(token, expiresAt)`.

### 4.4 `AuthEndpoints.cs`
Três endpoints:

| Método | Rota | O que faz |
|---|---|---|
| POST | `/auth/register` | Cria conta e devolve JWT |
| POST | `/auth/login` | Valida senha e devolve JWT |
| GET | `/auth/me` | Retorna dados do usuário logado (precisa do token) |

**Detalhe importante do register:** se ainda **não existir nenhum usuário** no banco, o primeiro cadastro vira `super_admin` automaticamente. É o *bootstrap*: sem isso, ninguém conseguiria criar o primeiro admin sem hackear o banco.

**Detalhe importante do login:** usa `CheckPasswordSignInAsync(..., lockoutOnFailure: true)`. Isso incrementa o contador de tentativas do Identity, e depois de 5 falhas o usuário é bloqueado temporariamente — proteção contra brute force.

---

## 5. Modelo de dados

### 5.1 Diagrama lógico

```
Turma (1) ──< (N) Aluno (1) ──< (N) Contrato (1) ──< (N) Parcela
                    │
                    └─── ApplicationUser (opcional — aluno pode não ter login)

Despesa (isolada — só a empresa)
WebhookEvent (log de eventos externos para idempotência)
```

### 5.2 Explicando cada entidade

- **`Turma`** — identifica o grupo. `Nome`, `Instituicao`, `Curso`, `AnoFormatura`. Índice no nome para busca rápida.

- **`Aluno`** — pertence a uma Turma (obrigatório). O campo `UserId` é **opcional**: nem todo aluno precisa ter login criado. Se um dia criarmos a conta, é só amarrar. Cascade delete: apagar a turma apaga os alunos.

- **`Contrato`** — pertence a um Aluno. `ValorTotal`, `ValorEntrada`, `NumParcelas`, `FormaPagamento`. `TextoContrato` guarda o corpo do contrato assinado (opcional).

- **`Parcela`** — pertence a um Contrato. Tem `Numero` (ordem 1..N), `Valor`, `Vencimento`, `Status` (Pendente/Pago/Atrasado/Cancelado) e um monte de campos que amarram com o PSP:
  - `PspProvider` — quem é o PSP (`"efi"`).
  - `PspChargeId` — id da cobrança na Efí (o "txid" no PIX ou "charge_id" no boleto).
  - Campos específicos de boleto (`BoletoUrl`, `LinhaDigitavel`, `CodigoBarras`) e de PIX (`PixCopiaCola`, `PixQrCodeUrl`).
  - `LinkPagamento` — link universal para mandar pro aluno pelo WhatsApp.

  Índice único em `(ContratoId, Numero)`: impossível ter duas parcelas com o mesmo número no mesmo contrato.

- **`Despesa`** — controle financeiro interno. Não tem relacionamento com o resto.

- **`WebhookEvent`** — cada evento recebido de um PSP vira uma linha aqui. Índice único em `(Provider, EventId)` — essa é a chave da **idempotência**: se a Efí reenviar o mesmo evento, a gente ignora silenciosamente em vez de dar baixa duplicada.

### 5.3 `AppDbContext.cs`

Herda de `IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` — assim as tabelas do Identity (`AspNetUsers`, `AspNetRoles`, etc.) ficam no mesmo banco.

No `OnModelCreating`:
- Nomes das tabelas em snake_case minúsculo (padrão Postgres).
- Precisão `(12, 2)` para todos os `decimal` — evita bug de arredondamento.
- Índices em todos os FKs e campos de busca.
- Enums (`StatusParcela`, `StatusDespesa`) salvos como **string** (`HasConversion<string>()`) em vez de int — leitura direta no banco fica auto-explicativa.

---

## 6. Endpoints REST — a API de negócio

Todos os endpoints estão sob `/api/v1/*` e exigem JWT (`RequireAuthorization`). Os endpoints de escrita adicionam o filtro de role.

### 6.1 `TurmaEndpoints.cs`

| Método | Rota | Role | Descrição |
|---|---|---|---|
| GET | `/turmas` | qualquer autenticado | Lista turmas com contagem de alunos |
| GET | `/turmas/{id}` | qualquer autenticado | Detalha uma turma |
| POST | `/turmas` | super_admin / funcionario | Cria turma |
| PUT | `/turmas/{id}` | super_admin / funcionario | Atualiza turma |
| DELETE | `/turmas/{id}` | super_admin | Apaga (cascata cai nos alunos) |

Uso de `AsNoTracking()` no GET — leitura pura, não precisa do change tracker do EF, ganha performance.

### 6.2 `AlunoEndpoints.cs`

Mesmo padrão CRUD. Pontos de atenção:
- No `POST`, valida se a `TurmaId` informada existe antes de criar (evita FK órfã).
- No `GET/{id}`, faz `Include` de `Contratos` e `Parcelas` — a tela de detalhe do aluno mostra tudo aninhado.
- No `LIST`, aceita `?turmaId=<guid>` para filtrar.

### 6.3 `ContratoEndpoints.cs`

O endpoint importante é o `POST /contratos`: **cria contrato e todas as parcelas em uma transação atômica**.

Lógica passo a passo dentro de `CreateWithParcelasAsync`:

1. Valida: `NumParcelas >= 1` e aluno existe.
2. `saldo = ValorTotal − ValorEntrada`.
3. `valorParcela = round(saldo / NumParcelas, 2)`.
4. `resto = saldo − (valorParcela * NumParcelas)` — a diferença de centavos que o arredondamento gerou.
5. Abre transação (`BeginTransactionAsync`) para garantir que **ou tudo é criado, ou nada** (se der erro no meio, `SaveChangesAsync` faz rollback).
6. Insere o contrato.
7. Loop de 1 até NumParcelas gerando parcelas com vencimentos `PrimeiroVencimento + (i-1) meses`.
8. **A última parcela absorve o `resto`** — assim a soma das parcelas bate exatamente com o saldo.
9. Commit.

**Por que a última parcela absorve o resto?** Porque `round` pode fazer 3 × 33,33 = 99,99 e o saldo real é 100,00. Colocar o centavo perdido na última parcela é a convenção mais simples e evita divergência.

### 6.4 `ParcelaEndpoints.cs`

Dois endpoints além do LIST:

- **`POST /parcelas/{id}/baixar`** — dá baixa manual (staff bateu um pagamento em dinheiro/transferência):
  - `Status = Pago`
  - `ValorPago = req.ValorPago ?? Valor` (aceita pagamento parcial)
  - `DataPagamento = req.DataPagamento ?? hoje`

- **`POST /parcelas/{id}/desfazer`** — reverte a baixa (staff errou):
  - `Status = Pendente`, zera `ValorPago` e `DataPagamento`.

Filtro por `?status=pago|pendente|...` no LIST usando `Enum.TryParse`.

---

## 7. Integração com a Efí Bank (PSP)

Esse é o módulo mais denso. A Efí exige três coisas ao mesmo tempo:

1. **OAuth client credentials** (`client_id` + `client_secret`) para pegar um access token.
2. **mTLS** — certificado `.p12` do cliente em toda request.
3. **Chave PIX** cadastrada no painel deles para receber os pagamentos.

### 7.1 `EfiOptions.cs`
Config tipada com tudo: `Sandbox` (troca entre homologação e produção), credenciais, certificado em **base64** (pra caber em variável de ambiente), senha do certificado, chave PIX e o segredo do webhook. As URLs base são geradas automaticamente com base no `Sandbox`.

### 7.2 `EfiHttpHandler.cs`
Extende `HttpClientHandler`. No construtor, decodifica o base64, carrega o `.p12` como `X509Certificate2` e adiciona em `ClientCertificates`. Depois disso, **toda request feita pelo `HttpClient` que usa esse handler vai automaticamente com o certificado mTLS** — a gente não precisa lembrar disso em nenhum outro lugar.

### 7.3 `EfiClient.cs`
Cliente HTTP tipado (injetado via `AddHttpClient<EfiClient>()`). Três responsabilidades:

**a) `GetAccessTokenAsync()`** — pega token OAuth com cache local:
- Se já tem token e ainda vale por > 1 minuto, reusa.
- Senão, chama `/v1/authorize` com `Basic Auth (client_id:client_secret)` + `grant_type=client_credentials`.
- Guarda `access_token` e calcula `_tokenExpira = now + expires_in`.

Cache evita bater no `/authorize` a cada cobrança — ganha latência e reduz risco de rate limit.

**b) `CriarBoletoAsync()`** — chama `/v1/charge/one-step` da API de Cobranças:
- Monta o payload no schema real da Efí (valor em centavos, CPF só dígitos, formato de vencimento ISO).
- Se der erro HTTP, loga o body e joga `EfiException` — deixa a stack fluir para o middleware de ProblemDetails.
- Extrai `charge_id`, `barcode` (linha digitável) e `link` (URL do PDF).

**c) `CriarPixAsync()`** — chama `/v2/cob/{txid}` da API PIX (PUT porque o Banco Central definiu assim, é idempotente pelo txid):
- Payload no formato do BACEN: `calendario.expiracao`, `devedor.cpf/nome`, `valor.original`, `chave`, `solicitacaoPagador`.
- Extrai `pixCopiaECola` e `loc.location` (URL do QR Code).

### 7.4 `EfiEndpoints.cs`

**a) `POST /parcelas/{id}/cobranca` (staff)** — emite cobrança para uma parcela específica:
1. Carrega a parcela + contrato + aluno via `Include`.
2. Barra se a parcela já está **paga** (409 Conflict).
3. **Idempotência local:** se a parcela já tem `PspChargeId`, devolve `{ existente: true, parcela }` — evita cobrar o mesmo cliente duas vezes por engano do staff.
4. Dependendo de `req.Tipo`:
   - `boleto` → chama `EfiClient.CriarBoletoAsync` e guarda os campos de boleto.
   - `pix` → gera `txId` truncado em 35 chars (limite do BACEN), chama `CriarPixAsync` e guarda os campos de PIX.
5. Persiste na parcela e retorna o objeto atualizado.

**b) `POST /webhooks/efi` (público, sem JWT)** — recebe callbacks da Efí:

Esse endpoint é **público** de propósito, porque a Efí precisa alcançar sem autenticação. Segurança por:
- **Segredo compartilhado** no query string (`?secret=...`) validado contra `EfiOptions.WebhookSecret`. Se não bater, `401 Unauthorized`.
- **Idempotência**: extrai o `event_id` do payload e consulta a tabela `webhook_events` — se já foi processado, retorna `{ duplicado: true }` sem alterar nada.

Se o evento contém um array `pix`, itera cada item:
- Busca a parcela pelo `PspChargeId == txid`.
- Marca `Status = Pago`, grava `ValorPago` e `DataPagamento`.
- Marca `PspStatus = "CONCLUIDA"`.

No final, grava o `WebhookEvent` com `ProcessadoEm` preenchido e comita tudo numa única `SaveChangesAsync` — se qualquer parte falhar, nada é salvo (transição implícita do EF).

**Por que idempotência?** PSPs reenviam webhooks quando não recebem `2xx` a tempo. Sem idempotência, um único pagamento poderia dar baixa 3× e bagunçar o histórico financeiro.

---

## 8. Segurança — resumo

| Camada | O que protege |
|---|---|
| JWT HMAC-SHA256 com issuer/audience validados | Autenticação do front |
| Roles (`super_admin`, `funcionario`, `aluno`) | Autorização por endpoint |
| `RequireUniqueEmail = true` + `MinLength = 8` | Higiene de conta |
| `Lockout` após 5 falhas | Brute force |
| HTTPS Redirection + CORS whitelisted | Transporte |
| mTLS com certificado `.p12` | Comunicação com Efí |
| Segredo no query string + validação de idempotência | Webhook público |
| ProblemDetails (RFC 7807) | Não vazar stack trace |

---

## 9. Configuração (`appsettings.Development.json`)

Seções obrigatórias:

- **`ConnectionStrings.Default`** — string Postgres.
- **`Jwt`** — `Issuer`, `Audience`, `Key` (≥ 32 bytes) e `AccessTokenMinutes`.
- **`Efi`** — sandbox flag, credenciais, certificado base64, chave PIX, `WebhookSecret`.
- **`Cors.Origins`** — lista de origens permitidas (front local: `http://localhost:5173`, etc.).

Em produção, **nada disso vai em arquivo** — vai em variável de ambiente. O binding `Configure<T>` continua funcionando igual.

---

## 10. Como rodar localmente

```powershell
# 1. Sobe o Postgres (Docker, WSL ou nativo). Banco: formaturas_dev.

# 2. Restaura pacotes
dotnet restore

# 3. Roda — as migrations são aplicadas automaticamente em Development
dotnet run --project src/FormaturasFlow.Api

# 4. Abre a doc interativa
# http://localhost:{porta}/scalar
```

O primeiro cadastro em `/auth/register` vira `super_admin` — depois disso, entra por `/auth/login` e passa o `Authorization: Bearer <token>` nas próximas chamadas.

---

## 11. Decisões arquiteturais em uma linha cada

- **Minimal APIs** em vez de Controllers — menos boilerplate, time pequeno.
- **Módulos verticais** (`Auth/`, `Efi/`) em vez de por camada — cada módulo é auto-contido, fácil de mover.
- **DTOs `record`** — imutáveis, geram equals/hash de graça, deixam o payload explícito.
- **`AsNoTracking` em reads** — ganha performance, evita bugs de mutação acidental.
- **Transação explícita no contrato** — geração de parcelas é atômica.
- **Cache de token OAuth** com margem de 1 min — evita chamadas desnecessárias e race condition com expiração.
- **Idempotência dupla** (local no `PspChargeId` + tabela `webhook_events`) — cobre erro de operação e reenvio do PSP.
- **Roles em `const string`** — impossível errar o nome do role.
- **Enum como string no banco** — leitura direta faz sentido, evita “o que é status = 2?”.

---

## 12. Próximos passos (backlog conhecido)

- [ ] Refresh token com rotação (hoje só access token).
- [ ] Job em background para marcar parcelas vencidas como `Atrasado`.
- [ ] Endpoint de dashboard financeiro (soma paga/pendente/atrasada por período).
- [ ] Envio automático do link de pagamento por WhatsApp/e-mail.
- [ ] Substituir validação HMAC simples do webhook por assinatura do payload (quando a Efí liberar).
- [ ] Testes de integração cobrindo o fluxo completo (Testcontainers com Postgres).
