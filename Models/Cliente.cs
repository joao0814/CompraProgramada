namespace CompraProgramada.Models
{

  public class Cliente
  {
    public int Id { get; set; }
    public string? Nome { get; set; }
    public decimal ValorMensal { get; set; }
    public bool Ativo { get; set; }
  }
}