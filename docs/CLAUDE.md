# Arquitetura do Sistema de Análise Forense de Imagens

## 1. Visão geral

O projeto consiste em um sistema de análise forense de imagens desenvolvido em **C#/.NET**, cujo objetivo é identificar evidências compatíveis com **geração, manipulação ou edição de imagens**, sem assumir que uma única técnica seja suficiente para determinar a autenticidade de uma imagem.

A solução será baseada na combinação de diferentes fontes de evidência, como:

* Metadados;
* Estrutura e compressão JPEG;
* Análise ELA (Error Level Analysis);
* Análise de ruído e textura;
* Análise de regiões suspeitas;
* Detecção de copy-move;
* Proveniência através de C2PA;
* Análise visual;
* Modelos de detecção de imagens geradas por IA;
* Características de manipulação e transformação da imagem.

O sistema não terá como objetivo produzir apenas um resultado binário como `REAL` ou `FAKE`. Em vez disso, deverá apresentar **evidências, indicadores, regiões potencialmente alteradas, níveis de confiança e limitações da análise**.

A arquitetura será baseada nos princípios de **Domain-Driven Design (DDD)** e **Clean Architecture**, mantendo o domínio independente de frameworks, bancos de dados e ferramentas externas.

---

# 2. Objetivos arquiteturais

A arquitetura foi definida para atender aos seguintes objetivos:

* Separar regras de negócio de detalhes de infraestrutura;
* Permitir a substituição de algoritmos de análise sem alterar o domínio;
* Permitir a inclusão de novos analisadores futuramente;
* Executar análises de forma assíncrona;
* Evitar que processamento pesado bloqueie a API;
* Permitir armazenamento eficiente de dados intermediários;
* Manter os resultados finais persistentes;
* Permitir auditoria das evidências utilizadas;
* Facilitar testes unitários e testes de integração;
* Evitar dependência direta do domínio em bibliotecas externas;
* Permitir evolução futura para modelos de Machine Learning;
* Manter cada etapa da análise com uma responsabilidade bem definida.

---

# 3. Arquitetura geral

A solução será organizada em camadas seguindo os princípios da Clean Architecture:

```text
                    ┌──────────────────────┐
                    │      Frontend        │
                    └──────────┬───────────┘
                               │
                               ▼
                    ┌──────────────────────┐
                    │    API / HTTP        │
                    │  ASP.NET Core        │
                    └──────────┬───────────┘
                               │
                               ▼
              ┌────────────────────────────────┐
              │        Application              │
              │                                │
              │ Use Cases / Application        │
              │ Services / DTOs / Pipeline     │
              └────────────────┬───────────────┘
                               │
                               ▼
              ┌────────────────────────────────┐
              │           Domain               │
              │                                │
              │ Entities / Value Objects       │
              │ Domain Rules / Interfaces      │
              │ Evidence / Analysis            │
              └────────────────┬───────────────┘
                               │
                 ┌─────────────┴──────────────┐
                 ▼                            ▼
       ┌───────────────────┐        ┌───────────────────┐
       │   Infrastructure  │        │ External Analysis │
       │                   │        │    Libraries      │
       │ PostgreSQL        │        │ ExifTool          │
       │ Redis             │        │ OpenCV            │
       │ Storage           │        │ ML Models         │
       │ Queue             │        │ C2PA Libraries    │
       └───────────────────┘        └───────────────────┘
```

A regra fundamental será:

> As camadas internas não devem depender diretamente das camadas externas.

Por exemplo, o domínio não deverá conhecer PostgreSQL, Redis, ExifTool, OpenCV ou ASP.NET Core.

---

# 4. Organização da solução

A solução poderá ser organizada da seguinte forma:

```text
ImageAuthenticity/
│
├── ImageAuthenticity.API/
│
├── ImageAuthenticity.Application/
│
├── ImageAuthenticity.Domain/
│
├── ImageAuthenticity.Infrastructure/
│
└── ImageAuthenticity.Tests/
```

## API

Responsável pela comunicação HTTP com o frontend.

Responsabilidades:

* Receber upload;
* Validar requisição;
* Criar análise;
* Consultar status;
* Consultar resultado;
* Expor endpoints;
* Retornar DTOs.

A API não deverá executar diretamente algoritmos pesados de análise.

---

## Application

Representa os casos de uso do sistema.

Exemplos:

```text
CreateImageAnalysis
ProcessImageAnalysis
GetAnalysisStatus
GetAnalysisResult
GetSuspiciousRegions
```

Também será responsável pela orquestração do pipeline.

