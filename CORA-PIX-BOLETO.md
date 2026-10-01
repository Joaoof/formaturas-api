# Cora — Pix e boleto

Integração Direta da Cora, usada pelo domínio **Formatura**. Emite **boleto** e
**Pix**; cartão de crédito a Cora não processa (esse fica no Asaas, domínio
Casamento).

---

## 1. Credencial

A Cora só emite credencial pela **Cora Web** — não existe endpoint de API para
isso.

1. Entre em <https://app.cora.com.br/>
2. Menu **Conta → Integrações via APIs → Integração Direta**
3. Escolha o ambiente (**Stage** ou **Produção**)
4. Clique em **Gerar uma nova credencial**

O download traz dois arquivos:

```
certificate.pem     → certificado público
private-key.key     → chave privada (RSA)
```

> **Limite:** 2 credenciais por ambiente a cada 12 meses. Da terceira em diante,
> é preciso pedir ao time técnico da Cora por e-mail. Guarde os arquivos: um
> download perdido custa uma das duas emissões do ano.

### Não existe `ClientId` para copiar

O `client_id` do fluxo OAuth é o **CN do próprio certificado**
(`CN=int-XXXXXXXXXXXX`). A aplicação lê o CN sozinha, então
`Cora:ClientId` fica **vazio** no appsettings.

Isso é proposital: elimina a classe de erro em que o `.pem` de produção sobe
junto com o `client_id` de stage, que falha só na hora de cobrar de verdade.

Para conferir qual id está no certificado:

```bash
openssl x509 -in certificate.pem -noout -subject
```

---

## 2. Configuração

Caminho em disco é o formato preferido:

```json
"Cora": {
  "Sandbox": true,
  "ClientId": "",
  "CertificatePemPath": "certs/cora/stage/certificate.pem",
  "PrivateKeyPemPath": "certs/cora/stage/private-key.key",
  "WebhookSecret": "<um segredo forte>",
  "WebhookUrlPublica": "https://api.exemplo.com.br"
}
```

`Sandbox` decide o host, e só isso:

| Sandbox | Host |
|---|---|
| `true` | `https://matls-clients.api.stage.cora.com.br` |
| `false` | `https://matls-clients.api.cora.com.br` |

**A credencial de stage não funciona em produção.** Virar `Sandbox: false` sem
trocar o par de arquivos derruba o handshake mTLS logo no `/token`.

### Deploy sem volume (Railway, Fly, container)

Onde não dá para montar arquivo, use o conteúdo inline. Aceita PEM cru ou o
mesmo PEM em base64, porque variável de ambiente multi-linha quebra em boa
parte dos orquestradores:

```bash
Cora__CertificatePem=$(base64 -w0 certificate.pem)
Cora__PrivateKeyPem=$(base64 -w0 private-key.key)
```

O `.gitignore` já barra `*.pem`, `*.key`, `*.pfx` e `*.p12`. Nunca versione a
credencial.

---

## 3. Endpoints

### Emitir cobrança

```http
POST /api/v1/pagamentos/cobrancas
Authorization: Bearer <jwt>
```

```json
{
  "tipoProjeto": "Formatura",
  "metodo": "Pix",
  "valor": 250.00,
  "vencimento": "2026-10-24",
  "descricao": "Parcela 2/10",
  "referenciaExterna": "parcela-123",
  "parcelaId": "8f14e45f-ceea-467a-9575-6e1d2c0a9b3d",
  "pagador": {
    "nome": "João da Silva",
    "documento": "111.222.333-96",
    "email": "joao@exemplo.com"
  }
}
```

O PSP não é escolhido pelo chamador: `Formatura` roteia para a Cora. Pedir
`CartaoCredito` aqui devolve **422** antes de qualquer chamada HTTP.

**Mande sempre o `parcelaId`.** Ele é opcional no contrato, mas é o que grava
`PspChargeId` na parcela — e sem esse vínculo o webhook recebe a confirmação de
pagamento e não encontra o que baixar. A parcela é carregada antes da emissão:
id inexistente devolve 400 e parcela já quitada devolve 409, ambos sem criar
cobrança no PSP.

`referenciaExterna` continua sendo a chave de idempotência. Use algo estável por
parcela, não um valor novo a cada clique.

