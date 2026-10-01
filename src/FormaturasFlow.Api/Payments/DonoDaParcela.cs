using System.Security.Claims;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FormaturasFlow.Api.Payments;

/*  Quem pode emitir cobrança de qual parcela.

    Existe porque o painel do formando oferece "Pagar" nas parcelas dele, e o
    endpoint de emissão exigia papel de equipe — o botão aparecia, o formando
    clicava e levava 403.  Liberar para qualquer autenticado resolveria o 403
    e abriria outro problema: um formando emitindo cobrança na parcela de
    outro.  A regra, então, é posse.  */
public static class DonoDaParcela
{
    public static bool EhEquipe(ClaimsPrincipal user) =>
        user.IsInRole(Roles.SuperAdmin) || user.IsInRole(Roles.Funcionario);

    /*  Resolve o aluno do token.  Aceita o vínculo por UserId e também o CPF
        embutido no e-mail (`cpf@formandos.local`), que é como o formando
        entra — a mesma regra de /alunos/me, para as duas telas não
        discordarem sobre quem é o dono.  */
    public static async Task<Aluno?> AlunoDoTokenAsync(
        ClaimsPrincipal user,
        AppDbContext db,
        CancellationToken ct = default)
    {
        var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
        Guid.TryParse(sub, out var userId);

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        var cpf = CpfDoEmail(email);

        if (userId == Guid.Empty && cpf is null)
            return null;

        return await db.Alunos.AsNoTracking()
            .FirstOrDefaultAsync(a => (userId != Guid.Empty && a.UserId == userId)
                                   || (cpf != null && a.Cpf == cpf), ct);
    }

    internal static string? CpfDoEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var local = email.Split('@')[0];
        var digitos = new string(local.Where(char.IsDigit).ToArray());

        return digitos.Length == 11 ? digitos : null;
    }

    /*  Equipe emite para qualquer um.  Formando só para si, e só informando a
        parcela — sem `parcelaId` não há posse para conferir, e aceitar seria
        deixar qualquer aluno criar cobrança solta na conta da empresa.  */
    public static async Task GarantirPodeEmitirAsync(
        ClaimsPrincipal user,
        AppDbContext db,
        Guid? parcelaId,
        CancellationToken ct = default)
    {
        if (EhEquipe(user))
            return;

        if (parcelaId is null)
            throw new AcessoNegadoException("PARCELA_OBRIGATORIA",
                "Informe a parcela: só a equipe pode emitir cobrança sem vínculo.");

        var aluno = await AlunoDoTokenAsync(user, db, ct)
            ?? throw new AcessoNegadoException("SEM_CADASTRO_DE_FORMANDO",
                "Nenhum formando vinculado a este acesso.");

        var dono = await db.Parcelas.AsNoTracking()
            .Where(p => p.Id == parcelaId.Value)
            .Select(p => p.Contrato!.AlunoId)
            .FirstOrDefaultAsync(ct);

        if (dono == Guid.Empty)
            throw new RecursoNaoEncontradoException("Parcela", parcelaId.Value);

        if (dono != aluno.Id)
            throw new AcessoNegadoException("PARCELA_DE_OUTRO_FORMANDO",
                "Esta parcela não pertence a você.");
    }
}
