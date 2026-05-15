# PortableContextMcpServer

A complete local .NET MCP (Model Context Protocol) server that manages personal context with semantic search capabilities. Stores data in SQLite with optional native vector indexing via sqlite-vec.

## Overview

PortableContextMcpServer solves the problem of AI context loss when switching between tools (Claude Desktop, Cursor, VS Code Agent, etc.). It's a personal context layer that any MCP-compatible tool can query in real-time without repeating information.

## Key Features

- **Standard MCP Protocol** — Implements Model Context Protocol over stdio/JSON-RPC
- **Local-First Design** — All data stays on your device, never sent to external services
- **Semantic Search** — Vector-based similarity search for intelligent context retrieval
- **Optional sqlite-vec** — Native SQLite vector extension for high-performance search
- **Privacy Controls** — Fine-grained per-tool visibility policies
- **Docker Ready** — Containerized deployment with persistent storage
- **Fully Tested** — Comprehensive unit and integration tests with CI/CD

## Project Structure

```
portable-context-mcp-server/
├── src/PortableContextMcpServer/     # Main application
│   ├── Models/                       # Data models (ContextEntry, VisibilityPolicy, etc.)
│   ├── Services/                     # Core services (embedding, storage, vector search)
│   ├── Handlers/                     # MCP request handling
│   ├── Contracts/                    # Request/response DTOs
│   └── Program.cs                    # Application entry point
├── tests/PortableContextMcpServer.Tests/  # Unit and integration tests
├── docker/                           # Docker configuration
│   ├── Dockerfile                    # Container image build
│   └── docker-compose.yml            # Local deployment
├── .github/workflows/                # GitHub Actions CI
└── README.md                         # This file
```

## Installation

### Prerequisites
- .NET 8.0 SDK or later
- Docker (optional, for containerized deployment)

### Build from Source

```bash
cd portable-context-mcp-server
dotnet build src/PortableContextMcpServer/PortableContextMcpServer.csproj --configuration Release
```

## Usage

### Local Development (stdio MCP Server)

```bash
cd portable-context-mcp-server
dotnet run --project src/PortableContextMcpServer/PortableContextMcpServer.csproj
```

The server listens on **standard input/output** for JSON-RPC 2.0 MCP requests.

### Docker Compose

Deploy with automatic data persistence:

```bash
docker compose up --build
```

## MCP Interface

The server exposes two core tools via MCP protocol:

### Tool: `get_context`

Retrieve personal context by semantic similarity.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "get_context",
    "arguments": {
      "query": "my preferences for code style",
      "toolId": "claude-desktop",
      "maxResults": 5
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "type": "text",
    "text": "{\"entries\":[{\"id\":\"...\",\"category\":\"preference\",\"text\":\"...\",\"sourceT
ool\":\"...\",\"createdAt\":\"...\",\"updatedAt\":\"...\",\"score\":0.95}]}"
  }
}
```

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `query` | string | Yes | Semantic query to search context |
| `toolId` | string | Yes | Identifier of the calling tool |
| `maxResults` | integer | No | Max results to return (1-100, default: 10) |

### Tool: `update_context`

Save or update a personal context entry.

**Request:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "update_context",
    "arguments": {
      "text": "I prefer functional programming with immutable data structures",
      "category": "preference",
      "sourceTool": "claude-desktop"
    }
  }
}
```

**Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "type": "text",
    "text": "{\"success\":true,\"entryId\":\"...\",\"message\":\"Context entry saved.\"}"
  }
}
```

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | Context content to save |
| `category` | string | Yes | Category (preference, personality, skill, experience, relationship, memory) |
| `sourceTool` | string | Yes | Tool that provided this context |

## Configuration

Configure the service via environment variables:

| Variable | Type | Default | Purpose |
|----------|------|---------|---------|
| `DATABASE__PATH` | string | `./data/context.db` | SQLite database file location |
| `SERVER__PORT` | integer | `6700` | Reserved for future HTTP mode |

**Example `.env` file:**
```bash
DATABASE__PATH=./data/context.db
SERVER__PORT=6700
```

## Vector Search

### Embedding Strategy

The server generates embeddings using **SHA256-based deterministic hashing** normalized to unit vectors. This provides:
- **Offline compatibility** — No external API calls needed
- **Deterministic results** — Same input always produces same embedding
- **Fast computation** — Local CPU-only processing

### Search Methods

**Without sqlite-vec (In-Memory):**
- Cosine similarity over all stored vectors
- O(n) time complexity per search
- No database indexing overhead

**With sqlite-vec (Optional):**
- Native SQLite vector extension for semantic search
- Indexed vector storage for faster retrieval
- Automatic extension detection and loading

### Enabling sqlite-vec

To use the sqlite-vec extension:

1. Build or download `sqlite-vec` binaries for your platform
2. Ensure the extension is accessible to SQLite (e.g., in system library paths)
3. Restart the application — it will auto-detect and load the extension

The server gracefully falls back to in-memory search if the extension is unavailable.

## Privacy Model

### Context Visibility

Each context entry has a `VisibilityPolicy` that controls which tools can access it:

```csharp
new VisibilityPolicy
{
    AllowedCategories = new[] { "preference", "skill" },
    AllowedToolIds = new[] { "claude-desktop", "cursor" }
}
```

- **Empty AllowedCategories** — All categories are visible
- **Empty AllowedToolIds** — All tools are allowed
- **Default policy** — All standard categories visible to all tools

### Supported Categories

- `preference` — User preferences and style choices
- `personality` — Personality traits and quirks
- `skill` — Technical or professional skills
- `experience` — Past work experience and achievements
- `relationship` — Information about important relationships
- `memory` — Important events, decisions, and key facts

## Testing

Run the complete test suite:

```bash
dotnet test tests/PortableContextMcpServer.Tests/PortableContextMcpServer.Tests.csproj --configuration Release
```

**Coverage:**
- Repository and storage operations
- Embedding and vector search
- Policy enforcement and visibility filtering
- MCP request handling and response serialization

## CI/CD

GitHub Actions automatically:
- Builds the solution
- Runs all tests
- Reports code coverage
- Triggered on `push` and `pull_request`

View workflows in `.github/workflows/ci.yml`

## Architecture & Design

### Principles

- **SOLID** — Single responsibility, Open/closed, Liskov substitution, Interface segregation, Dependency inversion
- **Dependency Injection** — Loosely coupled, testable services
- **Minimal Comments** — Self-documenting code with clear names
- **Offline First** — No external dependencies or API calls

### Core Services

- **`IEmbeddingService`** — Converts text to vector embeddings
- **`IContextRepository`** — SQLite storage and retrieval
- **`IPolicyService`** — Visibility policy enforcement
- **`IVectorSearchService`** — Similarity search (in-memory or sqlite-vec)
- **`StdioMcpServer`** — MCP protocol handler over stdio
- **`McpRequestHandler`** — Business logic for get/update operations

## Deployment Scenarios

### Local Development
```bash
dotnet run --project src/PortableContextMcpServer/PortableContextMcpServer.csproj
```

### Docker Container
```bash
docker compose up --build
```

### Cloud (Future)
The architecture supports:
- Custom transport layers (HTTP, gRPC, etc.)
- External storage backends (PostgreSQL, MongoDB)
- Distributed embedding services
- Multi-user deployments

## Future Enhancements

- Real-time context synchronization across devices
- Advanced tagging and metadata support
- Context expiry and archival
- Multi-user and organization support
- CLI management tool for context administration
- Alternative embedding models (local transformers, etc.)

## License

MIT License — See LICENSE file for details

## Contributing

Contributions are welcome! Please ensure:
- All tests pass
- Code follows SOLID principles
- New features include tests
- Documentation is updated

## Support

For issues, questions, or feature requests, open an issue on GitHub.
