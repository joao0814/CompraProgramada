namespace CompraProgramada.Models
{

  public class CustodiaCliente
  {
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public int AtivoId { get; set; }
    public decimal Quantidade { get; set; }
    public decimal PrecoMedio { get; set; }
  }

}