Se a cobrança for criada no PSP e a gravação no banco falhar, a API devolve
**500** e registra um log `Critical` com o id da cobrança e da parcela. Reemitir
com a mesma `referenciaExterna` é seguro e resolve: a Cora devolve a mesma
fatura em vez de abrir outra.

### Consultar situação

```http
GET /api/v1/pagamentos/cobrancas/cora/{chargeId}
```

Devolve o contrato normalizado com `pago` já resolvido. Serve para o painel e
para reconciliar manualmente quando o webhook não chegou.

### Webhook

```http
POST /api/v1/pagamentos/webhooks/cora?secret=<Cora:WebhookSecret>
```

Anônimo por necessidade — quem chama é a Cora, não um usuário logado.

**O segredo vai na query, e não é escolha.** Na Integração Direta a Cora não
tem tela de webhook: o endereço é cadastrado por API, e esse cadastro aceita
apenas `url`, `resource` e `trigger`. Não há como configurar cabeçalho, então a
query é o único lugar onde o segredo cabe. O endpoint ainda aceita
`X-Webhook-Secret` para quem chamar à mão, mas a Cora nunca vai usar.

### Cadastrar o endpoint na Cora

Sem este cadastro o boleto é pago e **o sistema nunca sabe**: a parcela fica
pendente com o dinheiro já na conta. É o passo mais fácil de esquecer e o mais
caro de descobrir tarde.

Defina `Cora:WebhookUrlPublica` com o endereço público da API e chame:

```http
POST /api/v1/pagamentos/cora/endpoints
Authorization: Bearer <jwt de super_admin>
```

Com o corpo vazio ele monta a URL sozinho e já anexa o segredo. Por padrão
assina `invoice.*`, todos os gatilhos de fatura. Marcar demais é seguro, porque
o sistema reconsulta a Cora antes de dar qualquer parcela como paga; marcar de
menos faz perder confirmação de pagamento.

Para conferir para onde a Cora está entregando hoje:

```http
GET /api/v1/pagamentos/cora/endpoints
```

**Lista vazia significa que ninguém será avisado de pagamento algum.** A URL
vem sem a query, porque ela carrega o segredo.

Para remover um cadastro:

```http
DELETE /api/v1/pagamentos/cora/endpoints/{id}
```

Equivalente em curl, se precisar falar direto com a Cora (note o `matls-clients`
no host e o `Idempotency-Key` em UUID, ambos obrigatórios):

```bash
curl https://matls-clients.api.cora.com.br/endpoints \
  --cert certificate.pem --key private-key.key \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{"url":"https://sua-api/api/v1/pagamentos/webhooks/cora?secret=SEGREDO","resource":"invoice","trigger":"*"}'
```

Recursos aceitos: `invoice`, `transfer`, `payment`, `register`,
`service_receipt` e `*`. Gatilhos de fatura: `drafted`, `created`, `paid`,
`canceled`, `overdue` e `*`.

É ele que **dá baixa na parcela**: encontra a parcela por `PspChargeId`, marca
`Pago`, grava `ValorPago` e `DataPagamento`. O evento fica registrado em
`webhook_events`, e reentrega do mesmo par cobrança+status não baixa duas vezes.

**Pagamento parcial não quita.** `PAID_PARTIALLY` atualiza o `ValorPago` e
mantém a parcela pendente, porque o saldo ainda precisa ser cobrado. Contar
parcial como quitado daria baixa numa parcela de R$ 1.000 que recebeu R$ 300. O
valor recebido é reescrito a cada notificação, então um parcial seguido da
quitação termina com o valor cheio gravado.

A cota é separada por quem apresenta o segredo: 600 por minuto para quem
apresenta, 30 para quem não. Uma janela única deixaria um atacante encher o
balde e a notificação legítima da Cora levar 429 — o pagamento entraria e a
parcela nunca seria baixada.

---

## 4. O que a resposta da Cora tem de armadilha

Confirmado contra o stage em setembro de 2026. Estes três pontos são a razão de
o adapter existir:

**O Pix não vem onde parece.** Não existe `payment_options.pix`. O copia-e-cola
está na **raiz**, em `pix.emv`.

**Numa cobrança Pix, `payment_options.bank_slip.url` muda de significado:** vira
o **PNG do QR Code**, e `barcode`/`digitable` vêm nulos. É a mesma chave JSON
servindo a dois conteúdos diferentes conforme o `payment_forms` enviado.

