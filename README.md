# Personal Context MCP Server

A self-hosted MCP server for shared personal context across AI clients. It stores preferences, skills, project notes, memories, and reusable context in SQLite, then exposes them through a remote HTTP MCP endpoint.

The project is aimed at people who move between Claude Desktop, Cursor, Copilot, VS Code Agent, and similar tools, and want a consistent context layer without repeating the same background every session.

Data stays in a local folder or self-hosted Docker volume.

## Why This Exists

AI tools do not share short-term memory with each other. Switching between clients often means re-explaining the same identity, preferences, project constraints, and working style, so this server provides a small user-owned context layer that any MCP-compatible client can query.

## Overview

- Shared context for multiple MCP-compatible AI clients
- Local SQLite storage with sqlite-vec vector search
- Hash embeddings by default, optional Ollama embeddings with hash fallback
- Remote MCP endpoint at `/mcp`
- Bearer token protection
- Docker Compose deployment

Current MVP tools:

- `get_context`: search saved context
- `update_context`: save new context

## Quick Start: Hash Mode

Hash mode is the default. It needs no embedding model, so it is the fastest way to confirm the MCP server can be added by a client.

```bash
cd portable-context-mcp-server
export PERSONAL_CONTEXT_MCP_TOKEN="replace-with-a-long-random-token"
docker compose -f docker/docker-compose.yml up --build
```

`PERSONAL_CONTEXT_MCP_TOKEN` is required. Docker Compose will fail fast if it is missing.

The default host port is `8000`; the container still listens on `8080` internally.

Health check:

```bash
curl http://localhost:8000/healthz
```

MCP endpoint:

```text
http://localhost:8000/mcp
```

If port `8000` is already in use, set `HOST_PORT` in `docker/.env` or the shell:

```bash
HOST_PORT=18080 docker compose -f docker/docker-compose.yml up --build
```

SQLite data:

```text
docker/data/context.db
```

## Quick Test With MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

Use:

```text
Transport Type: Streamable HTTP
URL: http://localhost:8000/mcp
```

Add a custom header under Authentication:

```text
Authorization: Bearer replace-with-a-long-random-token
```

![MCP Inspector connection settings](docs/assets/inspector-connection.png)

The tool list should show:

```text
get_context
update_context
```

![MCP Inspector tools list](docs/assets/inspector-tools.png)

Try `update_context`:

```json
{
  "text": "I prefer local-first AI tooling with MCP.",
  "category": "preference",
  "sourceTool": "mcp-inspector"
}
```

Then try `get_context`:

```json
{
  "query": "local-first MCP AI tooling",
  "toolId": "mcp-inspector",
  "maxResults": 5
}
```

![MCP Inspector get_context form](docs/assets/inspector-get-context.png)

## Enable Ollama Embeddings

Hash mode is useful for smoke testing. For real semantic retrieval, run Ollama locally and switch the embedding provider.

Install and start Ollama on macOS:

```bash
brew install ollama
brew services start ollama
ollama pull nomic-embed-text
```

Create or update `docker/.env`:

```bash
PERSONAL_CONTEXT_MCP_TOKEN=replace-with-a-long-random-token
HOST_PORT=8000
EMBEDDING__PROVIDER=Ollama
EMBEDDING__MODEL=nomic-embed-text
EMBEDDING__DIMENSION=768
EMBEDDING__OLLAMA__ENDPOINT=http://host.docker.internal:11434
EMBEDDING__FALLBACKTOHASH=true
```

Restart the server:

```bash
docker compose -f docker/docker-compose.yml down
docker compose -f docker/docker-compose.yml up --build
```

If the database was first created in hash mode with dimension `128`, rebuild the local development database before switching to `768`:

```bash
docker compose -f docker/docker-compose.yml down
rm docker/data/context.db
docker compose -f docker/docker-compose.yml up --build
```

## MCP Client Config

Local:

```json
{
  "url": "http://localhost:8000/mcp",
  "headers": {
    "Authorization": "Bearer replace-with-a-long-random-token"
  }
}
```

Remote:

```json
{
  "url": "https://context.example.com/mcp",
  "headers": {
    "Authorization": "Bearer replace-with-a-long-random-token"
  }
}
```

Use HTTPS for remote deployments.

### Client Compatibility

`localhost:8000` is intended for local clients and local testing tools such as MCP Inspector. Some clients require the MCP URL to be reachable from the public internet. For instance, Claude custom connectors connect from Anthropic's cloud infrastructure, so a local `localhost` URL is not reachable from Claude. Use a public HTTPS deployment, a tunnel, or a local MCP proxy/stdio setup when connecting Claude.

