# ImageAnalysis

Sistema de **análise forense de imagens** desenvolvido em **C# / .NET 9**, cujo objetivo é identificar evidências compatíveis com geração, manipulação ou edição de imagens — combinando metadados, análise JPEG/ELA, ruído e textura, copy-move, proveniência C2PA, análise visual e modelos de detecção de IA.

O sistema não produz um veredito binário (`REAL`/`FAKE`). Em vez disso, apresenta **evidências, regiões suspeitas, níveis de confiança e limitações da análise**, deixando a decisão final para quem avalia o laudo.

> Arquitetura completa e detalhada em [`docs/CLAUDE.md`](docs/CLAUDE.md).

---

## Arquitetura

O projeto segue **Clean Architecture** + **Domain-Driven Design (DDD)**: as camadas internas (`Domain`, `Application`) não conhecem frameworks, bancos de dados ou bibliotecas externas. Infraestrutura e ferramentas externas (PostgreSQL, Redis, ExifTool, OpenCV, modelos de ML, C2PA) implementam as interfaces definidas pelo núcleo.

```text
Frontend → API (ASP.NET Core) → Application (casos de uso / pipeline) → Domain (entidades e regras)
                                                                              ↑
                                                        Infrastructure (Postgres, Redis, Storage, Queue, ferramentas externas)
```

## Estrutura de pastas

```text
ImageAnalysis/
│
├── ImageAnalysis.sln
│
├── src/
│   ├── ImageAnalysis.API/              # Camada de apresentação (ASP.NET Core)
│   │   ├── Controllers/                # Endpoints HTTP (upload, status, resultado)
│   │   ├── Contracts/                  # Request/Response DTOs expostos pela API
│   │   └── Properties/                 # launchSettings, configuração de execução
│   │
│   ├── ImageAnalysis.Application/      # Casos de uso e orquestração do pipeline
│   │   ├── UseCases/                   # CreateImageAnalysis, ProcessImageAnalysis, GetAnalysisResult...
│   │   ├── DTOs/                       # Objetos de transferência entre Application e API
│   │   └── Pipeline/                   # Orquestração da sequência de analisadores
│   │
│   ├── ImageAnalysis.Domain/           # Núcleo do sistema, sem dependências externas
│   │   ├── Entities/                   # Analysis, Finding, Evidence, ImageMetadata...
│   │   ├── ValueObjects/               # BoundingBox, ConfidenceScore, PixelRegion...
│   │   ├── Enums/                      # AnalysisStatus, EvidenceType
│   │   └── Interfaces/                 # IImageAnalyzer, IAnalysisRepository, IImageStorage...
│   │
│   └── ImageAnalysis.Infrastructure/   # Implementações concretas das interfaces do Domain
│       ├── Persistence/                # EF Core + repositórios PostgreSQL
│       ├── Caching/                    # Cache Redis de dados intermediários
│       ├── Storage/                    # Armazenamento de imagens, mapas ELA e heatmaps
│       ├── Messaging/                  # Fila de mensagens e Analysis Worker
│       └── ExternalTools/              # ExifTool, OpenCV, C2PA, modelos de ML
│
├── tests/
│   └── ImageAnalysis.Tests/            # Testes unitários e de integração
│
└── docs/
    └── CLAUDE.md                      # Documento completo da arquitetura forense
```

A regra de dependência é sempre para dentro: `API`/`Infrastructure` → `Application` → `Domain`. O `Domain` nunca referencia `Infrastructure` nem frameworks externos.

## Pré-requisitos

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- Docker e Docker Compose (para PostgreSQL, Redis e RabbitMQ)

## Como subir o ambiente

O `docker-compose.yml` sobe a API e todas as dependências:

| Serviço    | Uso                                      | Porta local                   |
|------------|------------------------------------------|-------------------------------|
| `api`      | ImageAnalysis.API                        | 8080                          |
| `postgres` | Persistência dos resultados              | 5432                          |
| `redis`    | Cache de dados intermediários            | 6379                          |
| `rabbitmq` | Fila de mensagens para o Analysis Worker | 5672 (painel em 15672)        |

As imagens, mapas ELA e heatmaps ficam no volume `storage-data`, montado em `/data/storage` no container da API.

```bash
# 1. Criar o arquivo de variáveis de ambiente
cp .env.example .env

# 2. Subir tudo
docker compose up -d --build

# 3. Verificar a saúde da API e das dependências
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
```

O banco `imageanalysis` é criado automaticamente pelo container do PostgreSQL na primeira execução. Painel do RabbitMQ: http://localhost:15672 (usuário e senha definidos no `.env`).

```bash
docker compose logs -f api   # acompanhar logs da API
docker compose down          # parar (mantém os dados)
docker compose down -v       # parar e apagar os volumes (banco, fila e storage)
```

### Variáveis de ambiente

Documentadas em [`.env.example`](.env.example). A API recebe a configuração via variáveis no formato do ASP.NET Core:

| Variável                       | Descrição                                   |
|--------------------------------|---------------------------------------------|
| `ConnectionStrings__Postgres`  | Connection string do PostgreSQL             |
| `ConnectionStrings__Redis`     | Endereço do Redis (`host:porta`)            |
| `ConnectionStrings__RabbitMq`  | URI AMQP do RabbitMQ                        |
| `Storage__RootPath`            | Diretório raiz do object storage local      |

### Health checks

- `GET /health/live` — a API está no ar (não consulta dependências).
- `GET /health/ready` — verifica PostgreSQL, Redis e RabbitMQ e retorna o status de cada um em JSON.

## Como executar fora do Docker

Para rodar a API com `dotnet run`, suba apenas as dependências; o `appsettings.Development.json` já aponta para `localhost`:

```bash
docker compose up -d postgres redis rabbitmq

# Restaurar e compilar a solução
dotnet build

# Rodar a API
dotnet run --project src/ImageAnalysis.API

# Rodar os testes
dotnet test
```

## Desenvolvimento

O roadmap do projeto está organizado em milestones e issues no GitHub:

- **Issues**: https://github.com/amandalucena/ImageAnalysis/issues
- **Project board**: https://github.com/users/amandalucena/projects/2

Cada milestone (`M0` a `M12`) corresponde a um módulo da arquitetura descrita em [`docs/CLAUDE.md`](docs/CLAUDE.md) (ex.: Metadata Engine, JPEG Forensics/ELA, Evidence Aggregator), e cada issue representa uma tarefa pequena e fechável dentro desse módulo.

## Documentação

- [`docs/CLAUDE.md`](docs/CLAUDE.md) — arquitetura completa do sistema: visão geral, pipeline de análise, ELA, Evidence Aggregator, persistência, testes e validação forense.
