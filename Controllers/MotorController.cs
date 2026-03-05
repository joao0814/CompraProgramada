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
    private readonly IConfiguration _config;

    public MotorController(AppDbContext context, CotacaoService cotacaoService, IConfiguration config)
    {
        _context = context;
        _cotacaoService = cotacaoService;
        _config = config;
    }

    [HttpPost("executar")]
    public async Task<IActionResult> Executar()
    {
        var cesta = await _context.Cestas.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Ativa);
        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        if (cesta == null || !clientes.Any()) return BadRequest("Sem cesta ou clientes ativos.");

        foreach (var item in cesta.Itens)
        {
            var ativo = await _context.Ativos.FindAsync(item.AtivoId);
            decimal preco = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
            if (preco <= 0) continue;

            decimal qtdTotalAtivo = 0;

            foreach (var cliente in clientes)
            {
                decimal valorDisponivel = (cliente.ValorMensal / 3m) * item.Percentual;
                decimal qtdComprada = valorDisponivel / preco;
                qtdTotalAtivo += qtdComprada;

                var custodia = await _context.CustodiasClientes
                    .FirstOrDefaultAsync(c => c.ClienteId == cliente.Id && c.AtivoId == ativo.Id);

                if (custodia == null) {
                    custodia = new CustodiaCliente { ClienteId = cliente.Id, AtivoId = ativo.Id, Quantidade = qtdComprada, PrecoMedio = preco };
                    _context.CustodiasClientes.Add(custodia);
                } else {
                    custodia.PrecoMedio = ((custodia.Quantidade * custodia.PrecoMedio) + (qtdComprada * preco)) / (custodia.Quantidade + qtdComprada);
                    custodia.Quantidade += qtdComprada;
                }

                _context.Movimentacoes.Add(new Movimentacao {
                    ClienteId = cliente.Id, AtivoId = ativo.Id, Tipo = "Compra",
                    Quantidade = qtdComprada, PrecoUnitario = preco, Data = DateTime.Now
                });

                decimal irDedoDuro = valorDisponivel * 0.00005m;
                await PublicarKafka(cliente.Id, ativo.Codigo, irDedoDuro);
            }

            await AtualizarMaster(ativo.Id, qtdTotalAtivo);
        }

        await _context.SaveChangesAsync();
        return Ok("Motor executado com sucesso.");
    }

    private async Task AtualizarMaster(int ativoId, decimal qtdTotal)
    {
        var master = await _context.CustodiasMaster.FirstOrDefaultAsync(m => m.AtivoId == ativoId);
        if (master == null) {
            master = new CustodiaMaster { AtivoId = ativoId, Quantidade = qtdTotal };
            _context.CustodiasMaster.Add(master);
        } else {
            master.Quantidade += qtdTotal;
        }

        int lotePadrao = (int)(qtdTotal / 100) * 100;
        decimal fracionario = qtdTotal % 100;
        Console.WriteLine($"Ativo {ativoId}: Lote Padrão: {lotePadrao} | Fracionário: {fracionario}");
    }

    private async Task PublicarKafka(int clienteId, string codigo, decimal valorIR)
    {
        var config = new ProducerConfig { BootstrapServers = "localhost:9092" };
        using var producer = new ProducerBuilder<Null, string>(config).Build();
        
        var message = System.Text.Json.JsonSerializer.Serialize(new {
            clienteId, ativo = codigo, valorIR, data = DateTime.Now.ToString("yyyy-MM-dd")
        });

        await producer.ProduceAsync("ir-dedo-duro", new Message<Null, string> { Value = message });
    }
}