A camada Application poderá determinar a sequência:

```text
Upload
   ↓
Criar Analysis
   ↓
Enviar para processamento
   ↓
Executar analisadores
   ↓
Consolidar evidências
   ↓
Persistir resultado
```

---

# 5. Domain

O Domain representa o núcleo do sistema.

Ele conterá conceitos relacionados diretamente ao problema de negócio.

Possíveis entidades:

```text
Analysis
Finding
SuspiciousRegion
ImageMetadata
ElaAnalysis
CompressionAnalysis
C2paAnalysis
AiDetectionResult
```

Possíveis Value Objects:

```text
BoundingBox
ConfidenceScore
ImageDimensions
PixelRegion
AnalysisStatus
EvidenceType
```

O domínio também poderá possuir abstrações como:

```text
IImageAnalyzer
IEvidenceRepository
IAnalysisRepository
IImageStorage
IAnalysisCache
```

Essas interfaces permitem que o domínio e a Application utilizem funcionalidades sem conhecer suas implementações.

---

# 6. Infrastructure

A Infrastructure implementará as interfaces definidas pelas camadas internas.

Exemplos:

```text
PostgreSQL
Redis
Object Storage
Message Queue
ExifTool
OpenCV
C2PA libraries
Machine Learning models
```

Por exemplo:

```text
Domain
   │
   └── IAnalysisRepository
             │
             ▼
Infrastructure
   │
   └── PostgreSQLAnalysisRepository
```

O domínio conhece apenas `IAnalysisRepository`.

Ele não conhece Entity Framework, PostgreSQL ou qualquer outro mecanismo de persistência.

---

# 7. Pipeline de análise

A análise será realizada através de um pipeline.

```text
Imagem
  │
  ▼
Upload
  │
  ▼
Criar Analysis
  │
  ▼
Queue
  │
  ▼
Analysis Worker
  │
  ├── Metadata Analysis
  │
  ├── JPEG Forensics
  │      │
  │      └── ELA
  │             │
  │             ▼
  │      ELA Interpretation
  │             │
  │             ▼
  │      Suspicious Regions
  │
  ├── C2PA Analysis
  │
  ├── Visual Forensics
  │
  └── AI Detection
          │
          ▼
   Evidence Aggregator
          │
          ▼
   Final Analysis Result
          │
          ▼
      PostgreSQL
```

Os módulos podem executar de forma independente quando não houver dependência entre eles.

---

# 8. Processamento assíncrono

O processamento das imagens poderá ser executado de forma assíncrona.

A API não deverá ficar esperando a conclusão de uma análise pesada.

O fluxo será:

```text
Frontend
   │
   │ POST /analyses
   ▼
API
   │
   ├── salva imagem
   ├── cria Analysis
   └── publica mensagem
            │
            ▼
          Queue
            │
            ▼
       Analysis Worker
            │
            ▼
         Pipeline
```

A API poderá retornar inicialmente:

```text
HTTP 202 Accepted
```

junto com o identificador da análise.

O frontend poderá posteriormente consultar:

```text
GET /analyses/{id}
```

ou receber uma atualização através de WebSocket/SignalR em uma evolução futura.

---

# 9. Metadata Engine

O Metadata Engine será responsável pela análise das informações existentes no arquivo.

Poderá utilizar ferramentas como ExifTool.

Serão analisados dados como:

* Make;
* Model;
* Lens;
* DateTimeOriginal;
* CreateDate;
* ModifyDate;
* Software;
* CreatorTool;
* GPS;
* ICC Profile;
* XMP;
* IPTC;
* JFIF;
* Document IDs;
* History;
* Informações de edição.

As informações serão divididas entre:

### Raw metadata

Informação original retornada pela ferramenta.

### Normalized metadata

Informação estruturada pelo sistema.

### Findings

Interpretações relevantes da metadata.

A ausência de metadata não será considerada automaticamente uma evidência de manipulação.

---

# 10. JPEG Forensics Engine

O JPEG Forensics Engine analisará características internas do arquivo JPEG.

Ele poderá ser dividido em:

```text
JpegStructureAnalyzer
CompressionAnalyzer
ELAAnalyzer
CopyMoveDetector
NoiseConsistencyAnalyzer
ImageTransformationAnalyzer
```

Cada componente terá uma responsabilidade específica.

---

# 11. Error Level Analysis (ELA)

O ELA será utilizado para identificar regiões que apresentam diferenças no comportamento de compressão.

O processo conceitual será:

