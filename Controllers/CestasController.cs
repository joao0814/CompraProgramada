using CompraProgramada.Data;
using CompraProgramada.Services;
using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class CestasController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CotacaoService _cotacaoService;
    private readonly IrFiscalService _irFiscalService;

    public CestasController(AppDbContext context, CotacaoService cotacaoService, IrFiscalService irFiscalService)
    {
        _context = context;
        _cotacaoService = cotacaoService;
        _irFiscalService = irFiscalService;
    }

    [HttpPost] // POST /api/cesta
    public async Task<IActionResult> Post(CestaTopFive cesta)
    {
        // RN-014: Cesta contém exatamente 5 ativos.
        if (cesta.Itens == null || cesta.Itens.Count != 5)
            return BadRequest("A cesta deve conter exatamente 5 ativos.");

        if (cesta.Itens.Select(i => i.AtivoId).Distinct().Count() != 5)
            return BadRequest("A cesta deve conter 5 ativos distintos.");

        // RN-016: Percentuais > 0.
        if (cesta.Itens.Any(i => i.Percentual <= 0))
            return BadRequest("Todos os percentuais devem ser maiores que zero.");

        // RN-015: Soma dos percentuais = 100%.
        var soma = cesta.Itens.Sum(i => i.Percentual);
        if (soma != 1.0m)
            return BadRequest($"A soma e {soma}. Deve ser exatamente 1.0 (100%).");

        await using var tx = await _context.Database.BeginTransactionAsync();

        var cestaAtual = await _context.Cestas
            .Include(c => c.Itens)
            .FirstOrDefaultAsync(c => c.Ativa);

        var itensAnteriores = cestaAtual?.Itens
            .Select(i => new ItemCesta { AtivoId = i.AtivoId, Percentual = i.Percentual })
            .ToList()
            ?? new List<ItemCesta>();

        // RN-017 e RN-018: Nova cesta desativa a anterior e mantém apenas uma ativa.
        if (cestaAtual != null)
            cestaAtual.Ativa = false;

        cesta.Ativa = true;
        cesta.DataInicio = DateTime.UtcNow;
        _context.Cestas.Add(cesta);

        // RN-045: Alteração da cesta dispara rebalanceamento.
        if (HouveAlteracaoDeCesta(itensAnteriores, cesta.Itens))
        {
            await RebalancearAposTrocaCesta(itensAnteriores, cesta.Itens);
        }

        await _context.SaveChangesAsync();

        var totalAtivas = await _context.Cestas.CountAsync(c => c.Ativa);
        if (totalAtivas != 1)
        {
            await tx.RollbackAsync();
            return StatusCode(500, "Falha ao garantir apenas uma cesta ativa.");
        }

        await tx.CommitAsync();
        return Ok(cesta);
    }

    [HttpGet("atual")] // GET /api/cesta/atual
    public async Task<IActionResult> GetAtual()
    {
        var cesta = await _context.Cestas
            .Include(c => c.Itens)
            .FirstOrDefaultAsync(c => c.Ativa);

        if (cesta == null) return NotFound("Nenhuma cesta ativa.");
        return Ok(cesta);
    }

    private async Task RebalancearAposTrocaCesta(List<ItemCesta> itensAnteriores, List<ItemCesta> itensNovos)
    {
        var idsAnteriores = itensAnteriores.Select(i => i.AtivoId).ToHashSet();
        var idsNovos = itensNovos.Select(i => i.AtivoId).ToHashSet();
        var idsRemovidos = idsAnteriores.Except(idsNovos).ToHashSet(); // RN-046: identificar ativos removidos.
        var percentuaisNovos = itensNovos.ToDictionary(i => i.AtivoId, i => i.Percentual);

        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        foreach (var cliente in clientes)
        {
            decimal caixaDeVendasNoRebalanceamento = 0m;

            var custodiasCliente = await _context.CustodiasClientes
                .Where(cu => cu.ClienteId == cliente.Id && cu.Quantidade > 0)
                .ToListAsync();

            var precoAtivoCache = new Dictionary<int, decimal>();

            // RN-047: vender posição completa dos ativos removidos.
            foreach (var custodia in custodiasCliente.Where(c => idsRemovidos.Contains(c.AtivoId)).ToList())
            {
                var precoVenda = await ObterPrecoAtivo(custodia.AtivoId, precoAtivoCache);
                if (precoVenda <= 0) continue;

                var qtdVenda = decimal.Truncate(custodia.Quantidade);
                if (qtdVenda <= 0) continue;

                var valorVenda = qtdVenda * precoVenda;
                var custoAquisicao = qtdVenda * custodia.PrecoMedio;
                // RN-060: Lucro = ValorVenda - (Qtd × PM).
                var lucroOperacao = valorVenda - custoAquisicao;

                caixaDeVendasNoRebalanceamento += valorVenda;

                _context.Movimentacoes.Add(new Movimentacao
                {
                    ClienteId = cliente.Id,
                    AtivoId = custodia.AtivoId,
                    Tipo = "Venda",
                    Quantidade = qtdVenda,
                    PrecoUnitario = precoVenda,
                    Data = DateTime.UtcNow
                });

                // RN-057/58/59/61: apuração mensal de IR sobre vendas (até 20k isento, acima 20% sobre lucro, prejuízo = 0).
                var impostoIncremental = await _irFiscalService.RegistrarVendaECalcularImpostoIncrementalAsync(
                    cliente.Id,
                    valorVenda,
                    lucroOperacao,
                    DateTime.UtcNow);

                if (impostoIncremental > 0)
                {
                    await PublicarKafka(cliente.Id, "DARF_IR_20_LUCRO", impostoIncremental);
                }

                custodia.Quantidade -= qtdVenda;
                if (custodia.Quantidade <= 0)
                    _context.CustodiasClientes.Remove(custodia);
            }

            var custodiasNovosAtivos = await _context.CustodiasClientes
                .Where(cu => cu.ClienteId == cliente.Id && idsNovos.Contains(cu.AtivoId))
                .ToListAsync();

            decimal valorTotalCarteiraRebalanceavel = 0m;
            foreach (var itemNovo in itensNovos)
            {
                var preco = await ObterPrecoAtivo(itemNovo.AtivoId, precoAtivoCache);
                if (preco <= 0) continue;

                var qtdAtual = custodiasNovosAtivos
                    .FirstOrDefault(c => c.AtivoId == itemNovo.AtivoId)?.Quantidade ?? 0m;
                valorTotalCarteiraRebalanceavel += qtdAtual * preco;
            }

            // Inclui caixa gerado nas vendas de removidos para redistribuir em novos/ajustes
            valorTotalCarteiraRebalanceavel += caixaDeVendasNoRebalanceamento;

            // RN-048: comprar novos ativos.
            // RN-049: ajustar ativos com percentual alterado.
            foreach (var itemNovo in itensNovos)
            {
                var preco = await ObterPrecoAtivo(itemNovo.AtivoId, precoAtivoCache);
                if (preco <= 0) continue;

                var alvoValor = valorTotalCarteiraRebalanceavel * percentuaisNovos[itemNovo.AtivoId];
                var custodia = custodiasNovosAtivos.FirstOrDefault(c => c.AtivoId == itemNovo.AtivoId);
                var qtdAtual = custodia?.Quantidade ?? 0m;
                var valorAtual = qtdAtual * preco;
                var diferencaValor = alvoValor - valorAtual;

                if (diferencaValor > 0)
                {
                    var qtdCompra = decimal.Truncate(diferencaValor / preco);
                    if (qtdCompra <= 0) continue;

                    if (custodia == null)
                    {
                        custodia = new CustodiaCliente
                        {
                            ClienteId = cliente.Id,
                            AtivoId = itemNovo.AtivoId,
                            Quantidade = qtdCompra,
                            PrecoMedio = preco
                        };
                        _context.CustodiasClientes.Add(custodia);
                        custodiasNovosAtivos.Add(custodia);
                    }
                    else
                    {
                        // RN-041/RN-042: PM por ativo com média ponderada após compra.
                        custodia.PrecoMedio =
                            ((custodia.Quantidade * custodia.PrecoMedio) + (qtdCompra * preco))
                            / (custodia.Quantidade + qtdCompra);
                        custodia.Quantidade += qtdCompra;
                    }

                    _context.Movimentacoes.Add(new Movimentacao
                    {
                        ClienteId = cliente.Id,
                        AtivoId = itemNovo.AtivoId,
                        Tipo = "Compra",
                        Quantidade = qtdCompra,
                        PrecoUnitario = preco,
                        Data = DateTime.UtcNow
                    });
                }
                else if (diferencaValor < 0)
                {
                    if (custodia == null || custodia.Quantidade <= 0) continue;

                    var qtdVenda = decimal.Truncate((-diferencaValor) / preco);
                    qtdVenda = Math.Min(qtdVenda, decimal.Truncate(custodia.Quantidade));
                    if (qtdVenda <= 0) continue;

                    var valorVenda = qtdVenda * preco;
                    var custoAquisicao = qtdVenda * custodia.PrecoMedio;
                    // RN-060: Lucro = ValorVenda - (Qtd × PM).
                    var lucroOperacao = valorVenda - custoAquisicao;

                    caixaDeVendasNoRebalanceamento += valorVenda;

                    // RN-043: Venda não altera PM; apenas reduz/remover posição.
                    custodia.Quantidade -= qtdVenda;
                    if (custodia.Quantidade <= 0)
                        _context.CustodiasClientes.Remove(custodia);

                    _context.Movimentacoes.Add(new Movimentacao
                    {
                        ClienteId = cliente.Id,
                        AtivoId = itemNovo.AtivoId,
                        Tipo = "Venda",
                        Quantidade = qtdVenda,
                        PrecoUnitario = preco,
                        Data = DateTime.UtcNow
                    });

                    var impostoIncremental = await _irFiscalService.RegistrarVendaECalcularImpostoIncrementalAsync(
                        cliente.Id,
                        valorVenda,
                        lucroOperacao,
                        DateTime.UtcNow);

                    if (impostoIncremental > 0)
                    {
                        await PublicarKafka(cliente.Id, "DARF_IR_20_LUCRO", impostoIncremental);
                    }
                }
            }
        }
    }

    private async Task<decimal> ObterPrecoAtivo(int ativoId, Dictionary<int, decimal> cache)
    {
        if (cache.TryGetValue(ativoId, out var precoCache)) return precoCache;

        var ativo = await _context.Ativos.FindAsync(ativoId);
        if (ativo == null) return 0m;

        var preco = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
        cache[ativoId] = preco;
        return preco;
    }

    private static bool HouveAlteracaoDeCesta(List<ItemCesta> itensAnteriores, List<ItemCesta> itensNovos)
    {
        if (itensAnteriores.Count != itensNovos.Count) return true;

        var anterior = itensAnteriores.OrderBy(i => i.AtivoId).ToList();
        var nova = itensNovos.OrderBy(i => i.AtivoId).ToList();

        for (var i = 0; i < anterior.Count; i++)
        {
            if (anterior[i].AtivoId != nova[i].AtivoId) return true;
            if (anterior[i].Percentual != nova[i].Percentual) return true;
        }

        return false;
    }

    private static async Task PublicarKafka(int clienteId, string codigo, decimal valorIR)
    {
        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
        using var producer = new ProducerBuilder<Null, string>(config).Build();

        var message = JsonSerializer.Serialize(new
        {
            clienteId,
            ativo = codigo,
            valorIR,
            data = DateTime.UtcNow.ToString("yyyy-MM-dd")
        });

        await producer.ProduceAsync("ir-dedo-duro", new Message<Null, string> { Value = message });
    }
}
