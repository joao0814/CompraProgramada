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

    public CestasController(AppDbContext context, CotacaoService cotacaoService)
    {
        _context = context;
        _cotacaoService = cotacaoService;
    }

    [HttpPost] // POST /api/cesta
    public async Task<IActionResult> Post(CestaTopFive cesta)
    {
        if (cesta.Itens == null || cesta.Itens.Count != 5)
            return BadRequest("A cesta deve conter exatamente 5 ativos.");

        if (cesta.Itens.Select(i => i.AtivoId).Distinct().Count() != 5)
            return BadRequest("A cesta deve conter 5 ativos distintos.");

        if (cesta.Itens.Any(i => i.Percentual <= 0))
            return BadRequest("Todos os percentuais devem ser maiores que zero.");

        var soma = cesta.Itens.Sum(i => i.Percentual);
        if (soma != 1.0m)
            return BadRequest($"A soma e {soma}. Deve ser exatamente 1.0 (100%).");

        await using var tx = await _context.Database.BeginTransactionAsync();

        var cestasAtivas = await _context.Cestas.Where(c => c.Ativa).ToListAsync();
        foreach (var atual in cestasAtivas)
            atual.Ativa = false;

        cesta.Ativa = true;
        cesta.DataInicio = DateTime.UtcNow;
        _context.Cestas.Add(cesta);

        var idsAtivosNaCesta = cesta.Itens.Select(i => i.AtivoId).ToList();
        await RebalancearAposTrocaCesta(idsAtivosNaCesta);

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

    private async Task RebalancearAposTrocaCesta(List<int> idsAtivosNaCesta)
    {
        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        foreach (var cliente in clientes)
        {
            var custodiasParaVender = await _context.CustodiasClientes
                .Where(cu => cu.ClienteId == cliente.Id && !idsAtivosNaCesta.Contains(cu.AtivoId))
                .ToListAsync();

            decimal lucroTotalVendasMes = 0;
            decimal volumeTotalVendasMes = 0;

            foreach (var custodia in custodiasParaVender)
            {
                var ativo = await _context.Ativos.FindAsync(custodia.AtivoId);
                if (ativo == null) continue;

                decimal precoVenda = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
                if (precoVenda <= 0) continue;

                decimal valorVenda = custodia.Quantidade * precoVenda;
                decimal custoAquisicao = custodia.Quantidade * custodia.PrecoMedio;
                decimal lucroOperacao = valorVenda - custoAquisicao;

                lucroTotalVendasMes += lucroOperacao;
                volumeTotalVendasMes += valorVenda;

                _context.Movimentacoes.Add(new Movimentacao
                {
                    ClienteId = cliente.Id,
                    AtivoId = ativo.Id,
                    Tipo = "Venda",
                    Quantidade = custodia.Quantidade,
                    PrecoUnitario = precoVenda,
                    Data = DateTime.UtcNow
                });

                _context.CustodiasClientes.Remove(custodia);
            }

            if (volumeTotalVendasMes > 20000 && lucroTotalVendasMes > 0)
            {
                decimal imposto20 = lucroTotalVendasMes * 0.20m;
                await PublicarKafka(cliente.Id, "DARF_IR_20_LUCRO", imposto20);
            }
        }
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