```text
Imagem original
      │
      ▼
Recompressão JPEG controlada
      │
      ▼
Comparação entre imagens
      │
      ▼
Mapa ELA
      │
      ▼
Extração de características
      │
      ▼
Interpretador ELA
      │
      ▼
Regiões candidatas
```

O ELA não será tratado como um detector absoluto de manipulação.

Uma região com alto erro de diferença significa apenas que ela apresenta comportamento diferente sob a análise.

---

# 12. Interpretador ELA

O Interpretador ELA transforma o mapa ELA em informações estatísticas e regiões candidatas.

As imagens poderão ser divididas em janelas locais, por exemplo:

```text
32 × 32 pixels
```

com sobreposição quando necessário.

Para cada região poderão ser extraídas características como:

* Média;
* Mediana;
* Percentil 95;
* Desvio padrão;
* MAD;
* Contraste local;
* Diferença em relação à vizinhança;
* Características de textura;
* Características de borda.

O sistema poderá utilizar estatísticas robustas para identificar regiões que se comportam como outliers.

O resultado dessa etapa será uma lista de **regiões candidatas**, e não uma conclusão de manipulação.

---

# 13. Análise de Regiões Suspeitas

Depois que o Interpretador ELA identificar regiões candidatas, essas regiões serão submetidas a uma análise mais detalhada.

O objetivo é responder:

> A diferença observada na região possui outras evidências que sejam compatíveis com uma possível manipulação?

A análise será composta por quatro etapas principais.

---

## 13.1 Refinamento da região

A primeira etapa determina com maior precisão quais pixels pertencem à região candidata.

O objetivo é evitar que uma simples bounding box represente uma área muito maior que a anomalia real.

Resultado:

```text
BoundingBox
Mask
RegionArea
RegionShape
```

---

## 13.2 Caracterização

A região será analisada em diferentes aspectos:

```text
ELA
Noise
Texture
Edges
Frequency
Compression
```

O objetivo é construir um conjunto de características da região.

Exemplo:

```text
ELA                  → elevado
Noise consistency    → baixa
Texture consistency  → alta
Compression          → inconsistente
Copy-move            → não encontrado
```

---

## 13.3 Comparação regional

A região suspeita não será analisada isoladamente.

Ela será comparada com:

* regiões vizinhas;
* regiões semelhantes;
* regiões com características semelhantes;
* outras partes da imagem.

A finalidade é determinar se o comportamento encontrado é realmente anômalo ou se é uma característica natural daquela parte da fotografia.

---

## 13.4 Consolidação regional

Todas as características serão agrupadas em uma evidência regional.

Exemplo:

```text
SuspiciousRegion

BoundingBox
ELA Evidence
Noise Evidence
Compression Evidence
Texture Evidence
CopyMove Evidence
Regional Comparison
```

O resultado poderá ser:

```text
RequiresFurtherAnalysis
```

ou outra classificação intermediária definida pelo domínio.

Essa etapa não deverá determinar sozinha que a imagem foi manipulada.

---

# 14. Redis

O Redis será utilizado principalmente como **cache e armazenamento temporário de dados intermediários**.

Durante o processamento, diversas etapas podem utilizar repetidamente as mesmas características.

Exemplo:

```text
ELA
 │
 ├── características
 ├── mapa
 ├── regiões
 ├── estatísticas
 └── scores
```

Esses dados podem permanecer temporariamente no Redis enquanto a análise estiver em execução.

Exemplo conceitual:

```text
analysis:{id}:ela
analysis:{id}:regions
analysis:{id}:features
```

Com TTL apropriado, esses dados poderão ser removidos após a conclusão da análise.

O Redis não será considerado a fonte definitiva dos resultados.

---

# 15. PostgreSQL

O PostgreSQL será utilizado como armazenamento persistente dos resultados.

Deverá armazenar informações importantes para:

* Histórico;
* Auditoria;
* Consulta dos resultados;
* Relatórios;
* Evidências;
* Regiões suspeitas;
* Status das análises;
* Resultados dos diferentes módulos.

Exemplo conceitual:

```text
Analysis
   │
   ├── MetadataFindings
   ├── CompressionFindings
   ├── ElaFindings
   ├── SuspiciousRegions
   ├── C2paFindings
   ├── AiDetectionResult
   └── FinalResult
```

Não será necessário armazenar cada pixel ou cada mapa intermediário no PostgreSQL.

As informações grandes, como imagens, máscaras e heatmaps, deverão ser armazenadas em um sistema de armazenamento de arquivos/object storage, enquanto o PostgreSQL manterá suas referências.

---

