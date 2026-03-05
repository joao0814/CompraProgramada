using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CompraProgramada.Data;

[ApiController]
[Route("api/[controller]")]
public class ClientesController : ControllerBase
{
    private readonly AppDbContext _context;
    public ClientesController(AppDbContext context) => _context = context;

    [HttpPost] // POST /api/clientes (adesão)
    public async Task<IActionResult> Post(Cliente cliente)
    {
        cliente.Ativo = true;
        _context.Clientes.Add(cliente);
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

        cliente.ValorMensal = novoValor;
        await _context.SaveChangesAsync();
        return Ok(cliente);
    }
}
