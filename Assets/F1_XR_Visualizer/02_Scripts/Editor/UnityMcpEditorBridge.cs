using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace F1XR.Editor
{
    [InitializeOnLoad]
    public static class UnityMcpEditorBridge
    {
        const string Prefix = "http://127.0.0.1:6400/";
        static HttpListener listener;
        static Thread listenerThread;
        static volatile bool running;

        static UnityMcpEditorBridge()
        {
            EditorApplication.delayCall += Start;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static void Start()
        {
            if (running)
                return;

            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add(Prefix);
                listener.Start();
                running = true;

                listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "F1 XR Unity MCP Bridge"
                };
                listenerThread.Start();

                Debug.Log($"Unity MCP bridge listening on {Prefix}");
            }
            catch (Exception ex)
            {
                running = false;
                Debug.LogWarning($"Could not start Unity MCP bridge: {ex.Message}");
            }
        }

        public static void Stop()
        {
            running = false;

            try
            {
                listener?.Stop();
                listener?.Close();
            }
            catch
            {
                // Listener shutdown can throw if Unity is recompiling or quitting.
            }
            finally
            {
                listener = null;
                listenerThread = null;
            }
        }

        static void ListenLoop()
        {
            while (running && listener != null)
            {
                try
                {
                    var context = listener.GetContext();
                    EditorApplication.delayCall += () => Handle(context);
                }
                catch
                {
                    if (running)
                        Thread.Sleep(100);
                }
            }
        }

        static void Handle(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url != null ? context.Request.Url.AbsolutePath : "/";
                switch (path)
                {
                    case "/status":
                        WriteJson(context, GetStatusJson());
                        break;
                    case "/scene-roots":
                        WriteJson(context, GetSceneRootsJson());
                        break;
                    case "/setup-ar-plane-placement":
                        ARPlanePlacementSceneSetup.ConfigureSampleScene();
                        WriteJson(context, "{\"ok\":true,\"message\":\"AR plane placement configured.\"}");
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        WriteJson(context, "{\"ok\":false,\"error\":\"Unknown endpoint.\"}");
                        break;
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                WriteJson(context, "{\"ok\":false,\"error\":\"" + Escape(ex.Message) + "\"}");
            }
        }

        static string GetStatusJson()
        {
            var scene = EditorSceneManager.GetActiveScene();
            return "{"
                + "\"ok\":true,"
                + "\"unityVersion\":\"" + Escape(Application.unityVersion) + "\","
                + "\"projectPath\":\"" + Escape(Application.dataPath.Replace("/Assets", string.Empty)) + "\","
                + "\"activeScene\":\"" + Escape(scene.path) + "\","
                + "\"isPlaying\":" + (EditorApplication.isPlaying ? "true" : "false")
                + "}";
        }

        static string GetSceneRootsJson()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var builder = new StringBuilder();
            builder.Append("{\"ok\":true,\"roots\":[");

            for (var i = 0; i < roots.Length; i++)
            {
                if (i > 0)
                    builder.Append(",");

                builder.Append("{\"name\":\"");
                builder.Append(Escape(roots[i].name));
                builder.Append("\",\"instanceId\":");
                builder.Append(roots[i].GetInstanceID());
                builder.Append("}");
            }

            builder.Append("]}");
            return builder.ToString();
        }

        static void WriteJson(HttpListenerContext context, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            using var output = context.Response.OutputStream;
            output.Write(bytes, 0, bytes.Length);
        }

        static string Escape(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
