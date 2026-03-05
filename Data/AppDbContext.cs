using CompraProgramada.Models;
using Microsoft.EntityFrameworkCore;

namespace CompraProgramada.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Cliente> Clientes { get; set; }
    public DbSet<Ativo> Ativos { get; set; }
    public DbSet<CestaTopFive> Cestas { get; set; }
    public DbSet<ItemCesta> ItensCesta { get; set; }
    public DbSet<CustodiaCliente> CustodiasClientes { get; set; }
    public DbSet<CustodiaMaster> CustodiasMaster { get; set; }
    public DbSet<Movimentacao> Movimentacoes { get; set; }

}

public class Cliente
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public decimal ValorMensal { get; set; }
    public bool Ativo { get; set; }
}

public class Ativo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
}

public class CestaTopFive
{
    public int Id { get; set; }
    public DateTime DataInicio { get; set; }
    public bool Ativa { get; set; }
    public List<ItemCesta> Itens { get; set; } = new();
}

public class ItemCesta
{
    public int Id { get; set; }
    public int AtivoId { get; set; }
    public decimal Percentual { get; set; }
}

public class CustodiaCliente
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int AtivoId { get; set; }
    public decimal Quantidade { get; set; }
    public decimal PrecoMedio { get; set; }
}

public class CustodiaMaster
{
    public int Id { get; set; }
    public int AtivoId { get; set; }
    public decimal Quantidade { get; set; }
}

public class Movimentacao
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int AtivoId { get; set; }
    public string Tipo { get; set; } = string.Empty; // Compra/Venda
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public DateTime Data { get; set; }
}
