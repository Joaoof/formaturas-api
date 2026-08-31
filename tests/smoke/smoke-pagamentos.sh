#!/usr/bin/env bash
set -euo pipefail

# =====================================================================
# Smoke do roteamento de pagamentos (matriz dominio x metodo x PSP).
#
#   BASE_URL=http://191.101.78.252:8081 bash tests/smoke/smoke-pagamentos.sh
#
# As rotas de cobranca exigem super_admin/funcionario. Em banco novo o
# primeiro registro ja vira super_admin; em banco existente, informe uma
# conta admin em ADMIN_EMAIL/ADMIN_PASSWORD — senao os POSTs sao pulados.
# =====================================================================

BASE="${BASE_URL:-http://127.0.0.1:5298}"
API="$BASE/api/v1"
ADMIN_EMAIL="${ADMIN_EMAIL:-smoke-pag-$(date +%s)@ex.com}"
ADMIN_PASSWORD="${ADMIN_PASSWORD:-SenhaForte2026!}"

log()  { printf "\033[1;36m[smoke-pag]\033[0m %s\n" "$*"; }
pass() { printf "  \033[1;32m✓\033[0m %s\n" "$*"; }
skip() { printf "  \033[1;33m~\033[0m %s\n" "$*"; }
fail() { printf "  \033[1;31m✗\033[0m %s\n" "$*" >&2; exit 1; }

expect_status() {
    local expected="$1"; local actual="$2"; local label="$3"
    [ "$actual" = "$expected" ] || fail "$label: esperado HTTP $expected, veio $actual"
}

campo() { python3 -c "import sys,json;d=json.load(sys.stdin);print(d.get('$1',''))" 2>/dev/null; }

cobranca_json() {
    cat <<JSON
{
  "tipoProjeto": "$1",
  "metodo": "$2",
  "valor": 150.00,
  "vencimento": "$(date -d '+30 days' +%Y-%m-%d)",
  "descricao": "Smoke roteamento",
  "referenciaExterna": "smoke-$(date +%s)-$1-$2",
  "pagador": {
    "nome": "Maria Teste",
    "documento": "12345678909",
    "email": "maria.teste@ex.com",
    "telefone": "11999998888",
    "cep": "01310930",
    "numeroEndereco": "1578"
  }
}
JSON
}

log "Alvo: $BASE"

log "[1] Autenticacao"
STATUS=$(curl -sS -o /tmp/smoke_pag_auth -w "%{http_code}" -X POST "$BASE/auth/login" \
    -H "Content-Type: application/json" \
    -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\"}")
if [ "$STATUS" != "200" ]; then
    STATUS=$(curl -sS -o /tmp/smoke_pag_auth -w "%{http_code}" -X POST "$BASE/auth/register" \
        -H "Content-Type: application/json" \
        -d "{\"email\":\"$ADMIN_EMAIL\",\"password\":\"$ADMIN_PASSWORD\",\"nomeCompleto\":\"Smoke Pagamentos\"}")
    expect_status 200 "$STATUS" "register"
fi
TOKEN=$(campo accessToken < /tmp/smoke_pag_auth)
PAPEIS=$(python3 -c "import sys,json;print(','.join(json.load(sys.stdin).get('roles',[])))" < /tmp/smoke_pag_auth)
[ -n "$TOKEN" ] || fail "sem accessToken"
pass "autenticado como [$PAPEIS]"

log "[2] GET /pagamentos/metodos — fonte de verdade do checkout"
for par in "Casamento Asaas" "Formatura Cora"; do
    set -- $par
    STATUS=$(curl -sS -o /tmp/smoke_pag_cap -w "%{http_code}" "$API/pagamentos/metodos/$1" \
        -H "Authorization: Bearer $TOKEN")
    expect_status 200 "$STATUS" "metodos/$1"
    PROVIDER=$(campo provider < /tmp/smoke_pag_cap)
    [ "$PROVIDER" = "$2" ] || fail "metodos/$1: esperado provider $2, veio $PROVIDER"
    pass "$1 -> $2 $(cat /tmp/smoke_pag_cap | python3 -c "import sys,json;print(json.load(sys.stdin)['metodos'])")"
