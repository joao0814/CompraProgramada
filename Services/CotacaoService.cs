namespace CompraProgramada.Services;

public class CotacaoService
{
  private readonly string _diretorioCotacoes = Path.Combine(Directory.GetCurrentDirectory(), "cotacoes");

  public decimal ObterPrecoFechamento(string codigo)
  {
    var arquivo = Directory.GetFiles(_diretorioCotacoes, "COTAHIST*")
                           .OrderByDescending(f => f)
                           .FirstOrDefault();

    if (arquivo == null) throw new FileNotFoundException("Nenhum arquivo COTAHIST encontrado na pasta /cotacoes");

    var linhas = File.ReadLines(arquivo);

    foreach (var linha in linhas)
    {
      var codigoNaLinha = linha.Substring(12, 12).Trim();

      if (codigoNaLinha.Equals(codigo, StringComparison.OrdinalIgnoreCase))
      {
        var precoRaw = linha.Substring(108, 13);

        return decimal.Parse(precoRaw) / 100m;
      }
    }

    return 0m;
  }
}
