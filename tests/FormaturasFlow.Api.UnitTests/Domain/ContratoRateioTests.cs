using FluentAssertions;
using Xunit;

namespace FormaturasFlow.Api.UnitTests.Domain;

public class ContratoRateioTests
{
    private static (decimal parcela, decimal resto) Ratear(decimal total, decimal entrada, int n)
    {
        var saldo = total - entrada;
        var parcela = Math.Round(saldo / n, 2);
        var resto = saldo - (parcela * n);
        return (parcela, resto);
    }

    [Fact]
    public void Divisao_Exata_Sem_Resto()
    {
        var (parcela, resto) = Ratear(total: 900m, entrada: 0m, n: 3);

        parcela.Should().Be(300m);
        resto.Should().Be(0m);
    }

    [Fact]
    public void Divisao_Nao_Exata_Sobra_Centavos_Na_Ultima()
    {
        var (parcela, resto) = Ratear(total: 100m, entrada: 0m, n: 3);

        parcela.Should().Be(33.33m);
        resto.Should().Be(0.01m);

        var ultima = parcela + resto;
        (parcela * (3 - 1) + ultima).Should().Be(100m);
    }

    [Fact]
    public void Com_Entrada_Reduz_Saldo_A_Ratear()
    {
        var (parcela, resto) = Ratear(total: 1200m, entrada: 300m, n: 3);

        parcela.Should().Be(300m);
        resto.Should().Be(0m);
    }

    [Fact]
    public void Uma_Parcela_Recebe_Tudo()
    {
        var (parcela, resto) = Ratear(total: 500.55m, entrada: 100.55m, n: 1);

        parcela.Should().Be(400m);
        resto.Should().Be(0m);
    }

    [Theory]
    [InlineData(1000.00, 0.00, 7)]
    [InlineData(9999.99, 1234.56, 12)]
    [InlineData(0.03, 0.00, 3)]
    public void Soma_Das_Parcelas_Bate_Com_Saldo_Original(decimal total, decimal entrada, int n)
    {
        var (parcela, resto) = Ratear(total, entrada, n);

        var soma = parcela * (n - 1) + (parcela + resto);
        soma.Should().Be(total - entrada);
    }
}
