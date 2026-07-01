const http = require("http");
const readline = require("readline");

const UNITY_BRIDGE_ORIGIN = process.env.UNITY_BRIDGE_ORIGIN || "http://127.0.0.1:6400";
const MCP_PORT = Number(process.env.UNITY_MCP_PORT || 7331);

const tools = [
  {
    name: "unity_get_status",
    description: "Get Unity Editor bridge status, Unity version, project path, and active scene.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "unity_list_scene_roots",
    description: "List root GameObjects in the active Unity scene.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
  {
    name: "unity_setup_ar_plane_placement",
    description: "Configure SampleScene with the AR plane placement controller and cube prefab.",
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  },
];

function jsonRpcResult(id, result) {
  return { jsonrpc: "2.0", id, result };
}

function jsonRpcError(id, code, message) {
  return { jsonrpc: "2.0", id, error: { code, message } };
}

async function callUnity(path) {
  const response = await fetch(`${UNITY_BRIDGE_ORIGIN}${path}`);
  const text = await response.text();

  if (!response.ok) {
    throw new Error(`Unity bridge ${response.status}: ${text}`);
  }

  try {
    return JSON.parse(text);
  } catch {
    return { ok: true, text };
  }
}

async function handleRpc(message) {
  const { id, method, params } = message;

  if (method === "initialize") {
    return jsonRpcResult(id, {
      protocolVersion: "2025-06-18",
      capabilities: { tools: {} },
      serverInfo: { name: "f1-xr-unity-mcp", version: "0.1.0" },
    });
  }

  if (method === "notifications/initialized") {
    return null;
  }

  if (method === "tools/list") {
    return jsonRpcResult(id, { tools });
  }

  if (method === "tools/call") {
    const toolName = params && params.name;
    let data;

    if (toolName === "unity_get_status") {
      data = await callUnity("/status");
    } else if (toolName === "unity_list_scene_roots") {
      data = await callUnity("/scene-roots");
    } else if (toolName === "unity_setup_ar_plane_placement") {
      data = await callUnity("/setup-ar-plane-placement");
    } else {
      return jsonRpcError(id, -32602, `Unknown tool: ${toolName}`);
    }

    return jsonRpcResult(id, {
      content: [{ type: "text", text: JSON.stringify(data, null, 2) }],
    });
  }

  return jsonRpcError(id, -32601, `Unknown method: ${method}`);
}

async function writeRpcResponse(writer, message) {
  try {
    const response = await handleRpc(message);
    if (response != null) {
      writer(JSON.stringify(response));
    }
  } catch (error) {
    writer(JSON.stringify(jsonRpcError(message.id, -32000, error.message)));
  }
}

function startHttpServer() {
  const server = http.createServer((request, response) => {
    if (request.method === "GET" && request.url === "/health") {
      response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
      response.end(JSON.stringify({ ok: true, unityBridgeOrigin: UNITY_BRIDGE_ORIGIN }));
      return;
    }

    if (request.method !== "POST" || request.url !== "/mcp") {
      response.writeHead(404, { "content-type": "application/json; charset=utf-8" });
      response.end(JSON.stringify({ ok: false, error: "Use POST /mcp" }));
      return;
    }

    let body = "";
    request.setEncoding("utf8");
    request.on("data", chunk => {
      body += chunk;
    });
    request.on("end", async () => {
      try {
        const message = JSON.parse(body);
        await writeRpcResponse(
          text => {
            response.writeHead(200, { "content-type": "application/json; charset=utf-8" });
            response.end(text);
          },
          message
        );
      } catch (error) {
        response.writeHead(400, { "content-type": "application/json; charset=utf-8" });
        response.end(JSON.stringify(jsonRpcError(null, -32700, error.message)));
      }
    });
  });

  server.listen(MCP_PORT, "127.0.0.1", () => {
    console.error(`Unity MCP server listening on http://127.0.0.1:${MCP_PORT}/mcp`);
    console.error(`Unity bridge origin: ${UNITY_BRIDGE_ORIGIN}`);
  });
}

function startStdioServer() {
  const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  rl.on("line", line => {
    if (!line.trim()) {
      return;
    }

    try {
      const message = JSON.parse(line);
      writeRpcResponse(text => process.stdout.write(`${text}\n`), message);
    } catch (error) {
      process.stdout.write(`${JSON.stringify(jsonRpcError(null, -32700, error.message))}\n`);
    }
  });
}

if (process.argv.includes("--http")) {
  startHttpServer();
} else {
  startStdioServer();
}
