using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Endpoints;

public static class PublicEndpoints
{
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
        List<ParcelaAdesao> Parcelas);

    public record AdesaoResponse(Guid AlunoId, string Nome, string Cpf, string LoginUsuario);

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

        group.MapPost("/adesao", AdesaoAsync)
            .WithSummary("Adesao publica do formando: cria/atualiza aluno, contrato e parcelas em transacao")
            .Produces<AdesaoResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static string ApenasDigitos(string s) => new(s.Where(char.IsDigit).ToArray());

    private static async Task<IResult> AdesaoAsync(AdesaoRequest req, AppDbContext db)
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
            FormaPagamento = "boleto",
            DataContrato = DateOnly.FromDateTime(DateTime.UtcNow),
            TextoContrato = req.TextoContratoCompleto
        };
        db.Contratos.Add(contrato);

        foreach (var p in req.Parcelas)
        {
            db.Parcelas.Add(new Parcela
            {
                ContratoId = contrato.Id,
                Numero = p.Numero,
                Valor = p.Valor,
                ValorPago = 0,
                Vencimento = p.Vencimento,
                FormaPagamento = "boleto",
                Status = StatusParcela.Pendente
            });
        }

        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return Results.Ok(new AdesaoResponse(aluno.Id, aluno.NomeCompleto, cpf, aluno.LoginUsuario ?? cpf));
    }
}
