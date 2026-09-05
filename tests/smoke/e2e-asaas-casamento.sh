#!/usr/bin/env bash
set -euo pipefail

BASE="${BASE_URL:-http://191.101.78.252}"
STAMP=$(date +%s)
ADMIN_EMAIL="${ADMIN_EMAIL:-e2e-admin-${STAMP}@ex.com}"
ADMIN_PASS="${ADMIN_PASS:-SenhaForte2026!}"
WEBHOOK_TOKEN="${ASAAS_WEBHOOK_TOKEN:?exporte ASAAS_WEBHOOK_TOKEN igual ao configurado no .env}"

log()  { printf "\n\033[1;36m[e2e]\033[0m %s\n" "$*"; }
ok()   { printf "  \033[1;32m✓\033[0m %s\n" "$*"; }
fail() { printf "  \033[1;31m✗\033[0m %s\n" "$*" >&2; exit 1; }

expect() { [ "$1" = "$2" ] || fail "$3 esperava HTTP $1, veio $2"; }
grab_id() { grep -oP '"id":"[^"]+' "$1" | head -1 | sed 's/.*"//'; }

TMP=$(mktemp -d); trap "rm -rf $TMP" EXIT

log "[1] register admin ($ADMIN_EMAIL)"
S=$(curl -sS -o $TMP/reg -w "%{http_code}" -X POST $BASE/auth/register \
  -H "Content-Type: application/json" \
  -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASS\",\"nomeCompleto\":\"E2E Admin\"}")
expect 200 "$S" "register"
TOK=$(grep -oP '"accessToken":"[^"]+' $TMP/reg | sed 's/.*"//')
[ -n "$TOK" ] || fail "sem token"
ok "token ok"

log "[2] cria turma tipo=Casamento com dataEvento futura"
S=$(curl -sS -o $TMP/turma -w "%{http_code}" -X POST $BASE/api/v1/turmas \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"nome":"Casamento Ana e Bruno","curso":"Casamento","tipoEvento":"Casamento","dataEvento":"2026-12-15"}')
expect 201 "$S" "criar turma"
TID=$(grab_id $TMP/turma)
ok "turmaId=$TID (tipo Casamento)"

log "[3] cria aluno (o noivo) com CPF valido de teste"
S=$(curl -sS -o $TMP/aluno -w "%{http_code}" -X POST $BASE/api/v1/alunos \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d "{\"turmaId\":\"$TID\",\"nomeCompleto\":\"Ana Cliente Teste\",\"cpf\":\"24971563792\",\"email\":\"cliente@sandbox.asaas.com\",\"whatsapp\":\"11999999999\"}")
expect 201 "$S" "criar aluno"
AID=$(grab_id $TMP/aluno)
ok "alunoId=$AID"

log "[4] cria contrato 3 parcelas (saldo 900 apos entrada de 100)"
S=$(curl -sS -o $TMP/contrato -w "%{http_code}" -X POST $BASE/api/v1/contratos \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d "{\"alunoId\":\"$AID\",\"valorTotal\":1000,\"valorEntrada\":100,\"numParcelas\":3,\"dataContrato\":\"2026-09-05\",\"primeiroVencimento\":\"2026-10-05\"}")
expect 201 "$S" "criar contrato"
ok "contrato + 3 parcelas geradas"

log "[5] pega uma parcela pendente"
curl -sS $BASE/api/v1/parcelas -H "Authorization: Bearer $TOK" > $TMP/parcelas
PID=$(grep -oP '"id":"[^"]+' $TMP/parcelas | head -1 | sed 's/.*"//')
ok "parcelaId=$PID"

log "[6] emite cobranca CARTAO via Asaas (deve rotear para Asaas por ser Casamento)"
S=$(curl -sS -o $TMP/cobr -w "%{http_code}" -X POST $BASE/api/v1/parcelas/$PID/cobranca \
  -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
  -d '{"tipo":"cartao","numParcelasCartao":1}')
if [ "$S" != "200" ]; then
  echo "resposta: $(cat $TMP/cobr)"
  fail "cobranca cartao (esperado 200, veio $S). Verifique se ASAAS_API_KEY esta correta no .env da VPS"
fi
CHARGE_ID=$(grep -oP '"pspChargeId":"[^"]+' $TMP/cobr | head -1 | sed 's/.*"//')
PROVIDER=$(grep -oP '"pspProvider":"[^"]+' $TMP/cobr | head -1 | sed 's/.*"//')
LINK=$(grep -oP '"linkPagamento":"[^"]+' $TMP/cobr | head -1 | sed 's/.*"//')
[ "$PROVIDER" = "asaas" ] || fail "roteador deveria ter escolhido asaas, veio $PROVIDER"
ok "provider=$PROVIDER chargeId=$CHARGE_ID"
ok "linkPagamento=$LINK"

log "[7] simula webhook Asaas confirmando o pagamento"
S=$(curl -sS -o $TMP/wh -w "%{http_code}" -X POST $BASE/api/v1/webhooks/asaas \
  -H "asaas-access-token: $WEBHOOK_TOKEN" -H "Content-Type: application/json" \
  -d "{\"id\":\"evt_e2e_${STAMP}\",\"event\":\"PAYMENT_RECEIVED\",\"payment\":{\"id\":\"$CHARGE_ID\",\"status\":\"RECEIVED\",\"value\":300.00}}")
expect 200 "$S" "webhook asaas"
ok "webhook aceito"

log "[8] verifica que a parcela virou Pago"
sleep 1
curl -sS $BASE/api/v1/parcelas?status=pago -H "Authorization: Bearer $TOK" > $TMP/pagos
if grep -q "\"$PID\"" $TMP/pagos; then
  ok "parcela $PID esta em /parcelas?status=pago"
else
  fail "parcela nao apareceu na listagem de pagas"
fi

printf "\n\033[1;32m✓ E2E CASAMENTO PASSOU\033[0m\n"
printf "  admin: %s\n  senha: %s\n  linkCartao: %s\n" "$ADMIN_EMAIL" "$ADMIN_PASS" "$LINK"
