# Personal Context MCP Server

A self-hosted MCP server for shared personal context across AI clients. It stores preferences, skills, project notes, memories, and reusable context in SQLite, then exposes them through a remote HTTP MCP endpoint.

The project is aimed at people who move between Claude Desktop, Cursor, Copilot, VS Code Agent, and similar tools, and want a consistent context layer without repeating the same background every session.

Data stays in a local folder or self-hosted Docker volume.

## Overview

- Shared context for multiple MCP-compatible AI clients
- Local SQLite storage with sqlite-vec vector search
- Remote MCP endpoint at `/mcp`
- Bearer token protection
- Docker Compose deployment

Current MVP tools:

- `get_context`: search saved context
- `update_context`: save new context

Current limitation: vector storage and search are wired through sqlite-vec, but embeddings still use a deterministic local hash provider. Real semantic embedding support is in progress.

## Get Started

```bash
cd portable-context-mcp-server
export PERSONAL_CONTEXT_MCP_TOKEN="replace-with-a-long-random-token"
docker compose -f docker/docker-compose.yml up --build
```

`PERSONAL_CONTEXT_MCP_TOKEN` is required. Docker Compose will fail fast if it is missing.

Health check:

```bash
curl http://localhost:8080/healthz
```

MCP endpoint:

```text
http://localhost:8080/mcp
```

SQLite data:

```text
docker/data/context.db
```

## MCP Client Config

Local:

```json
{
  "url": "http://localhost:8080/mcp",
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

## Quick Test With MCP Inspector

```bash
npx @modelcontextprotocol/inspector
```

Use:

```text
Transport Type: Streamable HTTP
URL: http://localhost:8080/mcp
```

Add a custom header:

```text
Authorization: Bearer replace-with-a-long-random-token
```

The tool list should show:

```text
get_context
update_context
```

## Configuration

| Variable | Purpose |
| --- | --- |
| `PERSONAL_CONTEXT_MCP_TOKEN` | Required Docker Compose token source |
| `AUTH__TOKENS__0` | Bearer token for `/mcp` |
| `DATABASE__PATH` | SQLite file path |
| `SQLITEVEC__ENABLED` | Load sqlite-vec |
| `SQLITEVEC__REQUIRED` | Fail startup if sqlite-vec cannot load |
| `EMBEDDING__DIMENSION` | Current hash embedding dimension, default: `128` |
| `TRANSPORT` | `http` or `stdio` |

`.env.example` is a template. Real secrets belong in `.env` or shell environment variables. `.env` is ignored by git.

Example `.env`:

```bash
PERSONAL_CONTEXT_MCP_TOKEN=replace-with-a-long-random-token
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
