using Microsoft.Extensions.Hosting;

namespace CompraProgramada.Services;

public class MotorAgendadorService : BackgroundService
{
    private static readonly int[] DiasBaseExecucao = [5, 15, 25];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MotorAgendadorService> _logger;
    private DateOnly? _ultimaDataExecutada;

    public MotorAgendadorService(IServiceScopeFactory scopeFactory, ILogger<MotorAgendadorService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var agora = DateTime.Now;
                var hoje = DateOnly.FromDateTime(agora);

                if (EhDiaDeExecucao(agora.Date) && _ultimaDataExecutada != hoje)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var motor = scope.ServiceProvider.GetRequiredService<MotorCompraService>();

                    var resultado = await motor.ExecutarAsync();
                    _ultimaDataExecutada = hoje;

                    _logger.LogInformation("Execução automática do motor em {Data}: {Resultado}", hoje, resultado);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na execução automática do motor.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private static bool EhDiaDeExecucao(DateTime hoje)
    {
        foreach (var diaBase in DiasBaseExecucao)
        {
            var diaDoMes = Math.Min(diaBase, DateTime.DaysInMonth(hoje.Year, hoje.Month));
            var dataAjustada = new DateTime(hoje.Year, hoje.Month, diaDoMes);

            while (dataAjustada.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                dataAjustada = dataAjustada.AddDays(1);
            }

            if (dataAjustada.Date == hoje.Date)
            {
                return true;
            }
        }

        return false;
    }
}
