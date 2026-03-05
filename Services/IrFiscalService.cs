using CompraProgramada.Data;
using Microsoft.EntityFrameworkCore;

namespace CompraProgramada.Services;

public class IrFiscalService
{
    private readonly AppDbContext _context;

    public IrFiscalService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<decimal> RegistrarVendaECalcularImpostoIncrementalAsync(
        int clienteId,
        decimal valorVenda,
        decimal lucroOperacao,
        DateTime dataOperacaoUtc)
    {
        var ano = dataOperacaoUtc.Year;
        var mes = dataOperacaoUtc.Month;

        var apuracao = await _context.ApuracoesIrMensais
            .FirstOrDefaultAsync(a => a.ClienteId == clienteId && a.Ano == ano && a.Mes == mes);

        if (apuracao == null)
        {
            apuracao = new ApuracaoIrMensal
            {
                ClienteId = clienteId,
                Ano = ano,
                Mes = mes,
                VolumeVendas = 0,
                LucroRealizado = 0,
                ImpostoCalculado = 0
            };
            _context.ApuracoesIrMensais.Add(apuracao);
        }

        apuracao.VolumeVendas += valorVenda;
        apuracao.LucroRealizado += lucroOperacao;

        var impostoTotalDevido =
            (apuracao.VolumeVendas > 20000m && apuracao.LucroRealizado > 0m)
                ? apuracao.LucroRealizado * 0.20m
                : 0m;

        var impostoIncremental = impostoTotalDevido - apuracao.ImpostoCalculado;
        if (impostoIncremental < 0m)
        {
            impostoIncremental = 0m;
        }

        apuracao.ImpostoCalculado += impostoIncremental;
        return impostoIncremental;
    }
}
