# Rinha Backend 2025 - .NET

Backend desenvolvido em .NET 9 para a Rinha de Backend 2025, implementando um sistema de processamento de pagamentos.

## Tecnologias

- **.NET 9.0** com Minimal API
- **PostgreSQL** com Dapper
- **Docker** com multi-stage build
- **AOT compilation** para performance

## Endpoints

### POST /payments
```json
{
  "correlationId": "uuid",
  "amount": 100.50
}
```

### GET /payments-summary
```
/payments-summary?from=2025-01-01&to=2025-01-31
```

## Execução

### Docker
```bash
docker compose up --build
```

### Local
```bash
cd RinhaBackend.Net
dotnet run
```

## Configuração

```json
{
  "ConnectionStrings": {
    "PostgresConnection": "Host=localhost;Database=rinha;Username=postgres;Password=postgres"
  },
  "PaymentProcessorDefault": {
    "BaseUrl": "http://payment-processor-default:8080"
  },
  "PaymentProcessorFallback": {
    "BaseUrl": "http://payment-processor-fallback:8080"
  }
}
```

## Teste

Para executar os testes da Rinha:

1. Inicie os payment processors:
```bash
cd rinha-de-backend-2025/payment-processor
docker compose up -d
```

2. Execute o backend na porta 9999
3. Execute os testes:
```bash
cd rinha-de-backend-2025/rinha-test
k6 run rinha.js
```