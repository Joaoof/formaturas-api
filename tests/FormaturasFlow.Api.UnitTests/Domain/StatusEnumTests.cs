using FluentAssertions;
using FormaturasFlow.Api.Data;
using FormaturasFlow.Api.Domain;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Domain;

public class StatusEnumTests
{
    [Fact]
    public void StatusParcela_Tem_Todos_Os_Estados_Financeiros()
    {
        Enum.GetNames<StatusParcela>().Should().BeEquivalentTo(
            [nameof(StatusParcela.Pendente), nameof(StatusParcela.Pago), nameof(StatusParcela.Atrasado), nameof(StatusParcela.Cancelado)]);
    }

    [Fact]
    public void Parcela_Novo_Nasce_Pendente_Com_Valor_Pago_Zero()
    {
        var p = new Parcela();
        p.Status.Should().Be(StatusParcela.Pendente);
        p.ValorPago.Should().Be(0m);
        p.DataPagamento.Should().BeNull();
        p.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Despesa_Nova_Nasce_Pendente()
    {
        var d = new Despesa();
        d.Status.Should().Be(StatusDespesa.Pendente);
        d.Categoria.Should().Be("geral");
    }

    [Fact]
    public void Roles_Constants_Sao_Estaveis()
    {
        Roles.SuperAdmin.Should().Be("super_admin");
        Roles.Funcionario.Should().Be("funcionario");
        Roles.Aluno.Should().Be("aluno");
    }
}
