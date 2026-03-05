namespace CompraProgramada.Models
{
  public class Movimentacao
  {
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int AtivoId { get; set; }
    public string? Tipo { get; set; } // Compra/Venda
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public DateTime Data { get; set; }
  }
}