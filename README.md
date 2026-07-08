# F1_XR_Visualizer

F1_XR_Visualizer is a Unity-based XR visualizer that works with F1_XR_Server.

The visualizer receives race, telemetry, or session data from the server and displays it inside the XR scene.

## Related Project

- F1_XR_Server: server application that provides data to the visualizer
- F1_XR_Visualizer: Unity XR client that visualizes server data

## Project Structure

```text
F1_XR_Visualizer/
  Assets/           Unity assets, scenes, scripts, prefabs
  Packages/         Unity package manifest and lock file
  ProjectSettings/  Unity project settings
  docs/             GitHub documentation
  README.md         Project overview
```

## F1_XR_Server Integration

F1_XR_Visualizer is intended to be used together with F1_XR_Server.

Before running the Unity scene, start F1_XR_Server and check the server address used by the visualizer.

For details, see:

- [F1_XR_Server Integration Guide](docs/F1_XR_Server.md)

## Basic Run Order

1. Clone or open `F1_XR_Server`.
2. Start `F1_XR_Server`.
3. Open `F1_XR_Visualizer` in Unity.
4. Check the server URL setting in the visualizer.
5. Run the Unity scene.

## Unity GitHub Notes

This repository should include:

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.meta` files
- `README.md`
- `docs/`

This repository should not include Unity-generated cache folders such as:

- `Library/`
- `Temp/`
- `Obj/`
- `Build/`
- `Builds/`
- `Logs/`
- `UserSettings/`

## Documentation

- [F1_XR_Server Integration Guide](docs/F1_XR_Server.md)
