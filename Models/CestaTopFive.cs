namespace CompraProgramada.Models
{
  public class CestaTopFive
  {
    public int Id { get; set; }
    public DateTime DataInicio { get; set; }
    public bool Ativa { get; set; }
    public List<ItemCesta> Itens { get; set; } = new List<ItemCesta>();
  }

}