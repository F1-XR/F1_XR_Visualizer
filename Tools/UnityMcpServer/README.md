# F1 XR Unity MCP Server

This local server exposes a small MCP-style JSON-RPC surface for the Unity Editor bridge.

## Start HTTP MCP endpoint

```powershell
node Tools\UnityMcpServer\server.js --http
```

Endpoint:

```text
POST http://127.0.0.1:7331/mcp
GET  http://127.0.0.1:7331/health
```

Unity bridge endpoint:

```text
http://127.0.0.1:6400
```

The Unity bridge starts automatically in the Unity Editor after scripts compile. It can also be started manually from:

```text
F1 XR > MCP > Start Unity Bridge
```
