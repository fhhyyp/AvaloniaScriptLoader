using System.Net.Http;
using System.Text;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Modules;

/// <summary>
/// HTTP 模块 — fetch() API，底层基于 HttpClient
///
/// 脚本用法:
///   import { fetch } from "avalonia"
///   var res = fetch("https://api.example.com/users")
///   var data = res.json()  // 解析 JSON 为 ArrayValue/ObjectValue
///
///   返回对象: { status: number, ok: bool, text(): string, json(): value }
/// </summary>
public static class HttpModule
{
    private static readonly HttpClient _client = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static ObjectValue CreateExports()
    {
        return new ObjectValue(new Dictionary<string, Value>
        {
            ["fetch"] = CreateFetchFunction(),
        });
    }

    private static FunctionValue CreateFetchFunction()
    {
        return new FunctionValue("fetch", (engine, args) =>
        {
            var url = args.FirstOrDefault()?.AsString() ?? "";
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("fetch() 需要 URL 参数");

            // 解析 options（第二个参数可选）
            var method = "GET";
            Dictionary<string, string>? headers = null;
            string? body = null;

            if (args.Count > 1 && args[1] is ObjectValue opts)
            {
                if (opts.Properties.TryGetValue("method", out var m))
                    method = m.AsString()?.ToUpperInvariant() ?? "GET";
                if (opts.Properties.TryGetValue("body", out var b))
                    body = b.AsString();
                if (opts.Properties.TryGetValue("headers", out var h) && h is ObjectValue hObj)
                {
                    headers = [];
                    foreach (var kv in hObj.Properties)
                        headers[kv.Key] = kv.Value.AsString();
                }
            }

            // 同步执行 HTTP 请求（脚本 Lambda 上下文是同步的）
            HttpResponseMessage response;
            try
            {
                var request = new HttpRequestMessage(new HttpMethod(method), url);

                if (headers != null)
                    foreach (var h in headers)
                        request.Headers.TryAddWithoutValidation(h.Key, h.Value);

                if (body != null && method is "POST" or "PUT" or "PATCH")
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                response = _client.Send(request);
            }
            catch (Exception ex)
            {
                Log.Error($"[fetch] 请求失败: {url} — {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }

            return CreateResponseObject(response);
        });
    }

    private static ObjectValue CreateResponseObject(HttpResponseMessage response)
    {
        // 缓存响应体（只读一次）
        string? _cachedBody = null;

        string ReadBody()
        {
            if (_cachedBody == null)
            {
                try { _cachedBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); }
                catch (Exception ex) {
#if DEBUG
                    System.Diagnostics.Debug.WriteLine(ex);
#endif
                    _cachedBody = "";
                }
            }
            return _cachedBody;
        }

        return new ObjectValue(new Dictionary<string, Value>
        {
            ["status"] = NumberValueFactory.Create((int)response.StatusCode),
            ["ok"] = BoolValue.Create(response.IsSuccessStatusCode),

            // text() → string
            ["text"] = new FunctionValue("text",
                () => StringValue.Create(ReadBody())),

            // json() → ArrayValue | ObjectValue | Null
            ["json"] = new FunctionValue("json", () =>
            {
                try
                {
                    var json = ReadBody();
                    var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
                    return JsonElementToValue(parsed);
                }
                catch (Exception ex)
                {
                    Log.Error($"[fetch] JSON 解析失败: {ex.Message}");
                    return Value.Null;
                }
            }),
        });
    }

    private static ObjectValue CreateErrorResponse(string message)
    {
        return new ObjectValue(new Dictionary<string, Value>
        {
            ["status"] = NumberValueFactory.Create(0),
            ["ok"] = BoolValue.False,
            ["text"] = new FunctionValue("text", () => StringValue.Create(message)),
            ["json"] = new FunctionValue("json", () => Value.Null),
        });
    }

    /// <summary>
    /// System.Text.Json.JsonElement → ScriptLang Value
    /// </summary>
    private static Value JsonElementToValue(System.Text.Json.JsonElement element)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Null:
                return Value.Null;
            case System.Text.Json.JsonValueKind.True:
                return BoolValue.True;
            case System.Text.Json.JsonValueKind.False:
                return BoolValue.False;
            case System.Text.Json.JsonValueKind.String:
                return StringValue.Create(element.GetString() ?? "");
            case System.Text.Json.JsonValueKind.Number:
                return NumberValueFactory.Create(element.GetDouble());
            case System.Text.Json.JsonValueKind.Array:
                var list = new List<Value>();
                foreach (var item in element.EnumerateArray())
                    list.Add(JsonElementToValue(item));
                return new ArrayValue(list);
            case System.Text.Json.JsonValueKind.Object:
                var dict = new Dictionary<string, Value>();
                foreach (var prop in element.EnumerateObject())
                    dict[prop.Name] = JsonElementToValue(prop.Value);
                return new ObjectValue(dict);
            default:
                return Value.Null;
        }
    }
}
