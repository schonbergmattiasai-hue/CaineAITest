using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

public class RemoteControlServer : MonoBehaviour
{
    public int port = 8080;

    private HttpListener _listener;
    private Thread _thread;
    private volatile bool _running;

    // Unity APIs must run on the main thread:
    private readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();

    void Start() => StartServer();
    void OnDestroy() => StopServer();

    void Update()
    {
        while (_mainThread.TryDequeue(out var a))
        {
            try { a(); }
            catch (Exception e) { Debug.LogError(e); }
        }
    }

    public void StartServer()
    {
        if (_running) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");

        try { _listener.Start(); }
        catch (Exception e)
        {
            Debug.LogError($"RemoteControlServer failed to start: {e}");
            Debug.LogError($"On Windows you may need: netsh http add urlacl url=http://+:{port}/ user=Everyone");
            return;
        }

        _running = true;
        _thread = new Thread(ListenLoop) { IsBackground = true };
        _thread.Start();

        Debug.Log($"RemoteControlServer listening on http://+:{port}/");
    }

    public void StopServer()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _thread = null;
    }

    private void ListenLoop()
    {
        while (_running)
        {
            try
            {
                var ctx = _listener.GetContext();
                Handle(ctx);
            }
            catch when (!_running) { }
            catch { }
        }
    }

    // ---------- JSON DTOs (JsonUtility-friendly) ----------
    [Serializable]
    private class CubeSpawn
    {
        public int id;      // REQUIRED (e.g. 420)
        public float x, y, z;
    }

    [Serializable]
    private class SpawnCubesByIdRequest
    {
        public CubeSpawn[] cubes; // REQUIRED
        public int extraPerCube;  // optional (default 0)
        public float jitter;      // optional (default 0)
    }

    private void Handle(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url.AbsolutePath ?? "/";

        if (path == "/health")
        {
            WriteJson(ctx, 200, "{\"ok\":true}");
            return;
        }

        // POST /spawn/cubes
        if (path == "/spawn/cubes" && ctx.Request.HttpMethod == "POST")
        {
            string body;
            using (var r = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                body = r.ReadToEnd();

            SpawnCubesByIdRequest req;
            try { req = JsonUtility.FromJson<SpawnCubesByIdRequest>(body); }
            catch (Exception e)
            {
                WriteJson(ctx, 400, "{\"ok\":false,\"error\":\"bad json\",\"detail\":" + QuoteJson(e.Message) + "}");
                return;
            }

            if (req == null || req.cubes == null || req.cubes.Length == 0)
            {
                WriteJson(ctx, 400, "{\"ok\":false,\"error\":\"cubes is required\"}");
                return;
            }

            var extra = Mathf.Max(0, req.extraPerCube);
            var jitter = Mathf.Max(0f, req.jitter);

            _mainThread.Enqueue(() =>
            {
                foreach (var c in req.cubes)
                {
                    var basePos = new Vector3(c.x, c.y, c.z);

                    // main cube with exact ID name ("420")
                    SpawnCubeNamedId(c.id, basePos);

                    // optional extras (named "420_1", "420_2", ...)
                    for (int i = 0; i < extra; i++)
                    {
                        var off = new Vector3(
                            UnityEngine.Random.Range(-jitter, jitter),
                            UnityEngine.Random.Range(-jitter, jitter),
                            UnityEngine.Random.Range(-jitter, jitter)
                        );
                        SpawnCubeNamedIdWithSuffix(c.id, i + 1, basePos + off);
                    }
                }
            });

            WriteJson(ctx, 200, "{\"ok\":true,\"queued\":true}");
            return;
        }

        // POST /clear  -> destroys cubes named like "420" or "420_1"
        if (path == "/clear" && ctx.Request.HttpMethod == "POST")
        {
            _mainThread.Enqueue(() =>
            {
                var all = FindObjectsOfType<GameObject>();
                foreach (var go in all)
                {
                    if (go == null) continue;
                    if (IsSpawnedIdCubeName(go.name))
                        Destroy(go);
                }
            });

            WriteJson(ctx, 200, "{\"ok\":true,\"queued\":true}");
            return;
        }

        WriteJson(ctx, 404, "{\"ok\":false,\"error\":\"unknown route\"}");
    }

    private void SpawnCubeNamedId(int id, Vector3 pos)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = pos;
        cube.name = id.ToString(); // ONLY "420"
    }

    private void SpawnCubeNamedIdWithSuffix(int id, int suffix, Vector3 pos)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = pos;
        cube.name = $"{id}_{suffix}"; // e.g. "420_1"
    }

    private static bool IsSpawnedIdCubeName(string name)
    {
        // Accept: "420" or "420_1" etc.
        if (string.IsNullOrEmpty(name)) return false;

        int i = 0;

        // digits
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i == 0) return false;               // must start with at least one digit
        if (i == name.Length) return true;      // only digits

        // optional _digits
        if (name[i] != '_') return false;
        i++;
        int j = i;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        if (i == j) return false;               // underscore must be followed by digits
        return i == name.Length;                // must consume whole string
    }

    private static string QuoteJson(string s)
    {
        if (s == null) return "null";
        // minimal JSON string escape
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private void WriteJson(HttpListenerContext ctx, int code, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = code;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }
}