# 16. Armazenamento de imagens

Os arquivos de imagem deverão ser separados do banco relacional.

Exemplo:

```text
Storage
│
└── analysis-123/
    ├── original.jpg
    ├── ela.png
    ├── heatmap.png
    └── suspicious-region-01.png
```

O PostgreSQL armazenará referências:

```text
Analysis
 ├── OriginalImagePath
 ├── ElaMapPath
 ├── HeatmapPath
 └── ...
```

A imagem original nunca deverá ser sobrescrita durante o processamento.

---

# 17. C2PA

O módulo C2PA será responsável por verificar credenciais e informações de proveniência disponíveis na imagem.

Quando existentes, poderão ser analisados:

* Manifestos;
* Assinaturas;
* Histórico de ações;
* Origem;
* Ferramentas utilizadas;
* Informações de edição.

A ausência de C2PA não será interpretada automaticamente como evidência de falsificação.

Ela significa apenas que não existe uma credencial verificável disponível através desse mecanismo.

---

# 18. AI Image Detector

O AI Image Detector será responsável por analisar características compatíveis com imagens geradas ou modificadas por modelos de inteligência artificial.

O módulo poderá utilizar modelos de Machine Learning especializados.

Seu resultado deverá ser tratado como mais uma fonte de evidência.

Exemplo:

```text
AI Detector
    ↓
Score
    ↓
Evidence
```

O score do modelo não deverá ser automaticamente interpretado como uma probabilidade real de que a imagem seja gerada por IA, a menos que o modelo tenha sido devidamente calibrado e validado para a população de imagens utilizada.

---

# 19. Evidence Aggregator

O Evidence Aggregator será responsável por combinar as evidências produzidas pelos diferentes módulos.

```text
Metadata
    │
JPEG
    │
ELA
    │
Suspicious Regions
    │
C2PA
    │
Visual Forensics
    │
AI Detector
    │
    ▼
Evidence Aggregator
    │
    ▼
Analysis Result
```

Inicialmente, a consolidação poderá utilizar regras explícitas e pesos definidos a partir da validação experimental.

Exemplo conceitual:

```text
Evidence:
- Metadata
- JPEG anomalies
- ELA regions
- Noise inconsistency
- AI detector
- C2PA
```

O Aggregator deverá considerar também evidências conflitantes.

Por exemplo:

```text
ELA                 → forte
Noise               → forte
AI Detector         → fraco
C2PA                → autenticidade verificável
```

Nesse cenário, o sistema não deverá simplesmente somar os scores.

A natureza, confiabilidade e independência das evidências deverão ser consideradas.

---

# 20. Resultado final

O resultado final deverá representar o conjunto de evidências disponíveis.

Em vez de:

```text
FAKE = TRUE
```

o sistema poderá produzir algo como:

```text
Status:
RequiresFurtherInvestigation

Evidence:
- suspicious JPEG compression
- localized ELA anomaly
- inconsistent noise pattern

Suspicious regions:
- Region 01
- Region 02

Confidence:
High / Medium / Low
```

A nomenclatura definitiva deverá ser definida após a validação dos algoritmos.

---

# 21. Modelo conceitual de uma análise

Uma análise poderá ser representada conceitualmente assim:

```text
Analysis
│
├── Image
│
├── Metadata
│
├── JPEG Analysis
│   ├── Structure
│   ├── Compression
│   └── ELA
│       └── Suspicious Regions
│
├── C2PA
│
├── Visual Forensics
│
├── AI Detection
│
└── Evidence
    ├── Findings
    ├── Scores
    ├── Regions
    └── Final Result
```

---

# 22. Princípios de DDD

O projeto utilizará DDD para representar conceitos importantes do domínio.

Alguns conceitos centrais serão:

### Analysis

Representa uma análise completa de uma imagem.

### Finding

Representa uma evidência ou descoberta produzida por um analisador.

### SuspiciousRegion

Representa uma região da imagem que apresenta características que justificam investigação.

### Evidence

Representa uma informação que contribui para a interpretação da análise.

### AnalysisResult

Representa o resultado consolidado da análise.

Esses conceitos pertencem ao domínio independentemente da tecnologia utilizada para armazená-los ou processá-los.

---

# 23. Princípios de Clean Architecture

A arquitetura seguirá principalmente:

### Dependency Rule

As dependências apontam para dentro:

```text
API
 ↓
Application
 ↓
Domain

Infrastructure
 └── implementa contratos utilizados pelo núcleo
```

O Domain não dependerá de Infrastructure.

### Separation of Concerns