**`Idempotency-Key` precisa ser UUID.** Qualquer outro formato leva 400:

```
The Idempotency-Key|x-idempotency-id header must be a valid UUID.
```

Como a referência interna é `parcela-123`, o adapter deriva um **UUIDv5
determinístico** dela. Determinístico, e não sorteado, porque é o que preserva o
efeito que importa: reenviar a mesma parcela devolve a mesma fatura em vez de
cobrar o formando duas vezes.

Resumo do que cada `payment_forms` produz:

| Enviado | `pix.emv` (raiz) | `payment_options.bank_slip` |
|---|---|---|
| `["PIX"]` | copia-e-cola | `url` = PNG do QR Code |
| `["BANK_SLIP"]` | nulo | `url` = PDF, + barcode e linha digitável |

Enviar os dois juntos emite **só o boleto**, com `pix.emv` nulo — por isso o
adapter manda sempre uma forma por cobrança.

---

## 5. O webhook não é fonte da verdade

O corpo recebido serve só para descobrir o **id da fatura**. Em seguida a API
reconsulta a Cora por mTLS, e é essa resposta que vale.

Custa uma chamada extra e elimina duas classes de problema de uma vez: dar
parcela como paga a partir de um POST forjado, já que ninguém de fora forja o
canal mTLS; e quebrar a cada ajuste de payload do provedor, já que o envelope
pouco importa.

O `WebhookSecret` é **obrigatório**, na query `?secret=` (ou no header
`X-Webhook-Secret` para chamadas manuais), comparado em
tempo constante. Não é ele que garante a veracidade do pagamento — isso é papel
da reconsulta. Ele existe porque o endpoint é anônimo e cada POST aceito dispara
uma chamada autenticada à Cora: sem segredo, quem descobrisse a URL teria um
amplificador de tráfego contra o PSP.

Respostas:

| Código | Quando |
|---|---|
| 200 | Processado, com o status da fatura |
| 202 | Corpo sem id reconhecível, descartado de propósito para a Cora não reenviar para sempre |
| 401 | Segredo ausente ou divergente, no cabeçalho e na query |
| 429 | Acima da cota (600/min com segredo, 30/min sem) |
| 503 | `Cora:WebhookSecret` não configurado — a Cora deve reenviar depois do ajuste |

O 503 é deliberado. Responder 200 sem processar daria o evento por entregue e a
parcela ficaria pendente com o dinheiro já recebido.

---

## 6. Testes

Os testes unitários usam **respostas reais do stage**, mantidas verbatim. Um
payload arrumado à mão deixaria de detectar a mudança que quebra produção.

```bash
dotnet test tests/FormaturasFlow.Api.UnitTests
```

O teste ao vivo é desligado por padrão e só roda com credencial na mão:

```bash
CORA_CERT_PEM=/caminho/certificate.pem \
CORA_KEY_PEM=/caminho/private-key.key \
dotnet test tests/FormaturasFlow.Api.IntegrationTests --filter FullyQualifiedName~CoraLiveTests
```

Ele cobre o que nenhum mock pega: handshake mTLS, contrato do provedor e a
idempotência que impede cobrança duplicada.

---

## 7. Antes de virar a chave em produção

- [ ] Credencial de produção gerada na Cora Web e guardada em lugar seguro
- [ ] `CertificatePemPath` / `PrivateKeyPemPath` apontando para o par de **produção**
- [ ] `Sandbox: false`
- [ ] `ClientId` vazio (vem do certificado)
- [ ] `WebhookSecret` e `WebhookUrlPublica` definidos
- [ ] Endpoint cadastrado na Cora via `POST /api/v1/pagamentos/cora/endpoints`
- [ ] `GET /api/v1/pagamentos/cora/endpoints` devolvendo o cadastro (lista vazia = ninguém é avisado)
- [ ] Parcela gravando `PspChargeId` na emissão — sem isso o webhook não acha o que baixar
- [ ] Teste ao vivo rodado contra produção com **valor baixo** e cobrança cancelada depois
- [ ] Vencimento do certificado anotado — ele expira em 1 ano:
      `openssl x509 -in certificate.pem -noout -enddate`
