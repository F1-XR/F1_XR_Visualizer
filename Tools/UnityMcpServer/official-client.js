const { spawn } = require("child_process");

const relayPath = "C:\\Users\\Admin\\.unity\\relay\\relay_win.exe";
const projectPath = "C:\\F1_XR_Visualizer";
const unityPid = process.env.UNITY_INSTANCE_ID || "25332";

const child = spawn(relayPath, [
  "--mcp",
  "--instance-id",
  unityPid,
  "--project-path",
  projectPath,
  "--debug",
  "--log-dir",
  "Logs",
], {
  cwd: projectPath,
  stdio: ["pipe", "pipe", "pipe"],
});

let buffer = Buffer.alloc(0);
let nextId = 1;
const pending = new Map();

child.stderr.on("data", data => {
  process.stderr.write(data);
});

child.on("exit", (code, signal) => {
  console.error(`relay exited code=${code} signal=${signal}`);
});

child.stdout.on("data", data => {
  buffer = Buffer.concat([buffer, data]);
  drain();
});

function drain() {
  while (true) {
    const headerEnd = buffer.indexOf("\r\n\r\n");
    if (headerEnd < 0) return;

    const header = buffer.subarray(0, headerEnd).toString("utf8");
    const match = /Content-Length:\s*(\d+)/i.exec(header);
    if (!match) throw new Error(`Missing Content-Length in ${header}`);

    const length = Number(match[1]);
    const start = headerEnd + 4;
    const end = start + length;
    if (buffer.length < end) return;

    const body = buffer.subarray(start, end).toString("utf8");
    buffer = buffer.subarray(end);
    const message = JSON.parse(body);

    if (message.id != null && pending.has(message.id)) {
      pending.get(message.id)(message);
      pending.delete(message.id);
    } else {
      console.error("notification", JSON.stringify(message));
    }
  }
}

function send(message) {
  const body = JSON.stringify(message);
  child.stdin.write(`Content-Length: ${Buffer.byteLength(body)}\r\n\r\n${body}`);
}

function request(method, params = {}) {
  const id = nextId++;
  send({ jsonrpc: "2.0", id, method, params });
  return new Promise(resolve => {
    pending.set(id, resolve);
  });
}

async function main() {
  const init = await request("initialize", {
    protocolVersion: "2025-06-18",
    capabilities: {},
    clientInfo: { name: "codex-direct-unity-mcp", version: "0.1.0" },
  });
  console.log("INITIALIZE");
  console.log(JSON.stringify(init, null, 2));

  send({ jsonrpc: "2.0", method: "notifications/initialized", params: {} });

  const tools = await request("tools/list", {});
  console.log("TOOLS");
  console.log(JSON.stringify(tools, null, 2));

  setTimeout(() => child.kill(), 1500);
}

main().catch(error => {
  console.error(error);
  child.kill();
  process.exitCode = 1;
});
