using CompraProgramada.Data;
using CompraProgramada.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class CarteirasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CotacaoService _cotacaoService;

    public CarteirasController(AppDbContext context, CotacaoService cotacaoService)
    {
        _context = context;
        _cotacaoService = cotacaoService;
    }

    [HttpGet("{clienteId}/rentabilidade")]
    public async Task<IActionResult> GetRentabilidade(int clienteId)
    {
        var clienteExiste = await _context.Clientes.AnyAsync(c => c.Id == clienteId);
        if (!clienteExiste) return NotFound("Cliente não encontrado.");

        var custodias = await _context.CustodiasClientes
            .Where(c => c.ClienteId == clienteId && c.Quantidade > 0)
            .ToListAsync();

        var itens = new List<PosicaoCarteiraDto>();
        // RN-063: Valor total da carteira.
        decimal valorTotalCarteira = 0m;
        decimal custoTotalCarteira = 0m;
        // RN-065: P/L total.
        decimal plTotal = 0m;

        foreach (var custodia in custodias)
        {
            var ativo = await _context.Ativos.FindAsync(custodia.AtivoId);
            if (ativo == null) continue;

            var cotacaoAtual = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
            if (cotacaoAtual <= 0) continue;

            var quantidade = custodia.Quantidade;
            // RN-067: Preço médio.
            var precoMedio = custodia.PrecoMedio;
            // RN-069: Cotação atual.
            var custoPosicao = quantidade * precoMedio;
            var valorPosicao = quantidade * cotacaoAtual;
            // RN-064: P/L por ativo.
            var plAtivo = valorPosicao - custoPosicao;

            custoTotalCarteira += custoPosicao;
            valorTotalCarteira += valorPosicao;
            plTotal += plAtivo;

            itens.Add(new PosicaoCarteiraDto
            {
                AtivoId = ativo.Id,
                Codigo = ativo.Codigo,
                Nome = ativo.Nome,
                // RN-068: Quantidade.
                Quantidade = quantidade,
                PrecoMedio = precoMedio,
                CotacaoAtual = cotacaoAtual,
                ValorPosicao = valorPosicao,
                PlAtivo = plAtivo
            });
        }

        // RN-066: Rentabilidade percentual.
        var rentabilidadePercentual =
            custoTotalCarteira > 0
                ? (plTotal / custoTotalCarteira) * 100m
                : 0m;

        var itensComComposicao = itens
            .Select(item =>
            {
                decimal composicaoPercentual =
                    valorTotalCarteira > 0
                        ? (item.ValorPosicao / valorTotalCarteira) * 100m
                        : 0m;
                // RN-070: Composição percentual da carteira.

                return new
                {
                    ativoId = item.AtivoId,
                    codigo = item.Codigo,
                    nome = item.Nome,
                    quantidade = item.Quantidade,
                    precoMedio = item.PrecoMedio,
                    cotacaoAtual = item.CotacaoAtual,
                    valorPosicao = item.ValorPosicao,
                    plAtivo = item.PlAtivo,
                    composicaoPercentual
                };
            })
            .ToList();

        return Ok(new
        {
            clienteId,
            valorTotalCarteira,
            plTotal,
            rentabilidadePercentual,
            ativos = itensComComposicao
        });
    }

    private sealed class PosicaoCarteiraDto
    {
        public int AtivoId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Nome { get; set; } = string.Empty;
        public decimal Quantidade { get; set; }
        public decimal PrecoMedio { get; set; }
        public decimal CotacaoAtual { get; set; }
        public decimal ValorPosicao { get; set; }
        public decimal PlAtivo { get; set; }
    }
}
