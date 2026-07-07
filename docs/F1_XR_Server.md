# F1_XR_Server Integration Guide

This document explains how F1_XR_Visualizer is expected to work with F1_XR_Server.

## Overview

F1_XR_Server is responsible for providing data to the Unity XR visualizer.

F1_XR_Visualizer connects to the server, receives the required data, and displays it inside the Unity scene.

```text
F1_XR_Server
  -> API, WebSocket, or streaming data
  -> F1_XR_Visualizer
  -> Unity XR scene
```

## Recommended Reading Order

1. Start F1_XR_Server.
2. Confirm the server host and port.
3. Open F1_XR_Visualizer in Unity.
4. Set or confirm the server URL inside the visualizer.
5. Run the target scene.

## Server Address

Use the server address provided by F1_XR_Server.

Example local address:

```text
http://localhost:5000
```

If the visualizer runs on a different device, such as a VR headset or another PC, use the host PC's local network IP instead of `localhost`.

Example network address:

```text
http://192.168.0.10:5000
```

## Unity Configuration

The exact location may vary depending on the implementation, but the server URL is usually configured in one of these places:

- Unity Inspector field on a manager object
- ScriptableObject configuration asset
- `.env` or local config file
- Constant value inside a server/client script

Recommended naming examples:

```text
F1_XR_SERVER_URL
ServerUrl
BaseUrl
WebSocketUrl
```

## Expected Flow

```text
1. F1_XR_Server starts.
2. F1_XR_Visualizer initializes in Unity.
3. Visualizer connects to the server.
4. Server sends race, telemetry, session, or replay data.
5. Visualizer updates the XR scene.
```

## Connection Checklist

Before debugging Unity-side behavior, check the following:

- F1_XR_Server is running.
- The server host and port are correct.
- F1_XR_Visualizer is using the same server address.
- The device running the visualizer can reach the server.
- Firewall settings allow the connection.
- If using WebSocket, the WebSocket endpoint is enabled.
- If using HTTP APIs, the required endpoints respond correctly.

## Common Issues

### Visualizer cannot connect to the server

Check whether the server is running and whether the visualizer is using the correct URL.

If the visualizer runs on another device, do not use `localhost`. Use the server PC's local network IP address.

### Data does not appear in the XR scene

Check whether F1_XR_Server is actually receiving or generating data.

Also check Unity Console logs for connection errors, parsing errors, or missing object references.

### Works in Editor but not on device

Check the network configuration of the device.

For standalone XR devices, the server PC and device usually need to be on the same network.

## Notes for Contributors

When changing the server integration, update this document with:

- Server URL or port changes
- New API endpoints
- WebSocket channel changes
- Required Unity scene or prefab setup
- Any required firewall or network setup
