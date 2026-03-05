using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CompraProgramada.Data;

[ApiController]
[Route("api/[controller]")]
public class CestasController : ControllerBase
{
    private readonly AppDbContext _context;
    public CestasController(AppDbContext context) => _context = context;

    [HttpPost] // POST /api/cesta
    public async Task<IActionResult> Post(CestaTopFive cesta)
    {
        // VALIDAÇÃO: Soma dos percentuais deve ser exatamente 1.0 (100%)
        var soma = cesta.Itens.Sum(i => i.Percentual);
        if (soma != 1.0m)
            return BadRequest($"A soma é {soma}. Deve ser exatamente 1.0 (100%).");

        // Desativa a cesta anterior antes de ativar a nova
        var atual = await _context.Cestas.FirstOrDefaultAsync(c => c.Ativa);
        if (atual != null) atual.Ativa = false;

        cesta.Ativa = true;
        cesta.DataInicio = DateTime.Now;

        _context.Cestas.Add(cesta);
        await _context.SaveChangesAsync();
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
}
