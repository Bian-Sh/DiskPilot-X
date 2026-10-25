using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TinyHarness;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Assistant;

/// <summary>Anthropic Messages 原生流协议适配；工具参数完整后才交给 Harness。</summary>
public sealed class AnthropicModelClient : IModelClient
{
    private readonly HttpClient httpClient;
    private readonly string apiKey;
    private readonly Uri messagesUri;
    private readonly string model;
    private readonly ThinkingLevel thinkingLevel;
    private readonly CliIdentityKind cliIdentity;
    private bool EnableThinking => thinkingLevel != ThinkingLevel.Off;
    // Harness 历史没有签名字段，只保存本客户端收到且已完整结束的签名思考。
    private readonly ConcurrentDictionary<string, JsonArray> signedThinking = new(StringComparer.Ordinal);

    public AnthropicModelClient(HttpClient httpClient, string apiKey, string? baseUrl, string model,
        ThinkingLevel thinkingLevel = ThinkingLevel.Off, CliIdentityKind cliIdentity = CliIdentityKind.None)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        this.apiKey = apiKey;
        this.model = model;
        this.thinkingLevel = thinkingLevel;
        this.cliIdentity = cliIdentity;
        try { messagesUri = AnthropicOptions.GetMessagesUri(baseUrl); }
        catch (InvalidOperationException) { throw new ModelClientException("模型服务地址无效。", ModelErrorKind.InvalidRequest); }
    }

    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var outgoing = CreateRequest(request);
        using var response = await SendAsync(outgoing, cancellationToken).ConfigureAwait(false);
        await using var events = ReadEventsAsync(response, cancellationToken).GetAsyncEnumerator(cancellationToken);
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
                throw ProtocolError();
            }
            cancellationToken.ThrowIfCancellationRequested();
            yield return current;
        }
    }

    private HttpRequestMessage CreateRequest(ModelRequest request)
    {
        try
        {
            var messages = new JsonArray();
            var systems = new List<string>();
            foreach (var message in request.Messages)
            {
                if (message.Role == ChatRole.System)
                {
                    if (!string.IsNullOrEmpty(message.Content)) systems.Add(message.Content);
                    continue;
                }

                var content = new JsonArray();
                var role = message.Role == ChatRole.Assistant ? "assistant" : "user";
                if (message.Role == ChatRole.Tool)
                {
                    if (string.IsNullOrWhiteSpace(message.ToolCallId)) throw ProtocolError();
                    content.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = message.ToolCallId, ["content"] = message.Content ?? "" });
                }
                else
                {
                    var calls = message.ToolCalls.Count > 0 ? message.ToolCalls : message.Blocks
                        .Where(block => block.Kind == ChatContentKind.ToolCall && block.ToolCall is not null).Select(block => block.ToolCall!).ToArray();
                    if (message.Role == ChatRole.Assistant && EnableThinking)
                    {
                        var thoughts = calls.Select(call => signedThinking.GetValueOrDefault(call.Id)).FirstOrDefault(value => value is not null);
                        if (thoughts is not null)
                            foreach (var thought in thoughts) content.Add(thought!.DeepClone());
                    }
                    // 普通历史中的 Thinking 没有签名，不能伪装成 Anthropic thinking block。
                    var text = message.Content ?? string.Concat(message.Blocks.Where(block => block.Kind == ChatContentKind.Text).Select(block => block.Text));
                    if (!string.IsNullOrEmpty(text)) content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
                    if (message.Role == ChatRole.Assistant)
                    {
                        foreach (var call in calls)
                        {
                            if (string.IsNullOrWhiteSpace(call.Id) || string.IsNullOrWhiteSpace(call.Name)) throw ProtocolError();
                            var input = JsonNode.Parse(call.ArgumentsJson) as JsonObject ?? throw ProtocolError();
                            content.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id, ["name"] = call.Name, ["input"] = input });
                        }
                    }
                }
                if (content.Count == 0) continue;
                // 连续工具结果必须合并成一个 user turn，保留它们与 tool_use 的对应关系。
                if (messages.LastOrDefault() is JsonObject previous && previous["role"]!.GetValue<string>() == role)
                {
                    var previousContent = (JsonArray)previous["content"]!;
                    foreach (var block in content) previousContent.Add(block!.DeepClone());
                }
                else messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
            }
            var (budgetTokens, maxTokens) = LlmContracts.ToAnthropicThinkingBudget(thinkingLevel);
            var body = new JsonObject { ["model"] = model, ["max_tokens"] = maxTokens, ["stream"] = true, ["messages"] = messages };
            if (systems.Count > 0) body["system"] = string.Join("\n\n", systems);
            if (request.Tools.Count > 0)
            {
                var tools = new JsonArray();
                foreach (var tool in request.Tools)
                    tools.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["input_schema"] = tool.ParametersSchema.DeepClone() });
                body["tools"] = tools;
            }
            // Anthropic 没有 minimal 档，按低档换算。
            body["thinking"] = EnableThinking
                ? new JsonObject { ["type"] = "enabled", ["budget_tokens"] = budgetTokens }
                : new JsonObject { ["type"] = "disabled" };
            var outgoing = new HttpRequestMessage(HttpMethod.Post, messagesUri);
            outgoing.Headers.Add("x-api-key", apiKey);
            outgoing.Headers.Add("anthropic-version", "2023-06-01");
            if (!messagesUri.Host.Equals("api.anthropic.com", StringComparison.OrdinalIgnoreCase))
                outgoing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            // CLI 客户端指纹：部分中转站按 User-Agent 与特征头放行。
            foreach (var (name, value) in CliIdentityService.BuildHeaders(cliIdentity))
                outgoing.Headers.TryAddWithoutValidation(name, value);
            outgoing.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            return outgoing;
        }
        catch (ModelClientException) { throw; }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new ModelClientException("模型请求参数无效。", ModelErrorKind.InvalidRequest);
        }
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

    private async IAsyncEnumerable<ModelStreamEvent> ReadEventsAsync(HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), new UTF8Encoding(false, true));
        var data = new StringBuilder();
        var blocks = new SortedDictionary<int, ContentBlock>();
        var toolIds = new HashSet<string>(StringComparer.Ordinal);
        var started = false;
        string? finishReason = null;
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
                continue;
            }
            if (data.Length == 0) continue;
            using var document = JsonDocument.Parse(data.ToString());
            data.Clear();
            var root = document.RootElement;
            var type = root.GetProperty("type").GetString();
            if (type == "error")
            {
                var (code, message) = ModelErrorClassifier.ParseErrorBody(root.GetRawText());
                throw ProviderError(null, code, message);
            }
            if (type == "ping") continue;
            if (type == "message_start")
            {
                if (started) throw ProtocolError();
                started = true;
                continue;
            }
            if (!started) throw ProtocolError();
            switch (type)
            {
                case "content_block_start":
                {
                    if (finishReason is not null) throw ProtocolError();
                    var index = root.GetProperty("index").GetInt32();
                    var content = root.GetProperty("content_block");
                    var block = new ContentBlock(content.GetProperty("type").GetString()!);
                    if (index < 0 || !blocks.TryAdd(index, block)) throw ProtocolError();
                    switch (block.Type)
                    {
                        case "text":
                            var text = String(content, "text") ?? "";
                            if (text.Length > 0) yield return new TextDeltaEvent(text);
                            break;
                        case "thinking":
                            var thinking = String(content, "thinking") ?? "";
                            block.Thinking.Append(thinking);
                            block.Signature.Append(String(content, "signature"));
                            if (thinking.Length > 0) yield return new ReasoningDeltaEvent(thinking);
                            break;
                        case "redacted_thinking":
                            block.RedactedData = String(content, "data") ?? throw ProtocolError();
                            break;
                        case "tool_use":
                            block.Id = String(content, "id");
                            block.Name = String(content, "name");
                            if (string.IsNullOrWhiteSpace(block.Id) || string.IsNullOrWhiteSpace(block.Name) || !toolIds.Add(block.Id)) throw ProtocolError();
                            block.InitialInput = content.GetProperty("input").Clone();
                            break;
                        default: throw ProtocolError();
                    }
                    break;
                }
                case "content_block_delta":
                {
                    if (finishReason is not null || !blocks.TryGetValue(root.GetProperty("index").GetInt32(), out var block) || block.Closed) throw ProtocolError();
                    var delta = root.GetProperty("delta");
                    switch (delta.GetProperty("type").GetString())
                    {
                        case "text_delta" when block.Type == "text":
                            yield return new TextDeltaEvent(delta.GetProperty("text").GetString() ?? throw ProtocolError());
                            break;
                        case "thinking_delta" when block.Type == "thinking":
                            var thinking = delta.GetProperty("thinking").GetString() ?? throw ProtocolError();
                            block.Thinking.Append(thinking);
                            yield return new ReasoningDeltaEvent(thinking);
                            break;
                        case "signature_delta" when block.Type == "thinking":
                            block.Signature.Append(delta.GetProperty("signature").GetString() ?? throw ProtocolError());
                            break;
                        case "input_json_delta" when block.Type == "tool_use":
                            block.HasArgumentsDelta = true;
                            block.Arguments.Append(delta.GetProperty("partial_json").GetString() ?? throw ProtocolError());
                            break;
                        default: throw ProtocolError();
                    }
                    break;
                }
                case "content_block_stop":
                {
                    if (!blocks.TryGetValue(root.GetProperty("index").GetInt32(), out var block) || block.Closed) throw ProtocolError();
                    block.Closed = true;
                    if (block.Type == "tool_use")
                    {
                        var arguments = block.HasArgumentsDelta ? block.Arguments.ToString() : block.InitialInput.GetRawText();
                        using var input = JsonDocument.Parse(arguments);
                        if (input.RootElement.ValueKind != JsonValueKind.Object) throw ProtocolError();
                        yield return new ModelToolCallEvent(new ToolCall(block.Id!, block.Name!, arguments));
                    }
                    break;
                }
                case "message_delta":
                    if (blocks.Values.Any(block => !block.Closed)) throw ProtocolError();
                    finishReason = String(root.GetProperty("delta"), "stop_reason") ?? finishReason;
                    break;
                case "message_stop":
                {
                    if (string.IsNullOrWhiteSpace(finishReason) || blocks.Values.Any(block => !block.Closed)
                        || (toolIds.Count > 0 && finishReason != "tool_use") || (toolIds.Count == 0 && finishReason == "tool_use")) throw ProtocolError();
                    if (EnableThinking && toolIds.Count > 0)
                    {
                        var thoughts = new JsonArray();
                        foreach (var block in blocks.Values)
                        {
                            if (block.Type == "thinking" && !string.IsNullOrWhiteSpace(block.Signature.ToString()))
                                thoughts.Add(new JsonObject { ["type"] = "thinking", ["thinking"] = block.Thinking.ToString(), ["signature"] = block.Signature.ToString() });
                            else if (block.Type == "redacted_thinking")
                                thoughts.Add(new JsonObject { ["type"] = "redacted_thinking", ["data"] = block.RedactedData });
                        }
                        if (thoughts.Count > 0)
                            foreach (var id in toolIds) signedThinking[id] = thoughts;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new ModelCompletedEvent(finishReason);
                    yield break;
                }
                // Anthropic 可能增加无关事件；未知事件不能代替 message_stop。
            }
        }
        throw ProtocolError();
    }

    private ModelClientException ProviderError(int? status, string? code, string? message, TimeSpan? retryAfter = null)
    {
        var safeCode = Clean(code);
        if (safeCode?.Length > 64 || safeCode?.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-' and not '.') == true) safeCode = null;
        return new ModelClientException("模型服务请求失败。", ModelErrorClassifier.Classify(status, code), status, safeCode, Clean(message)) { RetryAfter = retryAfter };
    }

    private string? Clean(string? value) => ModelErrorClassifier.SanitizeUpstreamMessage(value?.Replace(apiKey, "***", StringComparison.Ordinal));
    private static ModelClientException ProtocolError() => new("模型响应无效或流未完整结束。", ModelErrorKind.Unknown);
    private static string? String(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class ContentBlock(string type)
    {
        public string Type { get; } = type;
        public string? Id { get; set; }
        public string? Name { get; set; }
        public JsonElement InitialInput { get; set; }
        public StringBuilder Arguments { get; } = new();
        public bool HasArgumentsDelta { get; set; }
        public StringBuilder Thinking { get; } = new();
        public StringBuilder Signature { get; } = new();
        public string? RedactedData { get; set; }
        public bool Closed { get; set; }
    }
}