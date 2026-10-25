using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TinyHarness;

namespace SpaceMonger.Core.Services.Llm;

/// <summary>
/// OpenAI 兼容流协议适配，支持 Responses 与 Chat Completions 两种线上格式；
/// 工具参数完整后才交给 Harness，并兼容「声明流式但返回完整 JSON」的中转站。
/// </summary>
public sealed class OpenAiModelClient : IModelClient
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly Uri endpointUri;
    private readonly string model;
    private readonly LlmApiFormat format;
    private readonly ThinkingLevel thinkingLevel;
    private readonly CliIdentityKind cliIdentity;
    private readonly string sessionId = Guid.NewGuid().ToString();
    // Responses 格式：工具调用回合之间回传加密思考，避免上游拒绝丢失的 reasoning 项。
    private readonly ConcurrentDictionary<string, JsonArray> encryptedReasoning = new(StringComparer.Ordinal);

    public OpenAiModelClient(HttpClient httpClient, string apiKey, string? baseUrl, string model,
        LlmApiFormat format, ThinkingLevel thinkingLevel = ThinkingLevel.Off, CliIdentityKind cliIdentity = CliIdentityKind.None)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (format != LlmApiFormat.OpenAiResponses && format != LlmApiFormat.OpenAiChatCompletions)
            throw new ArgumentOutOfRangeException(nameof(format));
        this.apiKey = apiKey;
        this.model = model;
        this.format = format;
        this.thinkingLevel = thinkingLevel;
        this.cliIdentity = cliIdentity;
        try
        {
            endpointUri = OpenAiOptions.GetEndpointUri(format == LlmApiFormat.OpenAiResponses ? "/responses" : "/chat/completions", baseUrl);
        }
        catch (InvalidOperationException) { throw new ModelClientException("模型服务地址无效。", ModelErrorKind.InvalidRequest); }
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var outgoing = CreateRequest(request);
        using var response = await SendAsync(outgoing, cancellationToken).ConfigureAwait(false);
        // 协议故障现场：最近收到的原始帧/非流式响应体，仅在协议失败时落盘（见 LlmStreamDiagnostics）。
        var tail = new StringBuilder();
        await using var events = ReadEventsAsync(response, tail, cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            ModelStreamEvent current;
            // 只包住读取操作，避免在带 catch 的代码块中 yield；异常不附带原始响应或内部异常。
            try
            {
                if (!await events.MoveNextAsync().ConfigureAwait(false)) yield break;
                current = events.Current;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ModelClientException) { throw; }
            catch (OperationCanceledException) { throw new ModelClientException("模型请求超时。", ModelErrorKind.Timeout); }
            catch (HttpRequestException) { throw new ModelClientException("模型服务连接中断。", ModelErrorKind.ConnectionFailed); }
            catch (IOException) { throw new ModelClientException("模型服务连接中断。", ModelErrorKind.ConnectionFailed); }
            catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or NotSupportedException or KeyNotFoundException or FormatException)
            {
                // 帧 JSON/流解码失败：先落盘原始帧现场再按协议错误处理，避免只剩一句无法归因的报错。
                LlmStreamDiagnostics.WriteDump("openai", endpointUri, model, $"stream-parse: {error.GetType().Name}", tail.ToString());
                throw Fault("stream-parse", error.GetType().Name);
            }
            cancellationToken.ThrowIfCancellationRequested();
            yield return current;
        }
    }

    private HttpRequestMessage CreateRequest(ModelRequest request)
    {
        try
        {
            var body = format == LlmApiFormat.OpenAiResponses ? BuildResponsesBody(request) : BuildChatCompletionsBody(request);
            var outgoing = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            outgoing.Headers.Accept.ParseAdd("text/event-stream");
            outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            ApplyIdentityHeaders(outgoing);
            outgoing.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return outgoing;
        }
        catch (ModelClientException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new ModelClientException("模型请求参数无效。", ModelErrorKind.InvalidRequest);
        }
    }

    private void ApplyIdentityHeaders(HttpRequestMessage outgoing)
    {
        foreach (var (name, value) in CliIdentityService.BuildHeaders(cliIdentity))
            outgoing.Headers.TryAddWithoutValidation(name, value);
        if (cliIdentity == CliIdentityKind.Codex)
        {
            // Codex CLI 会对每个会话附带一致的三元组；部分中转站据此识别真实客户端。
            outgoing.Headers.TryAddWithoutValidation("session_id", sessionId);
            outgoing.Headers.TryAddWithoutValidation("thread-id", sessionId);
            outgoing.Headers.TryAddWithoutValidation("x-client-request-id", Guid.NewGuid().ToString());
        }
    }

    private JsonObject BuildChatCompletionsBody(ModelRequest request)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case ChatRole.System:
                    if (!string.IsNullOrEmpty(message.Content))
                        messages.Add(new JsonObject { ["role"] = "system", ["content"] = message.Content });
                    break;
                case ChatRole.User:
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = message.Content ?? "" });
                    break;
                case ChatRole.Assistant:
                {
                    var calls = message.ToolCalls.Count > 0 ? message.ToolCalls
                        : message.Blocks.Where(block => block.Kind == ChatContentKind.ToolCall && block.ToolCall is not null).Select(block => block.ToolCall!).ToArray();
                    var text = message.Content ?? string.Concat(message.Blocks.Where(block => block.Kind == ChatContentKind.Text).Select(block => block.Text));
                    if (calls.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(text)) messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = text });
                        break;
                    }
                    var assistant = new JsonObject { ["role"] = "assistant" };
                    if (!string.IsNullOrEmpty(text)) assistant["content"] = text;
                    var toolCalls = new JsonArray();
                    foreach (var call in calls)
                    {
                        if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name)) throw Fault("history-invalid-tool-call", null);
                        toolCalls.Add(new JsonObject
                        {
                            ["id"] = call.Id, ["type"] = "function",
                            ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = ValidatedArguments(call) },
                        });
                    }
                    assistant["tool_calls"] = toolCalls;
                    messages.Add(assistant);
                    break;
                }
                case ChatRole.Tool:
                    if (string.IsNullOrWhiteSpace(message.ToolCallId)) throw Fault("history-empty-tool-call-id", null);
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = message.ToolCallId, ["content"] = message.Content ?? "" });
                    break;
            }
        }
        if (messages.Count == 0) throw Fault("history-empty-messages", null);
        var body = new JsonObject
        {
            ["model"] = model,
            // 中转站生态对 max_tokens 的兼容面远大于 max_completion_tokens。
            ["max_tokens"] = LlmContracts.ToOpenAiMaxOutputTokens(thinkingLevel),
            ["stream"] = true,
            ["messages"] = messages,
        };
        if (thinkingLevel != ThinkingLevel.Off)
            body["reasoning_effort"] = LlmContracts.ToReasoningEffort(AdjustMinimal(thinkingLevel));
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
                tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = tool.ParametersSchema.DeepClone() } });
            body["tools"] = tools;
            if (cliIdentity == CliIdentityKind.Codex)
            {
                body["tool_choice"] = "auto";
                body["parallel_tool_calls"] = true;
            }
        }
        return body;
    }

    private JsonObject BuildResponsesBody(ModelRequest request)
    {
        var systems = new List<string>();
        var input = new JsonArray();
        foreach (var message in request.Messages)
        {
            switch (message.Role)
            {
                case ChatRole.System:
                    if (!string.IsNullOrEmpty(message.Content)) systems.Add(message.Content);
                    break;
                case ChatRole.User:
                    input.Add(MessageItem("user", "input_text", message.Content ?? ""));
                    break;
                case ChatRole.Assistant:
                {
                    var calls = message.ToolCalls.Count > 0 ? message.ToolCalls
                        : message.Blocks.Where(block => block.Kind == ChatContentKind.ToolCall && block.ToolCall is not null).Select(block => block.ToolCall!).ToArray();
                    var text = message.Content ?? string.Concat(message.Blocks.Where(block => block.Kind == ChatContentKind.Text).Select(block => block.Text));
                    if (calls.Count == 0)
                    {
                        if (!string.IsNullOrEmpty(text)) input.Add(MessageItem("assistant", "output_text", text));
                        break;
                    }
                    var thoughts = calls.Select(call => encryptedReasoning.GetValueOrDefault(call.Id)).FirstOrDefault(value => value is not null);
                    if (thoughts is not null)
                        foreach (var thought in thoughts) input.Add(thought!.DeepClone());
                    if (!string.IsNullOrEmpty(text)) input.Add(MessageItem("assistant", "output_text", text));
                    foreach (var call in calls)
                    {
                        if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name)) throw Fault("history-invalid-tool-call", null);
                        input.Add(new JsonObject { ["type"] = "function_call", ["call_id"] = call.Id, ["name"] = call.Name, ["arguments"] = ValidatedArguments(call) });
                    }
                    break;
                }
                case ChatRole.Tool:
                    if (string.IsNullOrWhiteSpace(message.ToolCallId)) throw Fault("history-empty-tool-call-id", null);
                    input.Add(new JsonObject { ["type"] = "function_call_output", ["call_id"] = message.ToolCallId, ["output"] = message.Content ?? "" });
                    break;
            }
        }
        if (input.Count == 0) throw Fault("history-empty-messages", null);
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_output_tokens"] = LlmContracts.ToOpenAiMaxOutputTokens(thinkingLevel),
            ["stream"] = true,
            ["store"] = false,
            ["input"] = input,
        };
        if (systems.Count > 0) body["instructions"] = string.Join("\n\n", systems);
        if (thinkingLevel != ThinkingLevel.Off)
        {
            body["reasoning"] = new JsonObject { ["effort"] = LlmContracts.ToReasoningEffort(AdjustMinimal(thinkingLevel)) };
            body["include"] = new JsonArray("reasoning.encrypted_content");
        }
        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
                tools.Add(new JsonObject
                {
                    ["type"] = "function", ["name"] = tool.Name, ["description"] = tool.Description,
                    ["parameters"] = tool.ParametersSchema.DeepClone(), ["strict"] = false,
                });
            body["tools"] = tools;
            body["tool_choice"] = "auto";
        }
        if (cliIdentity == CliIdentityKind.Codex)
            body["prompt_cache_key"] = sessionId;
        return body;
    }

    // 极少数上游不支持 minimal，统一降级到 low，保证请求可被接受。
    private static ThinkingLevel AdjustMinimal(ThinkingLevel level) => level == ThinkingLevel.Minimal ? ThinkingLevel.Low : level;

    private static JsonObject MessageItem(string role, string textType, string text) => new()
    {
        ["type"] = "message",
        ["role"] = role,
        ["content"] = new JsonArray(new JsonObject { ["type"] = textType, ["text"] = text }),
    };

    private static string ValidatedArguments(ToolCall call)
    {
        if (JsonNode.Parse(call.ArgumentsJson) is not JsonObject) throw Fault("history-invalid-tool-arguments", call.Name);
        return call.ArgumentsJson;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            using (response)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[ModelErrorClassifier.MaxErrorBodyBytes];
                var length = 0;
                while (length < buffer.Length)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    length += read;
                }
                var (code, message) = ModelErrorClassifier.ParseErrorBody(Encoding.UTF8.GetString(buffer, 0, length));
                var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
                throw ProviderError((int)response.StatusCode, code, message, retryAfter);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ModelClientException) { throw; }
        catch (OperationCanceledException) { throw new ModelClientException("模型请求超时。", ModelErrorKind.Timeout); }
        catch (HttpRequestException) { throw new ModelClientException("无法连接模型服务。", ModelErrorKind.ConnectionFailed); }
        catch (IOException) { throw new ModelClientException("模型服务连接中断。", ModelErrorKind.ConnectionFailed); }
    }

    private async IAsyncEnumerable<ModelStreamEvent> ReadEventsAsync(HttpResponseMessage response, StringBuilder tail, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 累积器内部协议故障：统一落盘现场并带站点标记。
        ModelClientException Fail(string site, string? detail)
        {
            LlmStreamDiagnostics.WriteDump("openai", endpointUri, model, $"{site}: {detail}", tail.ToString());
            return Fault(site, $"openai-stream/{site}: {detail}");
        }
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            // 中转站兼容：声明 stream 但直接返回完整 JSON。
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            LlmStreamDiagnostics.Capture(tail, body);
            foreach (var item in CompleteJson(body, tail)) yield return item;
            yield break;
        }
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), new UTF8Encoding(false, true));
        var data = new StringBuilder();
        Accumulator blocks = format == LlmApiFormat.OpenAiResponses ? new ResponsesAccumulator(encryptedReasoning, Fail) : new ChatCompletionsAccumulator(Fail);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length > 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var value = line[5..];
                    if (value.StartsWith(' ')) value = value[1..];
                    data.Append(value).Append('\n');
                }
                else if (line.StartsWith(":", StringComparison.Ordinal) || line.StartsWith("event:", StringComparison.Ordinal))
                {
                    continue;
                }
                continue;
            }
            if (data.Length == 0) continue;
            var payload = data.ToString();
            data.Clear();
            // 解析前先留档：无论后续在哪一步失败，现场都包含这条原始帧。
            LlmStreamDiagnostics.Capture(tail, payload);
            if (payload.StartsWith("[DONE]", StringComparison.Ordinal))
            {
                foreach (var item in blocks.Finish()) yield return item;
                // [DONE] 到达但从未收到完成原因：视为协议错误，避免假成功。
                if (!blocks.Completed) throw ProtocolFault(tail, "done-without-completion", $"format={format}");
                yield break;
            }
            using var document = JsonDocument.Parse(payload);
            foreach (var item in blocks.Handle(document.RootElement)) yield return item;
        }
        // 部分中转站省略 [DONE]：已收到完成原因时允许正常收尾，否则视为协议错误。
        foreach (var item in blocks.Finish()) yield return item;
        if (!blocks.Completed) throw ProtocolFault(tail, "stream-end-without-completion", $"format={format}");
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private IEnumerable<ModelStreamEvent> CompleteJson(string body, StringBuilder tail)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (format == LlmApiFormat.OpenAiChatCompletions)
        {
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) throw ProtocolFault(tail, "complete-json", "缺少 choices");
            var choice = choices[0];
            var message = choice.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.Object ? value : throw ProtocolFault(tail, "complete-json", "缺少 message");
            if (message.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String && reasoning.GetString() is { Length: > 0 } reasoningText)
                yield return new ReasoningDeltaEvent(reasoningText);
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
                yield return new TextDeltaEvent(text);
            var tools = new List<ToolCall>();
            if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
                foreach (var call in calls.EnumerateArray())
                {
                    var function = call.TryGetProperty("function", out var fn) ? fn : throw ProtocolFault(tail, "complete-json", "tool_call 缺少 function");
                    var arguments = function.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.String ? args.GetString()! : "{}";
                    if (JsonNode.Parse(arguments) is not JsonObject) throw ProtocolFault(tail, "complete-json", "工具参数不是 JSON 对象");
                    tools.Add(new ToolCall(call.GetProperty("id").GetString()!, function.GetProperty("name").GetString()!, arguments));
                }
            foreach (var tool in tools) yield return new ModelToolCallEvent(tool);
            yield return new ModelCompletedEvent(tools.Count > 0 ? "tool_use" : "end_turn");
            yield break;
        }
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) throw ProtocolFault(tail, "complete-json", "缺少 output");
        var emittedTools = new List<ToolCall>();
        foreach (var item in output.EnumerateArray())
        {
            switch (String(item, "type"))
            {
                case "message":
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) break;
                    foreach (var part in content.EnumerateArray())
                        if (String(part, "type") == "output_text" && String(part, "text") is { Length: > 0 } text)
                            yield return new TextDeltaEvent(text);
                    break;
                case "reasoning":
                    // 完整 JSON 分支不回传加密思考；后续请求由带 include 的流式路径自行恢复。
                    break;
                case "function_call":
                {
                    var arguments = String(item, "arguments") ?? "{}";
                    if (JsonNode.Parse(arguments) is not JsonObject) throw ProtocolFault(tail, "complete-json", "工具参数不是 JSON 对象");
                    var call = new ToolCall(item.GetProperty("call_id").GetString()!, item.GetProperty("name").GetString()!, arguments);
                    emittedTools.Add(call);
                    yield return new ModelToolCallEvent(call);
                    break;
                }
            }
        }
        yield return new ModelCompletedEvent(emittedTools.Count > 0 ? "tool_use" : "end_turn");
    }

    private ModelClientException ProviderError(int? status, string? code, string? message, TimeSpan? retryAfter = null)
    {
        var safeCode = Clean(code);
        if (safeCode?.Length > 64 || safeCode?.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.') == true) safeCode = null;
        return new ModelClientException("模型服务请求失败。", ModelErrorClassifier.Classify(status, code), status, safeCode, Clean(message)) { RetryAfter = retryAfter };
    }

    private string? Clean(string? value) => ModelErrorClassifier.SanitizeUpstreamMessage(value?.Replace(apiKey, "***", StringComparison.Ordinal));
    // 请求构造期（尚无流）的协议错误：只带站点标记，便于日志归因。
    private static ModelClientException Fault(string site, string? detail) =>
        new($"模型响应无效或流未完整结束。 [{site}]", ModelErrorKind.Unknown,
            upstreamMessage: ModelErrorClassifier.SanitizeUpstreamMessage(detail));
    // 流内协议故障：先落盘最近原始帧现场，再抛带站点标记的协议错误。
    private ModelClientException ProtocolFault(StringBuilder tail, string site, string? detail)
    {
        LlmStreamDiagnostics.WriteDump("openai", endpointUri, model, $"{site}: {detail}", tail.ToString());
        return Fault(site, $"openai-stream/{site}: {detail}");
    }
    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>两种线上格式共用的增量累积器：切片聚合 + 完成事件收尾。</summary>
    private abstract class Accumulator(Func<string, string?, Exception> fail)
    {
        // 协议故障统一经由宿主传入的 Fail 构造：先落盘流现场，再生成带站点标记的异常。
        protected readonly Func<string, string?, Exception> Fail = fail;
        public abstract IEnumerable<ModelStreamEvent> Handle(JsonElement frame);
        public abstract IEnumerable<ModelStreamEvent> Finish();
        public bool Completed { get; protected set; }
        protected static string? Str(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private sealed class ChatCompletionsAccumulator(Func<string, string?, Exception> fail) : Accumulator(fail)
    {
        private readonly SortedDictionary<int, FunctionBuilder> calls = [];
        private string? finishReason;

        public override IEnumerable<ModelStreamEvent> Handle(JsonElement frame)
        {
            if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) yield break;
            var choice = choices[0];
            if (choice.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
            {
                var reasoning = Str(delta, "reasoning_content") ?? Str(delta, "reasoning") ?? Str(delta, "reasoning_text");
                if (!string.IsNullOrEmpty(reasoning)) yield return new ReasoningDeltaEvent(reasoning!);
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
                    yield return new TextDeltaEvent(text);
                if (delta.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                {
                    foreach (var chunk in toolCalls.EnumerateArray())
                    {
                        var index = chunk.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : calls.Count;
                        var builder = calls.TryGetValue(index, out var existing) ? existing : calls[index] = new FunctionBuilder();
                        if (Str(chunk, "id") is { } id) builder.Id.Append(id);
                        if (chunk.TryGetProperty("function", out var function))
                        {
                            if (Str(function, "name") is { } name) builder.Name.Append(name);
                            if (Str(function, "arguments") is { } arguments) builder.Arguments.Append(arguments);
                        }
                    }
                }
            }
            if (Str(choice, "finish_reason") is { } reason) finishReason = reason;
        }

        public override IEnumerable<ModelStreamEvent> Finish()
        {
            if (Completed || finishReason is null) yield break;
            Completed = true;
            foreach (var builder in calls.Values)
            {
                if (builder.Id.Length == 0 || builder.Name.Length == 0)
                    throw Fail("tool-call-finish", $"id={builder.Id.Length > 0}, name={builder.Name.Length > 0}");
                var arguments = builder.Arguments.Length > 0 ? builder.Arguments.ToString() : "{}";
                if (JsonNode.Parse(arguments) is not JsonObject)
                    throw Fail("invalid-tool-arguments", $"tool={builder.Name}, bytes={builder.Arguments.Length}");
                yield return new ModelToolCallEvent(new ToolCall(builder.Id.ToString(), builder.Name.ToString(), arguments));
            }
            yield return new ModelCompletedEvent(calls.Count > 0 ? "tool_use" : "end_turn");
        }
    }

    private sealed class ResponsesAccumulator(ConcurrentDictionary<string, JsonArray> encryptedReasoning, Func<string, string?, Exception> fail) : Accumulator(fail)
    {
        private readonly SortedDictionary<int, FunctionBuilder> calls = [];
        private readonly List<(string Id, string Encrypted)> reasoningItems = [];
        private bool completed;

        public override IEnumerable<ModelStreamEvent> Handle(JsonElement frame)
        {
            if (frame.ValueKind != JsonValueKind.Object) yield break;
            var type = Str(frame, "type");
            switch (type)
            {
                case "response.output_item.added":
                    if (!frame.TryGetProperty("item", out var added) || added.ValueKind != JsonValueKind.Object) yield break;
                    if (Str(added, "type") == "function_call")
                    {
                        var index = frame.TryGetProperty("output_index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : calls.Count;
                        var builder = calls.TryGetValue(index, out var existing) ? existing : calls[index] = new FunctionBuilder();
                        if (Str(added, "call_id") is { } callId) builder.Id.Append(callId);
                        if (Str(added, "name") is { } name) builder.Name.Append(name);
                        if (Str(added, "arguments") is { } arguments && arguments.Length > 0) builder.Arguments.Append(arguments);
                    }
                    yield break;
                case "response.output_text.delta":
                    if (Str(frame, "delta") is { Length: > 0 } text) yield return new TextDeltaEvent(text);
                    yield break;
                case "response.reasoning_text.delta":
                    if (Str(frame, "delta") is { Length: > 0 } reasoning) yield return new ReasoningDeltaEvent(reasoning);
                    yield break;
                case "response.reasoning_summary_text.delta":
                    if (Str(frame, "delta") is { Length: > 0 } summary) yield return new ReasoningDeltaEvent(summary, IsSummary: true);
                    yield break;
                case "response.function_call_arguments.delta":
                {
                    var index = frame.TryGetProperty("output_index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : -1;
                    if (index >= 0 && calls.TryGetValue(index, out var builder) && Str(frame, "delta") is { } delta) builder.Arguments.Append(delta);
                    yield break;
                }
                case "response.output_item.done":
                {
                    if (!frame.TryGetProperty("item", out var done) || done.ValueKind != JsonValueKind.Object) yield break;
                    if (Str(done, "type") == "function_call")
                    {
                        var index = frame.TryGetProperty("output_index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : -1;
                        FunctionBuilder? builder = index >= 0 ? calls.GetValueOrDefault(index) : null;
                        if (builder is null && calls.Count == 1) builder = calls.Values.First();
                        if (builder is null) throw Fail("tool-block-unknown-index", $"index={index}");
                        if (builder.Arguments.Length == 0 && Str(done, "arguments") is { } full && full.Length > 0) builder.Arguments.Append(full);
                        var arguments = builder.Arguments.Length > 0 ? builder.Arguments.ToString() : "{}";
                        if (JsonNode.Parse(arguments) is not JsonObject || builder.Id.Length == 0 || builder.Name.Length == 0)
                            throw Fail("tool-block-finish", $"index={index}, tool={builder.Name}, id={builder.Id.Length > 0}, bytes={builder.Arguments.Length}");
                        yield return new ModelToolCallEvent(new ToolCall(builder.Id.ToString(), builder.Name.ToString(), arguments));
                    }
                    else if (Str(done, "type") == "reasoning" && Str(done, "id") is { } id && Str(done, "encrypted_content") is { Length: > 0 } encrypted)
                    {
                        reasoningItems.Add((id, encrypted));
                    }
                    yield break;
                }
                case "response.completed":
                case "response.incomplete":
                {
                    if (completed) throw Fail("duplicate-response-completed", type);
                    completed = true;
                    Completed = true;
                    // 记录本轮的加密思考，供下一次工具回合回传。
                    if (calls.Count > 0 && reasoningItems.Count > 0)
                    {
                        var thoughts = new JsonArray();
                        foreach (var (id, encrypted) in reasoningItems)
                            thoughts.Add(new JsonObject { ["type"] = "reasoning", ["id"] = id, ["encrypted_content"] = encrypted });
                        foreach (var builder in calls.Values)
                            encryptedReasoning[builder.Id.ToString()] = thoughts.DeepClone() as JsonArray ?? thoughts;
                    }
                    yield return new ModelCompletedEvent(calls.Count > 0 ? "tool_use" : "end_turn");
                    yield break;
                }
                case "error":
                case "response.failed":
                {
                    var (code, message) = ModelErrorClassifier.ParseErrorBody(frame.GetRawText());
                    throw new ModelClientException("模型服务请求失败。", ModelErrorClassifier.Classify(null, code), null,
                        code is { Length: > 0 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? code : null,
                        ModelErrorClassifier.SanitizeUpstreamMessage(message));
                }
            }
        }

        public override IEnumerable<ModelStreamEvent> Finish()
        {
            // [DONE] 到达或流结束时：response.completed 已处理则无事可做。
            if (!completed && calls.Values.Any(builder => builder.Arguments.Length > 0))
                throw Fail("stream-end-unfinished-arguments", $"calls={calls.Count}");
            yield break;
        }
    }

    private sealed class FunctionBuilder
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
    }
}
