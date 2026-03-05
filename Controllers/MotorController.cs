using CompraProgramada.Data;
using CompraProgramada.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Confluent.Kafka;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class MotorController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly CotacaoService _cotacaoService;
    private readonly MotorCompraService _motorCompraService;
    private readonly IrFiscalService _irFiscalService;

    public MotorController(
        AppDbContext context,
        CotacaoService cotacaoService,
        MotorCompraService motorCompraService,
        IrFiscalService irFiscalService)
    {
        _context = context;
        _cotacaoService = cotacaoService;
        _motorCompraService = motorCompraService;
        _irFiscalService = irFiscalService;
    }

    [HttpPost("executar")]
    public async Task<IActionResult> Executar() 
    {
        var resultado = await _motorCompraService.ExecutarAsync();
        if (resultado == "Sem cesta ou clientes ativos.") return BadRequest(resultado);
        return Ok(resultado);
    }

    [HttpPost("rebalancear")]
    public async Task<IActionResult> Rebalancear()
    {
        var cestaNova = await _context.Cestas.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Ativa);
        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        if (cestaNova == null) return BadRequest("Nenhuma cesta ativa para rebalancear.");

        var idsAtivosNaCesta = cestaNova.Itens.Select(i => i.AtivoId).ToList();

        foreach (var cliente in clientes)
        {
            var custodiasParaVender = await _context.CustodiasClientes
                .Where(cu => cu.ClienteId == cliente.Id && !idsAtivosNaCesta.Contains(cu.AtivoId))
                .ToListAsync();

            foreach (var custodia in custodiasParaVender)
            {
                var ativo = await _context.Ativos.FindAsync(custodia.AtivoId);
                if (ativo == null) continue;

                decimal precoVenda = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);

                if (precoVenda <= 0) continue;

                decimal qtdVenda = decimal.Truncate(custodia.Quantidade);
                if (qtdVenda <= 0) continue;

                decimal valorVenda = qtdVenda * precoVenda;
                decimal custoAquisicao = qtdVenda * custodia.PrecoMedio;
                decimal lucroOperacao = valorVenda - custoAquisicao;

                _context.Movimentacoes.Add(new Movimentacao
                {
                    ClienteId = cliente.Id,
                    AtivoId = ativo.Id,
                    Tipo = "Venda",
                    Quantidade = qtdVenda,
                    PrecoUnitario = precoVenda,
                    Data = DateTime.Now
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

                _context.CustodiasClientes.Remove(custodia);
            }
        }

        await _context.SaveChangesAsync();
        return Ok("Rebalanceamento e apuração de IR concluídos.");
    }
    private async Task PublicarKafka(int clienteId, string codigo, decimal valorIR)
    {
        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
        using var producer = new ProducerBuilder<Null, string>(config).Build();

        var message = System.Text.Json.JsonSerializer.Serialize(new
        {
            clienteId,
            ativo = codigo,
            valorIR,
            data = DateTime.Now.ToString("yyyy-MM-dd")
        });

        await producer.ProduceAsync("ir-dedo-duro", new Message<Null, string> { Value = message });
    }
}