done

STATUS=$(curl -sS -o /dev/null -w "%{http_code}" "$API/pagamentos/metodos/Aniversario" \
    -H "Authorization: Bearer $TOKEN")
expect_status 400 "$STATUS" "dominio inexistente"
pass "dominio inexistente rejeitado"

log "[3] Autorizacao"
STATUS=$(curl -sS -o /dev/null -w "%{http_code}" "$API/pagamentos/metodos/Casamento")
expect_status 401 "$STATUS" "capabilities sem token"
pass "401 sem token"

case "$PAPEIS" in
    *super_admin*|*funcionario*) ;;
    *)
        skip "conta sem papel admin — POSTs de cobranca pulados"
        skip "reexecute com ADMIN_EMAIL/ADMIN_PASSWORD de um super_admin"
        log "OK (parcial)"
        exit 0
        ;;
esac

log "[4] Cruzamento indevido dominio x metodo — deve dar 422"
for par in "Casamento Pix" "Formatura CartaoCredito"; do
    set -- $par
    STATUS=$(curl -sS -o /tmp/smoke_pag_422 -w "%{http_code}" -X POST "$API/pagamentos/cobrancas" \
        -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
        -d "$(cobranca_json "$1" "$2")")
    expect_status 422 "$STATUS" "$1 + $2"
    CODIGO=$(campo codigo < /tmp/smoke_pag_422)
    [ "$CODIGO" = "PAGAMENTO_METODO_NAO_SUPORTADO" ] || fail "$1 + $2: codigo inesperado '$CODIGO'"
    ALTERNATIVAS=$(python3 -c "import sys,json;print(json.load(sys.stdin).get('metodosSuportados'))" < /tmp/smoke_pag_422)
    [ "$ALTERNATIVAS" != "None" ] || fail "$1 + $2: 422 sem metodosSuportados para o front se recuperar"
    pass "$1 + $2 -> 422 $CODIGO, alternativas $ALTERNATIVAS"
done

log "[5] Combinacoes permitidas — roteia para o PSP do dominio"
for par in "Casamento Boleto Asaas" "Casamento CartaoCredito Asaas" "Formatura Boleto Cora" "Formatura Pix Cora"; do
    set -- $par
    STATUS=$(curl -sS -o /tmp/smoke_pag_ok -w "%{http_code}" -X POST "$API/pagamentos/cobrancas" \
        -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
        -d "$(cobranca_json "$1" "$2")")
    case "$STATUS" in
        200)
            PROVIDER=$(campo provider < /tmp/smoke_pag_ok)
            [ "$PROVIDER" = "$3" ] || fail "$1 + $2: cobranca emitida no PSP errado ($PROVIDER)"
            pass "$1 + $2 -> 200 via $PROVIDER"
            ;;
        502)
            PROVIDER=$(campo provider < /tmp/smoke_pag_ok)
            [ "$PROVIDER" = "$3" ] || fail "$1 + $2: roteou para o PSP errado ($PROVIDER)"
            skip "$1 + $2 -> 502 no $PROVIDER (credencial de sandbox ausente); roteamento correto"
            ;;
        *)
            fail "$1 + $2: esperado 200 ou 502, veio $STATUS — $(head -c 200 /tmp/smoke_pag_ok)"
            ;;
    esac
done

log "[6] Payload invalido"
STATUS=$(curl -sS -o /dev/null -w "%{http_code}" -X POST "$API/pagamentos/cobrancas" \
    -H "Content-Type: application/json" -H "Authorization: Bearer $TOKEN" \
    -d "$(cobranca_json Casamento Bitcoin)")
expect_status 400 "$STATUS" "metodo inexistente"
pass "metodo inexistente rejeitado"

log "OK"
