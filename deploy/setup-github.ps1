$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\GitHub CLI;$env:PATH"

$repo = "Joaoof/formaturas-api"
Write-Host "Repo alvo: $repo" -ForegroundColor Cyan

Write-Host "[1/6] Habilitando secret scanning + push protection..." -ForegroundColor Yellow
gh api -X PATCH "repos/$repo" `
    -f "security_and_analysis[secret_scanning][status]=enabled" `
    -f "security_and_analysis[secret_scanning_push_protection][status]=enabled" `
    -f "security_and_analysis[dependabot_security_updates][status]=enabled" | Out-Null

Write-Host "[2/6] Habilitando vulnerability alerts (dependabot)..." -ForegroundColor Yellow
gh api -X PUT "repos/$repo/vulnerability-alerts" | Out-Null
gh api -X PUT "repos/$repo/automated-security-fixes" | Out-Null

Write-Host "[3/6] Configurando branch protection em main..." -ForegroundColor Yellow
$rules = @{
    required_status_checks           = @{ strict = $true; contexts = @("build", "analyze (csharp)", "scan") }
    enforce_admins                   = $false
    required_pull_request_reviews    = @{ dismiss_stale_reviews = $true; required_approving_review_count = 1 }
    restrictions                     = $null
    required_linear_history          = $true
    allow_force_pushes               = $false
    allow_deletions                  = $false
    required_conversation_resolution = $true
} | ConvertTo-Json -Depth 6 -Compress
$rules | gh api -X PUT "repos/$repo/branches/main/protection" --input - | Out-Null

Write-Host "[4/6] Ajustando merge settings do repo..." -ForegroundColor Yellow
gh api -X PATCH "repos/$repo" `
    -F "allow_squash_merge=true" `
    -F "allow_merge_commit=false" `
    -F "allow_rebase_merge=false" `
    -F "delete_branch_on_merge=true" `
    -F "allow_auto_merge=true" | Out-Null

Write-Host "[5/6] Criando labels padrao..." -ForegroundColor Yellow
$labels = @(
    @{ name = "bug"; color = "d73a4a"; description = "Algo esta quebrado" },
    @{ name = "feature"; color = "0e8a16"; description = "Nova funcionalidade" },
    @{ name = "chore"; color = "cccccc"; description = "Manutencao / refactor" },
    @{ name = "security"; color = "b60205"; description = "Correcao de seguranca" },
    @{ name = "docs"; color = "0075ca"; description = "Documentacao" },
    @{ name = "deploy"; color = "1d76db"; description = "Deploy / infra" }
)
foreach ($l in $labels) {
    try {
        gh label create $l.name --color $l.color --description $l.description --force | Out-Null
    }
    catch { }
}

Write-Host "[6/6] Criando PAT para o Watchtower puxar do GHCR..." -ForegroundColor Yellow
Write-Host "  IMPORTANTE: PATs para GHCR precisam ser criados manualmente." -ForegroundColor Red
Write-Host "  Abra https://github.com/settings/tokens/new?scopes=read:packages e crie um token 'ghcr-watchtower'." -ForegroundColor Red
Write-Host "  Guarde o token para o .env na VPS (variavel GHCR_TOKEN)." -ForegroundColor Red

Write-Host ""
Write-Host "Concluido. Verificacao:" -ForegroundColor Green
gh api "repos/$repo" --jq '{name, private, security_and_analysis, delete_branch_on_merge, allow_squash_merge}'
