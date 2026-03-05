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
        // RN-009 e RN-024: Apenas clientes ativos participam do motor.
        var clientes = await _context.Clientes.Where(c => c.Ativo).ToListAsync();

        if (cesta == null || !clientes.Any()) return "Sem cesta ou clientes ativos.";
        // RN-023 e RN-025: Valor mensal dividido em 3 parcelas.
        var totalAportesParcela = clientes.Sum(c => c.ValorMensal / 3m);
        if (totalAportesParcela <= 0) return "Sem aportes válidos para execução.";

        foreach (var item in cesta.Itens)
        {
            var ativo = await _context.Ativos.FindAsync(item.AtivoId);
            if (ativo == null) continue;

            // RN-027: Cotação obtida do arquivo COTAHIST da B3.
            decimal preco = _cotacaoService.ObterPrecoFechamento(ativo.Codigo);
            if (preco <= 0) continue;

            var valorTotalAtivo = totalAportesParcela * item.Percentual;
            // RN-028: Quantidade = TRUNC(valor / cotacao).
            var qtdTotalAtivo = decimal.Truncate(valorTotalAtivo / preco);
            if (qtdTotalAtivo <= 0) continue;

            decimal qtdDistribuida = 0;
            var comprasCliente = new List<(int clienteId, decimal quantidade, decimal valorDisponivel)>();

            foreach (var cliente in clientes)
            {
                var aporteParcelaCliente = cliente.ValorMensal / 3m;
                // RN-034 e RN-035: Distribuição proporcional ao aporte.
                var proporcao = aporteParcelaCliente / totalAportesParcela;
                // RN-036: Quantidade cliente = TRUNC(proporcao * qtd_total).
                var qtdCliente = decimal.Truncate(proporcao * qtdTotalAtivo);
                if (qtdCliente <= 0) continue;

                var valorDisponivel = aporteParcelaCliente * item.Percentual;
                comprasCliente.Add((cliente.Id, qtdCliente, valorDisponivel));
                qtdDistribuida += qtdCliente;
            }

            // RN-039: Resíduos da distribuição permanecem na custódia master.
            var residuoDistribuicao = qtdTotalAtivo - qtdDistribuida;

            var master = await _context.CustodiasMaster.FirstOrDefaultAsync(m => m.AtivoId == ativo.Id);
            var saldoMaster = master?.Quantidade ?? 0m;
            // RN-029 e RN-030: Considera saldo master e reduz quantidade a comprar.
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
                    // RN-041 e RN-042: PM por ativo usando média ponderada.
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

                decimal valorOperacao = compra.quantidade * preco;
                // RN-053 e RN-054: IR dedo-duro 0,005% por operação.
                decimal irDedoDuro = valorOperacao * 0.00005m;
                // RN-055: Publicado no Kafka.
                await PublicarKafka(compra.clienteId, ativo.Codigo, irDedoDuro);
            }

            await AtualizarMaster(
                ativo.Id,
                ativo.Codigo,
                saldoMaster,
                saldoConsumidoMaster,
                qtdParaComprarNoMercado,
                residuoDistribuicao);
            // RN-026: Valores consolidados para uma compra única na conta master por ativo.
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
        // RN-040: Resíduo em master é utilizado no próximo ciclo (via saldoMasterAtual).
        master.Quantidade = saldoMasterAtual - saldoConsumidoMaster + residuoDistribuicao;

        // RN-031: Lote padrão em múltiplos de 100.
        int lotePadrao = ((int)qtdCompradaMercado / 100) * 100;
        // RN-032: Fracionário 1..99.
        int fracionario = (int)(qtdCompradaMercado % 100);
        // RN-033: Ticker fracionário usa sufixo F.
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
