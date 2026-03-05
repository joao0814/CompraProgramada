# CompraProgramada

Projeto desenvolvido como desafio técnico para simular um sistema de compra programada de ações semelhante ao utilizado por corretoras de investimento.

API em ASP.NET Core para simular compra programada de ativos, com custodia por cliente, custodia master e apuracao de IR.

## Descricao do dominio

O sistema representa uma operacao de investimento recorrente:

- Clientes ativos definem um valor mensal para investir.
- Uma cesta ativa define os ativos e seus pesos percentuais.
- O motor executa compras periodicas consolidando os aportes dos clientes e distribuindo os ativos proporcionalmente.
- As posicoes ficam registradas em custodia por cliente e em uma custodia master consolidada.
- No rebalanceamento, ativos fora da cesta atual podem ser vendidos, com regra fiscal aplicada.

## Arquitetura

- **API REST**: ASP.NET Core (`Controllers/`) para cadastro de clientes, gestao de cesta e execucao do motor.
- **Camada de dados**: Entity Framework Core + Pomelo MySQL (`Data/AppDbContext.cs` + `Migrations/`).
- **Servico de cotacao**: `Services/CotacaoService.cs` para obter preco de fechamento dos ativos.
- **Motor de compra/rebalanceamento**: `Controllers/MotorController.cs`.
- **Mensageria**: Kafka para publicar eventos de IR (topico `ir-dedo-duro`).
- **Infra local**: Docker Compose sobe MySQL + Zookeeper + Kafka (`docker-compose.yml`).
- **Leitura de cotação**: `CotacaoService` faz parse do arquivo COTAHIST da B3 e extrai o preço de fechamento utilizado no cálculo das compras.

## Modelagem das entidades

- `Cliente`: investidor com `Nome`, `ValorMensal` e status `Ativo`.
- `Ativo`: papel negociado (`Codigo`, `Nome`).
- `CestaTopFive`: composicao ativa de investimento (`DataInicio`, `Ativa`, lista de `Itens`).
- `ItemCesta`: item da cesta com `AtivoId` e `Percentual`.
- `CustodiaCliente`: posicao por cliente e ativo (`Quantidade`, `PrecoMedio`).
- `CustodiaMaster`: consolidado total por ativo na operacao (`Quantidade`).
- `Movimentacao`: historico de compra/venda por cliente (`Tipo`, `Quantidade`, `PrecoUnitario`, `Data`).

## Fluxo do motor de compra

1. Busca a cesta ativa e os clientes ativos.
2. Para cada item da cesta, obtem o ativo e preco de fechamento.
3. Calcula o valor consolidado do ciclo somando `(ValorMensal / 3)` de todos os clientes ativos.
4. Aplica os percentuais da cesta sobre o valor consolidado para determinar quanto comprar de cada ativo.
5. Converte o valor em quantidade de ações com base no preço de fechamento.
6. Registra a compra na custódia master.
7. Distribui os ativos proporcionalmente entre os clientes de acordo com o valor de aporte de cada um.
8. Publica evento de IR dedo-duro no Kafka.
9. Atualiza `CustodiaMaster` com quantidade total comprada no ativo.
10. Persiste alteracoes no banco.

## Regras de negocio implementadas

- Deve existir cesta ativa e ao menos um cliente ativo para executar o motor.
- A soma de percentuais da cesta deve ser exatamente `1.0`.
- Apenas uma cesta fica ativa por vez (nova cesta desativa a anterior).
- Cancelamento de cliente marca `Ativo = false`.
- Alteracao de aporte atualiza `ValorMensal`.
- Rebalanceamento vende ativos fora da cesta ativa.
- IR de 20% no rebalanceamento:
  - aplica somente se volume de vendas do mes for maior que `20000`;
  - aplica somente se houver lucro positivo.
- Se nao houver preco valido de cotacao (`<= 0`), o ativo e ignorado no ciclo.

## Decisoes

- **MySQL via Docker** para ambiente local reproduzivel.
- **Kafka** para desacoplar notificacao fiscal da execucao principal.
- **Regra de alocacao proporcional** baseada no percentual da cesta.
- **Preco medio ponderado** para refletir custo de aquisicao acumulado.
- **Configuracao via `.env`** com `DotNetEnv`.

## Estrutura do projeto

```text
CompraProgramada/
|-- Controllers/
|   |-- ClientesController.cs
|   |-- CestasController.cs
|   `-- MotorController.cs
|-- Data/
|   `-- AppDbContext.cs
|-- Models/
|   |-- Cliente.cs
|   |-- Ativo.cs
|   |-- CestaTopFive.cs
|   |-- ItemCesta.cs
|   |-- CustoDiaCliente.cs
|   |-- CustoDiaMaster.cs
|   `-- Movimentacao.cs
|-- Services/
|   `-- CotacaoService.cs
|-- Migrations/
|-- Test/
|   `-- CalculosFinanceirosTests.cs
|-- Program.cs
|-- docker-compose.yml
`-- .env.example
```

## Como rodar Docker

1. Copie o exemplo de variaveis:

```powershell
Copy-Item .env.example .env
```

2. Ajuste o `.env` (portas e credenciais).
3. Suba os servicos:

```powershell
docker compose up -d
```

4. Verifique os containers:

```powershell
docker compose ps
```

## Como rodar API

Pre-requisitos:
- .NET SDK compativel com o projeto (`net10.0`)
- Docker em execucao (MySQL/Kafka iniciados)

Comandos:

```powershell
dotnet restore
dotnet ef database update
dotnet run
```

API local:
- `http://localhost:5232`
- Swagger (Development): `http://localhost:5232/swagger`

## Como executar motor

Endpoint:
- `POST /api/motor/executar`

Exemplo:

```powershell
curl -X POST http://localhost:5232/api/motor/executar
```

Retorno esperado:
- `200 OK` com mensagem de sucesso da execucao.

## Como executar rebalanceamento

Endpoint:
- `POST /api/motor/rebalancear`

Exemplo:

```powershell
curl -X POST http://localhost:5232/api/motor/rebalancear
```

Retorno esperado:
- `200 OK` com mensagem de conclusao do rebalanceamento e apuracao de IR.

## Testes

Os testes atuais cobrem calculos financeiros basicos:

- preco medio ponderado;
- regra de isencao e calculo de IR de 20%;
- separacao entre lote padrao e fracionario.

Para executar:

```powershell
dotnet test
```
