#!/usr/bin/env bash
set -euo pipefail

BASE="${BASE_URL:-http://191.101.78.252}"
EMAIL="${SMOKE_EMAIL:-smoke-$(date +%s)@ex.com}"
PASSWORD="${SMOKE_PASSWORD:-SenhaForte2026!}"

log()  { printf "\033[1;36m[smoke]\033[0m %s\n" "$*"; }
pass() { printf "  \033[1;32m✓\033[0m %s\n" "$*"; }
fail() { printf "  \033[1;31m✗\033[0m %s\n" "$*" >&2; exit 1; }

expect_status() {
    local expected="$1"; local actual="$2"; local label="$3"
    [ "$actual" = "$expected" ] || fail "$label: esperado HTTP $expected, veio $actual"
}

log "Alvo: $BASE"

log "[1] GET /health"
STATUS=$(curl -sS -o /tmp/smoke_health -w "%{http_code}" "$BASE/health")
expect_status 200 "$STATUS" "health"
grep -q Healthy /tmp/smoke_health && pass "Healthy"

log "[2] GET /"
STATUS=$(curl -sS -o /tmp/smoke_root -w "%{http_code}" "$BASE/")
expect_status 200 "$STATUS" "root"
grep -q FormaturasFlow.Api /tmp/smoke_root && pass "root serve metadata"

log "[3] POST /auth/register ($EMAIL)"
STATUS=$(curl -sS -o /tmp/smoke_reg -w "%{http_code}" -X POST "$BASE/auth/register" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"nomeCompleto\":\"Smoke Test\"}")
expect_status 200 "$STATUS" "register"
TOK=$(grep -oP '"accessToken":"[^"]+' /tmp/smoke_reg | sed 's/.*"//')
[ -n "$TOK" ] || fail "sem accessToken no register"
pass "register OK — token emitido"

log "[4] GET /auth/me"
STATUS=$(curl -sS -o /tmp/smoke_me -w "%{http_code}" "$BASE/auth/me" -H "Authorization: Bearer $TOK")
expect_status 200 "$STATUS" "me"
grep -q "$EMAIL" /tmp/smoke_me && pass "me retorna email do user logado"

log "[5] POST /api/v1/turmas"
STATUS=$(curl -sS -o /tmp/smoke_turma -w "%{http_code}" -X POST "$BASE/api/v1/turmas" \
    -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
    -d '{"nome":"Smoke 2026","curso":"Test","anoFormatura":2026}')
expect_status 201 "$STATUS" "criar turma"
TID=$(grep -oP '"id":"[^"]+' /tmp/smoke_turma | head -1 | sed 's/.*"//')
pass "turma criada — id=$TID"

log "[6] POST /api/v1/alunos"
STATUS=$(curl -sS -o /tmp/smoke_aluno -w "%{http_code}" -X POST "$BASE/api/v1/alunos" \
    -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
    -d "{\"turmaId\":\"$TID\",\"nomeCompleto\":\"Smoke Aluno\",\"cpf\":\"12345678900\"}")
expect_status 201 "$STATUS" "criar aluno"
AID=$(grep -oP '"id":"[^"]+' /tmp/smoke_aluno | head -1 | sed 's/.*"//')
pass "aluno criado — id=$AID"

log "[7] POST /api/v1/contratos (rateia 900 em 3)"
STATUS=$(curl -sS -o /tmp/smoke_contrato -w "%{http_code}" -X POST "$BASE/api/v1/contratos" \
    -H "Authorization: Bearer $TOK" -H "Content-Type: application/json" \
    -d "{\"alunoId\":\"$AID\",\"valorTotal\":1000,\"valorEntrada\":100,\"numParcelas\":3,\"dataContrato\":\"2026-01-01\",\"primeiroVencimento\":\"2026-02-05\"}")
expect_status 201 "$STATUS" "criar contrato"
pass "contrato criado com 3 parcelas"

log "[8] GET /api/v1/parcelas?status=pendente"
STATUS=$(curl -sS -o /tmp/smoke_parc -w "%{http_code}" "$BASE/api/v1/parcelas?status=pendente" -H "Authorization: Bearer $TOK")
expect_status 200 "$STATUS" "listar pendentes"
[ "$(grep -oc '"id"' /tmp/smoke_parc)" -ge 3 ] && pass "3 parcelas pendentes visiveis"

log "[9] Webhook Efi sem secret → 401"
STATUS=$(curl -sS -o /dev/null -w "%{http_code}" -X POST "$BASE/webhooks/efi" \
    -H "Content-Type: application/json" -d '{}')
expect_status 401 "$STATUS" "webhook sem secret"
pass "webhook rejeitado sem secret"

printf "\n\033[1;32m✓ SMOKE PASSOU\033[0m — API em %s\n" "$BASE"