## Configuration

| Variable | Purpose |
| --- | --- |
| `PERSONAL_CONTEXT_MCP_TOKEN` | Required Docker Compose token source |
| `HOST_PORT` | Host port exposed by Docker Compose, default: `8000` |
| `AUTH__TOKENS__0` | Bearer token for `/mcp` |
| `DATABASE__PATH` | SQLite file path |
| `SQLITEVEC__ENABLED` | Load sqlite-vec |
| `SQLITEVEC__REQUIRED` | Fail startup if sqlite-vec cannot load |
| `EMBEDDING__PROVIDER` | `Hash` or `Ollama`; default: `Hash` |
| `EMBEDDING__MODEL` | Hash model label or Ollama embedding model name |
| `EMBEDDING__DIMENSION` | Embedding dimension; must match the selected model |
| `EMBEDDING__FALLBACKTOHASH` | Fall back to hash embeddings when Ollama is unavailable |
| `EMBEDDING__TIMEOUTSECONDS` | Ollama request timeout |
| `EMBEDDING__OLLAMA__ENDPOINT` | Ollama endpoint, for Docker on macOS usually `http://host.docker.internal:11434` |
| `EMBEDDING__OLLAMA__APIKEY` | Optional bearer token for an Ollama-compatible endpoint |
| `TRANSPORT` | `http` or `stdio` |

`.env.example` is a template. Real secrets belong in `.env` or shell environment variables. `.env` is ignored by git.

For Docker Compose, local values can be exported in the shell or placed in `docker/.env`.

Minimal hash-mode `docker/.env`:

```bash
PERSONAL_CONTEXT_MCP_TOKEN=replace-with-a-long-random-token
HOST_PORT=8000
```

Ollama `docker/.env`:

```bash
PERSONAL_CONTEXT_MCP_TOKEN=replace-with-a-long-random-token
HOST_PORT=8000
EMBEDDING__PROVIDER=Ollama
EMBEDDING__MODEL=nomic-embed-text
EMBEDDING__DIMENSION=768
EMBEDDING__OLLAMA__ENDPOINT=http://host.docker.internal:11434
EMBEDDING__FALLBACKTOHASH=true
```

If Ollama is not configured, unavailable, or returns the wrong vector size, the server falls back to the deterministic hash embedding provider when `EMBEDDING__FALLBACKTOHASH=true`.

The database records the embedding dimension at initialization. If `EMBEDDING__DIMENSION` changes later, startup fails with a clear dimension mismatch error. Keep the old dimension, or rebuild/re-embed the database. For a disposable Docker development database:

```bash
docker compose -f docker/docker-compose.yml down
rm docker/data/context.db
docker compose -f docker/docker-compose.yml up --build
```

## Troubleshooting

**Inspector shows OAuth or 404 errors**

Use the custom `Authorization` header. This server uses bearer-token auth, not OAuth:

```text
Authorization: Bearer replace-with-a-long-random-token
```

Do not use the Inspector proxy token as the server token.

**Changing from hash to Ollama fails with a dimension mismatch**

The database was created with a different embedding dimension. Hash defaults to `128`; `nomic-embed-text` uses `768`. For local development, delete the disposable database and start again:

```bash
docker compose -f docker/docker-compose.yml down
rm docker/data/context.db
docker compose -f docker/docker-compose.yml up --build
```

## Development

```bash
dotnet build PortableContextMcpServer.sln --configuration Release
dotnet test tests/PortableContextMcpServer.Tests/PortableContextMcpServer.Tests.csproj --configuration Release
```

Run HTTP locally:

```bash
AUTH__TOKENS__0="replace-with-a-long-random-token" \
dotnet run --project src/PortableContextMcpServer/PortableContextMcpServer.csproj --configuration Release
```

Run stdio locally:

```bash
dotnet run --project src/PortableContextMcpServer/PortableContextMcpServer.csproj --configuration Release -- --transport stdio
```

## Structure

```text
portable-context-mcp-server/
├── src/PortableContextMcpServer/
│   ├── Configuration/
│   ├── Contracts/
│   ├── Handlers/
│   ├── Models/
│   ├── Services/
│   ├── Tools/
│   └── Program.cs
├── tests/PortableContextMcpServer.Tests/
├── docker/
├── .env.example
└── appsettings.example.json
```
