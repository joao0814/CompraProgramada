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
        cliente.Cpf = NormalizarCpf(cliente.Cpf);

        if (cliente.ValorMensal < 100)
            return BadRequest("Valor mensal minimo para adesao: R$100.");

        var cpfJaExiste = await _context.Clientes.AnyAsync(c => c.Cpf == cliente.Cpf);
        if (cpfJaExiste)
            return Conflict("CPF ja cadastrado.");

        cliente.Ativo = true;
        cliente.DataAdesao = DateTime.UtcNow;
        _context.Clientes.Add(cliente);
        await _context.SaveChangesAsync();

        _context.ContasGraficasFilhote.Add(new ContaGraficaFilhote
        {
            ClienteId = cliente.Id,
            DataCriacao = DateTime.UtcNow,
            Ativa = true
        });

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

        cliente.Ativo = false;
        await _context.SaveChangesAsync();
        return Ok("Cancelado com sucesso.");
    }

    [HttpPut("{id}/aporte")] // PUT /api/clientes/{id}/aporte
    public async Task<IActionResult> UpdateAporte(int id, [FromBody] decimal novoValor)
    {
        var cliente = await _context.Clientes.FindAsync(id);
        if (cliente == null) return NotFound();

        if (novoValor < 100)
            return BadRequest("Valor mensal minimo permitido: R$100.");

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