Cada componente deverá possuir uma responsabilidade clara.

### Dependency Inversion

Interfaces serão definidas próximas ao domínio/aplicação e implementadas pela infraestrutura.

### Testability

As regras de análise e consolidação poderão ser testadas sem depender de PostgreSQL, Redis ou serviços externos.

---

# 24. Testes

A solução terá diferentes níveis de testes.

## Unit Tests

Para testar:

* Regras do domínio;
* Interpretador ELA;
* Cálculos estatísticos;
* Agrupamento de regiões;
* Regras do Evidence Aggregator;
* Value Objects;
* Casos de borda.

## Integration Tests

Para testar:

* PostgreSQL;
* Redis;
* Storage;
* Queue;
* Integração entre módulos.

## Forensic Validation

Além dos testes tradicionais de software, será necessário validar os algoritmos utilizando datasets controlados.

O conjunto deverá possuir:

```text
1. Imagens originais
2. Imagens recomprimidas
3. Imagens redimensionadas
4. Imagens recortadas
5. Imagens com edição tradicional
6. Imagens com copy-move
7. Imagens com remoção de objetos
8. Imagens com inserção de objetos
9. Imagens modificadas por IA
10. Imagens completamente geradas por IA
```

O sistema deverá ser avaliado considerando métricas como:

* Precision;
* Recall;
* False Positive Rate;
* False Negative Rate;
* F1 Score.

Os thresholds dos algoritmos deverão ser definidos empiricamente a partir desses experimentos.

---

# 25. Evolução futura

A arquitetura permitirá adicionar novos analisadores sem alterar o núcleo do sistema.

Por exemplo:

```text
IImageAnalyzer
      │
      ├── MetadataAnalyzer
      ├── JpegAnalyzer
      ├── ElaAnalyzer
      ├── CopyMoveAnalyzer
      ├── NoiseAnalyzer
      ├── C2paAnalyzer
      ├── AiDetector
      └── FutureAnalyzer
```

Também será possível evoluir o Evidence Aggregator.

Inicialmente:

```text
Rules + Weighted Evidence
```

Futuramente:

```text
Machine Learning
        ↓
Evidence Fusion Model
        ↓
Calibrated Result
```

Essa evolução somente deverá ser adotada quando houver dataset suficiente, validação adequada e calibração do modelo.

---

# 26. Princípio fundamental do sistema

O sistema deverá seguir uma regra central:

> **Nenhuma técnica isolada deverá ser considerada suficiente para determinar a autenticidade de uma imagem.**

ELA, metadados, compressão, C2PA, análise visual e modelos de IA possuem limitações próprias.

O objetivo da arquitetura é justamente permitir que diferentes fontes independentes de evidência sejam analisadas e posteriormente combinadas.

O sistema, portanto, deverá funcionar como uma **plataforma de análise forense multimodal**, na qual cada módulo contribui com evidências específicas para a compreensão da imagem.

```text
                 ┌──────────────┐
                 │    Imagem    │
                 └──────┬───────┘
                        │
        ┌───────────────┼────────────────┐
        │               │                │
        ▼               ▼                ▼
    Metadata          JPEG             C2PA
                        │
                        ▼
                       ELA
                        │
                        ▼
                ELA Interpretation
                        │
                        ▼
              Suspicious Regions
                        │
        ┌───────────────┼──────────────┐
        ▼               ▼              ▼
      Noise          Texture        Copy-Move
        │               │              │
        └───────────────┼──────────────┘
                        │
        ┌───────────────┼──────────────┐
        ▼               ▼              ▼
     Visual          AI Detector    Compression
        │               │              │
        └───────────────┼──────────────┘
                        ▼
                Evidence Aggregator
                        │
                        ▼
                 Final Analysis
                        │
              ┌─────────┴─────────┐
              ▼                   ▼
         PostgreSQL             Storage
```

Essa arquitetura permite que o sistema seja desenvolvido inicialmente com técnicas clássicas de análise forense e, posteriormente, evolua para técnicas mais avançadas de Machine Learning sem que seja necessário reconstruir toda a aplicação.

---

# 27. Nota de nomenclatura neste repositório

O documento acima usa `ImageAuthenticity` como nome de exemplo da solução. Neste repositório o nome do projeto é **`ImageAnalysis`** (nome já definido no GitHub: `amandalucena/ImageAnalysis`). Onde o texto acima menciona `ImageAuthenticity.*`, leia-se `ImageAnalysis.*` (ex.: `ImageAnalysis.API`, `ImageAnalysis.Domain`, etc.).
