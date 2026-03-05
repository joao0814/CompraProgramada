using CompraProgramada.Data;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace CompraProgramada.Services;

public class MotorCompraService
{
    private readonly AppDbContext _context;
    private readonly CotacaoService _cotacaoService;

    public MotorCompraService(AppDbContext context, CotacaoService cotacaoService)
    {
        _context = context;
        _cotacaoService = cotacaoService;
    }

    public async Task<string> ExecutarAsync()
    {
        var cesta = await _context.Cestas.Include(c => c.Itens).FirstOrDefaultAsync(c => c.Ativa);
        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        if (cesta == null || !clientes.Any()) return "Sem cesta ou clientes ativos.";
        var totalAportesParcela = clientes.Sum(c => c.ValorMensal / 3m);
        if (totalAportesParcela <= 0) return "Sem aportes válidos para execução.";

        foreach (var item in cesta.Itens)
        {
            var ativo = await _context.Ativos.FindAsync(item.AtivoId);
            if (ativo == null) continue;

            decimal preco = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
            if (preco <= 0) continue;

            var valorTotalAtivo = totalAportesParcela * item.Percentual;
            var qtdTotalAtivo = decimal.Truncate(valorTotalAtivo / preco);
            if (qtdTotalAtivo <= 0) continue;

            decimal qtdDistribuida = 0;
            var comprasCliente = new List<(int clienteId, decimal quantidade, decimal valorDisponivel)>();

            foreach (var cliente in clientes)
            {
                var aporteParcelaCliente = cliente.ValorMensal / 3m;
                var proporcao = aporteParcelaCliente / totalAportesParcela;
                var qtdCliente = decimal.Truncate(proporcao * qtdTotalAtivo);
                if (qtdCliente <= 0) continue;

                var valorDisponivel = aporteParcelaCliente * item.Percentual;
                comprasCliente.Add((cliente.Id, qtdCliente, valorDisponivel));
                qtdDistribuida += qtdCliente;
            }

            var residuoDistribuicao = qtdTotalAtivo - qtdDistribuida;

            var master = await _context.CustodiasMaster.FirstOrDefaultAsync(m => m.AtivoId == ativo.Id);
            var saldoMaster = master?.Quantidade ?? 0m;
            var saldoConsumidoMaster = Math.Min(saldoMaster, qtdTotalAtivo);
            var qtdParaComprarNoMercado = qtdTotalAtivo - saldoConsumidoMaster;

            foreach (var compra in comprasCliente)
            {
                var custodia = await _context.CustodiasClientes
                    .FirstOrDefaultAsync(c => c.ClienteId == compra.clienteId && c.AtivoId == ativo.Id);

                if (custodia == null)
                {
                    custodia = new CustodiaCliente
                    {
                        ClienteId = compra.clienteId,
                        AtivoId = ativo.Id,
                        Quantidade = compra.quantidade,
                        PrecoMedio = preco
                    };
                    _context.CustodiasClientes.Add(custodia);
                }
                else
                {
                    custodia.PrecoMedio =
                        ((custodia.Quantidade * custodia.PrecoMedio) + (compra.quantidade * preco))
                        / (custodia.Quantidade + compra.quantidade);
                    custodia.Quantidade += compra.quantidade;
                }

                _context.Movimentacoes.Add(new Movimentacao
                {
                    ClienteId = compra.clienteId,
                    AtivoId = ativo.Id,
                    Tipo = "Compra",
                    Quantidade = compra.quantidade,
                    PrecoUnitario = preco,
                    Data = DateTime.Now
                });

                decimal irDedoDuro = compra.valorDisponivel * 0.00005m;
                await PublicarKafka(compra.clienteId, ativo.Codigo, irDedoDuro);
            }

            await AtualizarMaster(
                ativo.Id,
                ativo.Codigo,
                saldoMaster,
                saldoConsumidoMaster,
                qtdParaComprarNoMercado,
                residuoDistribuicao);
        }

        await _context.SaveChangesAsync();
        return "Motor executado com sucesso.";
    }

    private async Task AtualizarMaster(
        int ativoId,
        string codigoAtivo,
        decimal saldoMasterAtual,
        decimal saldoConsumidoMaster,
        decimal qtdCompradaMercado,
        decimal residuoDistribuicao)
    {
        var master = await _context.CustodiasMaster.FirstOrDefaultAsync(m => m.AtivoId == ativoId);
        if (master == null)
        {
            master = new CustodiaMaster { AtivoId = ativoId, Quantidade = 0 };
            _context.CustodiasMaster.Add(master);
        }

        qtdCompradaMercado = decimal.Truncate(qtdCompradaMercado);
        residuoDistribuicao = decimal.Truncate(residuoDistribuicao);
        master.Quantidade = saldoMasterAtual - saldoConsumidoMaster + residuoDistribuicao;

        int lotePadrao = ((int)qtdCompradaMercado / 100) * 100;
        int fracionario = (int)(qtdCompradaMercado % 100);
        var tickerFracionario = fracionario > 0 ? $"{codigoAtivo}F" : string.Empty;

        Console.WriteLine(
            $"Ativo {ativoId}: saldoMasterConsumido={saldoConsumidoMaster}, " +
            $"qtdCompradaMercado={qtdCompradaMercado}, lotePadrao={lotePadrao}, " +
            $"fracionario={fracionario}, residuo={residuoDistribuicao}, saldoMasterFinal={master.Quantidade}");

        if (lotePadrao > 0)
        {
            Console.WriteLine($"Ordem lote padrão: ticker={codigoAtivo}, quantidade={lotePadrao}");
        }

        if (fracionario is >= 1 and <= 99)
        {
            Console.WriteLine($"Ordem fracionária: ticker={tickerFracionario}, quantidade={fracionario}");
        }
    }

    private static async Task PublicarKafka(int clienteId, string codigo, decimal valorIR)
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
