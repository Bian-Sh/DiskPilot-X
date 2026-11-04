using System.Text.Json;
using System.Text.Json.Nodes;
namespace TinyHarness;

/// <summary>仅本轮有效；参数规范化后累计重复失败，重复相同查询不视为进展。</summary>
public sealed class RepeatedToolFailureGuard
{
    private readonly Dictionary<string,int> failures = new(StringComparer.Ordinal);
    private readonly HashSet<string> successes = new(StringComparer.Ordinal);
    public bool Observe(string tool,string arguments,bool success,string result)
    {
        var input=tool+"\n"+Canonical(arguments);
        if(success)
        {
            if(successes.Add(input+"\n"+result)) failures.Clear();
            return false;
        }
        var error=result;
        try { var node=JsonNode.Parse(result); error=node?["code"]?.ToString()??node?["error"]?.ToString()??node?["reason"]?.ToString()??result; }
        catch(JsonException) { }
        var key=input+"\n"+error;
        failures.TryGetValue(key,out var count); failures[key]=++count;
        return count>=3;
    }
    private static string Canonical(string raw)
    {
        try { return Normalize(JsonNode.Parse(raw))?.ToJsonString()??"null"; } catch(JsonException) { return raw; }
    }
    private static JsonNode? Normalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(pair=>pair.Key,StringComparer.Ordinal).Select(pair=>new KeyValuePair<string,JsonNode?>(pair.Key,Normalize(pair.Value)))),
        JsonArray array => new JsonArray(array.Select(Normalize).ToArray()),
        _ => node?.DeepClone()
    };
}
