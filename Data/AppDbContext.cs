using CompraProgramada.Models;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

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
    public DbSet<ContaGraficaFilhote> ContasGraficasFilhote { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Cliente>()
            .HasIndex(c => c.Cpf)
            .IsUnique();
        modelBuilder.Entity<ContaGraficaFilhote>()
            .HasIndex(c => c.ClienteId)
            .IsUnique();
    }

}

public class Cliente
{
    public int Id { get; set; }
    [Required]
    public string Nome { get; set; } = string.Empty;
    [Required]
    public string Cpf { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Range(100, double.MaxValue, ErrorMessage = "Valor mensal deve ser no minimo R$100.")]
    public decimal ValorMensal { get; set; }
    public bool Ativo { get; set; }
    public DateTime DataAdesao { get; set; }
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

public class ContaGraficaFilhote
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public DateTime DataCriacao { get; set; }
    public bool Ativa { get; set; }
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
