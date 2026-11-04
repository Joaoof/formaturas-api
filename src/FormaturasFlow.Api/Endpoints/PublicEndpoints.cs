using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using FormaturasFlow.Api.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FormaturasFlow.Api.Endpoints;

public static class PublicEndpoints
{
    /*  O formando não tem e-mail no cadastro obrigatório, então o acesso
        dele é derivado do CPF.  Login e senha iniciais são o próprio CPF —
        é o que o formulário de entrada pede, e o que a listagem de
        "meus contratos" usa para casar usuário com aluno.

        O domínio é `formandos.local` porque é o que o front usa para montar
        o e-mail no login e o que o resto da API já documentava.  Divergir
        aqui cria usuário com um endereço contra o qual ninguém autentica —
        a adesão termina "com sucesso" e o formando não entra.  */
    public const string DominioFormando = "formandos.local";

    public static string EmailDoFormando(string cpf) => $"{cpf}@{DominioFormando}";

    public record TurmaPublica(
        Guid Id, string Nome, string? Curso, string? Faculdade, string? Instituicao,
        string? Cidade, string? Semestre, int? AnoFormatura, DateOnly? PrevisaoFormatura,
        string TipoEvento, DateOnly? DataEvento, string Status, string? Observacoes);

    public record DadosPessoais(
        string NomeCompleto, string Cpf, string? Rg, string? Telefone,
        string Whatsapp, string Email, string Endereco, string Cidade, string? Cep);

    public record ParcelaAdesao(int Numero, decimal Valor, DateOnly Vencimento);

    public record AdesaoRequest(
        Guid TurmaId, DadosPessoais DadosPessoais, string Pacote,
        decimal ValorTotal, int NumParcelas, int DiaVencimento,
        bool AutorizaImagem, string TextoContratoCompleto,
        List<ParcelaAdesao> Parcelas,

        /*  Rubrica desenhada pelo formando (PNG em data URL).  Opcional no
            contrato para não quebrar quem já integra, mas é o que transforma
            o aceite num documento com valor de prova.  */
        string? AssinaturaImagem = null,

        /*  O que o formando escolheu no passo 2 da adesão.  Era ignorado: o
            contrato nascia "boleto" mesmo para quem escolheu cartão, e o
            painel então oferecia boleto a quem tinha contratado cartão.
            Opcional e com padrão para não quebrar integração antiga.  */
        string? FormaPagamento = null,

        /*  Id da cobrança do cartão já aprovada no passo de pagamento. Quando
            vem, as parcelas nascem PAGAS (o cartão quitou o total).  */
        string? CartaoChargeId = null);

    public record AdesaoResponse(
        Guid AlunoId, string Nome, string Cpf, string LoginUsuario,
        Guid ContratoId, bool Assinado);

    public record CartaoInfo(string Titular, string Numero, int MesValidade, int AnoValidade, string Cvv);

    public record CobrancaCartaoRequest(
        Guid TurmaId, decimal Valor, int Parcelas,
        string Nome, string Documento, string Email, string Cep, string NumeroEndereco,
        string Descricao, CartaoInfo Cartao);

    public record CobrancaCartaoResponse(bool Aprovado, string? ChargeId, string? Status, string Mensagem);

    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/public").WithTags("Public").AllowAnonymous();

