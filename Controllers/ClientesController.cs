using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CompraProgramada.Data;

[ApiController]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _context;
    public ClientesController(AppDbContext context) => _context = context;

    [HttpPost] // POST /api/clientes (adesao)
    public async Task<IActionResult> Post(Cliente cliente)
    {
        // RN-001: Cadastro com Nome, CPF, Email e ValorMensal.
        cliente.Cpf = NormalizarCpf(cliente.Cpf);

        // RN-003: Validação de aporte mínimo (R$100).
        if (cliente.ValorMensal < 100)
            return BadRequest("Valor mensal minimo para adesao: R$100.");

        // RN-002: CPF único no sistema.
        var cpfJaExiste = await _context.Clientes.AnyAsync(c => c.Cpf == cliente.Cpf);
        if (cpfJaExiste)
            return Conflict("CPF ja cadastrado.");

        // RN-005 e RN-006: Cliente inicia ativo e com data de adesão.
        cliente.Ativo = true;
        cliente.DataAdesao = DateTime.UtcNow;
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        // RN-004: Criação automática de Conta Gráfica Filhote.
        _context.ContasGraficasFilhote.Add(new ContaGraficaFilhote
        {
            ClienteId = cliente.Id,
            DataCriacao = DateTime.UtcNow,
            Ativa = true
        });

        // RN-004: Criação automática de Custódia Filhote (por ativo existente).
        var ativos = await _context.Ativos.Select(a => a.Id).ToListAsync();
        foreach (var ativoId in ativos)
        {
            _context.CustodiasClientes.Add(new CustodiaCliente
            {
                ClienteId = cliente.Id,
                AtivoId = ativoId,
                Quantidade = 0,
                PrecoMedio = 0
            });
        }

        await _context.SaveChangesAsync();
        return Ok(cliente);
    }

    [HttpPut("{id}/cancelar")] // PUT /api/clientes/{id}/cancelar
    public async Task<IActionResult> Cancelar(int id)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null) return NotFound();

        // RN-007: Saída do produto apenas desativa o cliente.
        // RN-008: Custódia permanece após saída (não há remoção de custódia aqui).
        cliente.Ativo = false;
        await _context.SaveChangesAsync();
        return Ok("Cancelado com sucesso.");
    }

    [HttpPut("{id}/aporte")] // PUT /api/clientes/{id}/aporte
    public async Task<IActionResult> UpdateAporte(int id, [FromBody] decimal novoValor)
    {
        // RN-011: Alteração do valor mensal é permitida.
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null) return NotFound();

        // RN-003: Mantém validação de aporte mínimo também na alteração.
        if (novoValor < 100)
            return BadRequest("Valor mensal minimo permitido: R$100.");

        // RN-012: Novo valor passa a ser usado na próxima execução do motor.
        cliente.ValorMensal = novoValor;
        await _context.SaveChangesAsync();
        return Ok(cliente);
    }

    private static string NormalizarCpf(string cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf)) return string.Empty;
        return new string(cpf.Where(char.IsDigit).ToArray());
    }
}
