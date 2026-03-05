using Xunit;

public class CalculosFinanceirosTests
{
    [Fact]
    public void Preco_Medio_Ponderado()
    {
        decimal qtdA = 10; decimal pmA = 20;
        decimal qtdB = 5; decimal precoB = 35;

        decimal resultado = ((qtdA * pmA) + (qtdB * precoB)) / (qtdA + qtdB);

        Assert.Equal(25m, resultado);
    }

    [Theory]
    [InlineData(25000, 1000, 200)] 
    [InlineData(15000, 1000, 0)]   
    [InlineData(25000, -500, 0)] 
    public void IR_20_Com_Regra_De_Isencao(decimal totalVendas, decimal lucro, decimal impostoEsperado)
    {
        decimal impostoCalculado = (totalVendas > 20000 && lucro > 0) ? lucro * 0.20m : 0;
        Assert.Equal(impostoEsperado, impostoCalculado);
    }

    [Fact]
    public void Separar_Lote_Padrao_E_Fracionario()
    {
        decimal qtdTotal = 253.5m;
        int lotePadrao = (int)(qtdTotal / 100) * 100; 
        decimal fracionario = qtdTotal % 100;         

        Assert.Equal(200, lotePadrao);
        Assert.Equal(53.5m, fracionario);
    }
}