        group.MapGet("/turmas/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var t = await db.Turmas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) throw new RecursoNaoEncontradoException("Turma", id);
            return Results.Ok(new TurmaPublica(
                t.Id, t.Nome, t.Curso, t.Faculdade, t.Instituicao,
                t.Cidade, t.Semestre, t.AnoFormatura, t.PrevisaoFormatura,
                t.TipoEvento.ToString(), t.DataEvento, t.Status.ToString(), t.Observacoes));
        })
            .WithSummary("Dados publicos de uma turma (para o formulario de adesao)")
            .Produces<TurmaPublica>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/turmas/{id:guid}/visita", async (Guid id, AppDbContext db) =>
        {
            /*  Incremento atomico no banco — nao carrega a turma, nao dá corrida
                com quem edita os pacotes.  */
            var n = await db.Turmas.Where(t => t.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.VisitasAdesao, t => t.VisitasAdesao + 1));
            return n > 0 ? Results.NoContent() : Results.NotFound();
        })
            .WithSummary("Registra um acesso ao link de adesao (contador de visitas)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/adesao", AdesaoAsync)
            .WithSummary("Adesao publica do formando: cria/atualiza aluno, contrato e parcelas em transacao")
            .Produces<AdesaoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/cobranca-cartao", CobrancaCartaoAsync)
            .WithSummary("Cobra o cartao (Asaas) no passo de pagamento da adesao — retorna aprovado/recusado")
            .Produces<CobrancaCartaoResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static string ApenasDigitos(string s) => new(s.Where(char.IsDigit).ToArray());

    /*  Normaliza o que vem do formulário para os ids que o resto do sistema
        já usa (FORMAS_PAGAMENTO no front e o rótulo do PDF do contrato).
        Qualquer coisa que não seja cartão nem pix cai em boleto: é o padrão
        da turma que não liberou cartão, e errar para o lado do boleto não
        cobra ninguém num cartão que ele não escolheu.  */
    private static string NormalizarFormaPagamento(string? forma) =>
        (forma ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "cartao" or "cartão" or "cartao_credito" or "cartaocredito" or "credit_card" => "cartao_credito",
            "pix" => "pix",
            _ => "boleto"
        };

    private static async Task<IResult> AdesaoAsync(
        AdesaoRequest req,
        HttpContext ctx,
        AppDbContext db,
        UserManager<ApplicationUser> users,
        IServiceScopeFactory scopeFactory)
    {
        var cpf = ApenasDigitos(req.DadosPessoais.Cpf);
        if (cpf.Length != 11) throw new DadosInvalidosException("CPF_INVALIDO", "CPF invalido.");

        var turma = await db.Turmas.FirstOrDefaultAsync(t => t.Id == req.TurmaId);
        if (turma is null) throw new RecursoNaoEncontradoException("Turma", req.TurmaId);

        await using var tx = await db.Database.BeginTransactionAsync();

        var aluno = await db.Alunos.FirstOrDefaultAsync(a => a.Cpf == cpf && a.TurmaId == req.TurmaId);
        if (aluno is null)
        {
            aluno = new Aluno { TurmaId = req.TurmaId, Cpf = cpf, Status = StatusAluno.Ativo };
            db.Alunos.Add(aluno);
        }

        aluno.NomeCompleto = req.DadosPessoais.NomeCompleto.Trim();
        aluno.Rg = req.DadosPessoais.Rg?.Trim();
        aluno.Telefone = req.DadosPessoais.Telefone?.Trim();
        aluno.Whatsapp = req.DadosPessoais.Whatsapp.Trim();
        aluno.Email = req.DadosPessoais.Email.Trim();
        aluno.Endereco = req.DadosPessoais.Endereco.Trim();
        aluno.Cidade = req.DadosPessoais.Cidade.Trim();
        aluno.Cep = req.DadosPessoais.Cep?.Trim();
        aluno.LoginUsuario = cpf;
        aluno.Status = StatusAluno.Ativo;
        aluno.AtualizadoEm = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        var formaPagamento = NormalizarFormaPagamento(req.FormaPagamento);

        var contratosVelhos = await db.Contratos.Where(c => c.AlunoId == aluno.Id).ToListAsync();
        if (contratosVelhos.Count > 0)
            db.Contratos.RemoveRange(contratosVelhos);

        var contrato = new Contrato
        {
            AlunoId = aluno.Id,
            Pacote = req.Pacote,
            ValorTotal = req.ValorTotal,
            ValorEntrada = 0,
            Desconto = 0,
            NumParcelas = req.NumParcelas,
            DiaVencimento = req.DiaVencimento,
            AutorizaImagem = req.AutorizaImagem,
            FormaPagamento = formaPagamento,
            DataContrato = DateOnly.FromDateTime(DateTime.UtcNow),
            TextoContrato = req.TextoContratoCompleto
        };
        db.Contratos.Add(contrato);

        /*  A assinatura entra na MESMA transação da adesão.  Gravar o
            contrato agora e assinar num segundo passo abriria a janela em
            que o formando acha que assinou e o documento está sem rubrica.  */
        if (!string.IsNullOrWhiteSpace(req.AssinaturaImagem))
            AssinaturaContrato.Aplicar(
                contrato,
                req.AssinaturaImagem,
                aluno.NomeCompleto,
                cpf,
                ctx.Connection.RemoteIpAddress?.ToString(),
                ctx.Request.Headers.UserAgent.ToString());

        /*  Cartão aprovado quita o total na hora: as parcelas já nascem PAGAS,
            com o id da cobrança do Asaas.  */
        var cartaoPago = formaPagamento == "cartao_credito" && !string.IsNullOrWhiteSpace(req.CartaoChargeId);
        foreach (var p in req.Parcelas)
        {
            db.Parcelas.Add(new Parcela
            {
                ContratoId = contrato.Id,
                Numero = p.Numero,
                Valor = p.Valor,
                ValorPago = cartaoPago ? p.Valor : 0,
                Vencimento = p.Vencimento,
                FormaPagamento = formaPagamento,
                Status = cartaoPago ? StatusParcela.Pago : StatusParcela.Pendente,
                PspProvider = cartaoPago ? "asaas" : null,
                PspChargeId = cartaoPago ? req.CartaoChargeId : null
            });
        }

        await db.SaveChangesAsync();

        /*  Acesso do formando.  Sem isto a adesão terminava sem login: o
            aluno existia na tabela, mas não havia usuário para autenticar, e
            a tela de entrada por CPF não tinha contra o que validar.  */
        await GarantirAcessoAsync(users, aluno, cpf);

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        /*  Boleto: gera TODOS os boletos automaticamente já na finalização, em
            background (fora da transação e da request, pra não travar a adesão
            em N chamadas ao banco). O painel então mostra a 2ª via, sem pedir
            pra gerar de novo.  O e-mail sai pelo próprio PSP.  */
        if (formaPagamento == "boleto")
        {
            var pagador = new PagadorInfo(aluno.NomeCompleto, cpf, aluno.Email, aluno.Whatsapp);
            var banco = BancoDaTurma(turma.Observacoes);
            var contratoId = contrato.Id;
            var descricao = $"{turma.Nome} - {req.Pacote}";
            _ = Task.Run(() => GerarBoletosAsync(scopeFactory, contratoId, banco, pagador, descricao));
        }

        return Results.Ok(new AdesaoResponse(
            aluno.Id, aluno.NomeCompleto, cpf, aluno.LoginUsuario ?? cpf,
            contrato.Id, contrato.Assinado));
    }

    /*  Lê o banco (Cora/Asaas) que a empresa escolheu pra turma — grava no
        JSON de observacoes.  Null = padrão do domínio (Cora).  */
    private static PaymentProvider? BancoDaTurma(string? observacoes)
    {
        if (string.IsNullOrWhiteSpace(observacoes)) return null;
        try
        {
            using var doc = JsonDocument.Parse(observacoes);
            if (doc.RootElement.TryGetProperty("banco", out var b) && b.ValueKind == JsonValueKind.String)
            {
                return (b.GetString()?.Trim().ToLowerInvariant()) switch
                {
                    "asaas" => PaymentProvider.Asaas,
                    "cora"  => PaymentProvider.Cora,
                    _       => (PaymentProvider?)null
                };
            }
        }
        catch { /*  observacoes em texto livre — ignora  */ }
        return null;
    }

    /*  Gera o boleto de cada parcela do contrato, em background e com escopo
        próprio (a request já respondeu). Falha de uma parcela não derruba as
        outras — fica logada e o painel pode reemitir a que faltou.  */
    private static async Task GerarBoletosAsync(
        IServiceScopeFactory scopeFactory,
        Guid contratoId,
        PaymentProvider? banco,
        PagadorInfo pagador,
        string descricaoBase)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var router = scope.ServiceProvider.GetRequiredService<IPaymentRouter>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Adesao.Boletos");

        var parcelas = await db.Parcelas
            .Where(p => p.ContratoId == contratoId)
            .OrderBy(p => p.Numero)
            .ToListAsync();

        foreach (var p in parcelas)
        {
            if (!string.IsNullOrEmpty(p.PspChargeId)) continue;  // já emitido
            try
            {
                var gateway = router.ResolverPreferencial(TipoProjeto.Formatura, MetodoPagamento.Boleto, banco);
                var cob = await gateway.CriarCobrancaAsync(new CobrancaRequest(
                    Metodo: MetodoPagamento.Boleto,
                    Valor: p.Valor,
                    Vencimento: p.Vencimento,
                    Descricao: $"{descricaoBase} - Parcela {p.Numero}",
                    ReferenciaExterna: $"parcela:{p.Id}",
                    Pagador: pagador));

                p.PspProvider = cob.Provider.ToString().ToLowerInvariant();
                p.PspChargeId = cob.ChargeId;
                p.PspStatus = cob.Status;
                p.BoletoUrl = cob.BoletoUrl;
                p.BoletoLinhaDigitavel = cob.BoletoLinhaDigitavel;
                p.BoletoCodigoBarras = cob.BoletoCodigoBarras;
                p.LinkPagamento = cob.LinkPagamento;
                p.FormaPagamento = "boleto";
                p.AtualizadaEm = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Falha ao gerar boleto da parcela {ParcelaId} na adesao", p.Id);
            }
        }
    }

    /*  Cobra o cartão no passo de pagamento da adesão (antes de finalizar).
        Aprovado → o front segue para assinar; recusado → o front mostra
        "continuar sem pagamento". O dado de cartão não é persistido aqui.  */
    private static async Task<IResult> CobrancaCartaoAsync(
        CobrancaCartaoRequest req,
        IPaymentRouter router,
        ILoggerFactory logs)
    {
        var log = logs.CreateLogger("Public.CartaoAdesao");
        try
        {
            var gateway = router.ResolverPreferencial(
                TipoProjeto.Formatura, MetodoPagamento.CartaoCredito, PaymentProvider.Asaas);

            var cob = await gateway.CriarCobrancaAsync(new CobrancaRequest(
                Metodo: MetodoPagamento.CartaoCredito,
                Valor: req.Valor,
                Vencimento: DateOnly.FromDateTime(DateTime.UtcNow),
                Descricao: req.Descricao,
                ReferenciaExterna: $"cartao:{req.TurmaId}:{ApenasDigitos(req.Documento)}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                Pagador: new PagadorInfo(req.Nome, req.Documento, req.Email, null, req.Cep, req.NumeroEndereco),
                Cartao: new CartaoCreditoInfo(req.Cartao.Titular, req.Cartao.Numero, req.Cartao.MesValidade, req.Cartao.AnoValidade, req.Cartao.Cvv),
                Parcelas: req.Parcelas));

            var aprovado = cob.Status is "CONFIRMED" or "RECEIVED" or "RECEIVED_IN_CASH" or "APPROVED";
            return Results.Ok(new CobrancaCartaoResponse(
                aprovado, cob.ChargeId, cob.Status,
                aprovado
                    ? "Pagamento aprovado no cartão!"
                    : $"O cartão não foi aprovado (status {cob.Status})."));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Cartão recusado/erro na adesão");
            return Results.Ok(new CobrancaCartaoResponse(false, null, null,
                "Não conseguimos aprovar o cartão. Tente outro cartão ou escolha outra forma de pagamento."));
        }
    }

    /*  Idempotente: refazer a adesão com o mesmo CPF reaproveita o usuário
        em vez de falhar por e-mail duplicado.  A senha só é definida na
        criação, para não reverter em silêncio a troca feita pelo formando.  */
    private static async Task GarantirAcessoAsync(
        UserManager<ApplicationUser> users,
        Aluno aluno,
        string cpf)
    {
        var email = EmailDoFormando(cpf);
        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                NomeCompleto = aluno.NomeCompleto
            };

            var criado = await users.CreateAsync(user, cpf);
            if (!criado.Succeeded)
                throw new DadosInvalidosException("ACESSO_NAO_CRIADO",
                    $"Não foi possível criar o acesso do formando: {string.Join("; ", criado.Errors.Select(e => e.Description))}.");

            await users.AddToRoleAsync(user, Roles.Aluno);
        }

        aluno.UserId = user.Id;
    }
}
