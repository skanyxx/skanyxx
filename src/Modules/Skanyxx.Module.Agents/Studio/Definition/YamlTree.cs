using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Skanyxx.Module.Agents.Studio.Definition;

/// <summary>
/// YAML ⇄ JSON tree. Writing: block style, multi-line text as a literal block, strings quoted wherever they would read as
/// something else. Reading: every scalar comes back as a string (YAML has no schema here), duplicate keys and tags fail.
/// The text read is untrusted (anyone who can push to a branch, D111), so before anything is built from it one flat pass
/// over the parser's events refuses what a manifest never has and what costs the process: anchors and aliases (an alias
/// bomb expands a few hundred bytes into gigabytes), nesting deeper than <see cref="MaxDepth"/> (the deserializer
/// recurses, and a stack overflow cannot be caught), more than <see cref="MaxEvents"/> nodes, a second document.
/// </summary>
internal static class YamlTree
{
    public const int MaxDepth = 16;

    public const int MaxEvents = 5_000;

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithQuotingNecessaryStrings()
        .WithEventEmitter(next => new LiteralMultiline(next))
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder().WithDuplicateKeyChecking().Build();

    public static string Write(JsonObject tree) => Serializer.Serialize(Plain(tree));

    /// <exception cref="YamlException">Not YAML, or a shape a manifest never has.</exception>
    public static JsonNode? Read(string yaml)
    {
        Inspect(yaml);
        return Json(Deserializer.Deserialize<object?>(yaml));
    }

    private static void Inspect(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));
        int depth = 0, events = 0, documents = 0;
        while (parser.MoveNext())
        {
            var current = parser.Current!;
            if (++events > MaxEvents)
                throw new YamlException($"More than {MaxEvents} YAML nodes.");
            switch (current)
            {
                case AnchorAlias:
                    throw new YamlException("YAML aliases are not allowed.");
                case NodeEvent { Anchor.IsEmpty: false }:
                    throw new YamlException("YAML anchors are not allowed.");
                case DocumentStart when ++documents > 1:
                    throw new YamlException("One YAML document per file.");
            }
            if (current is MappingStart or SequenceStart && ++depth > MaxDepth)
                throw new YamlException($"YAML nested deeper than {MaxDepth} levels.");
            if (current is MappingEnd or SequenceEnd)
                depth--;
        }
    }

    /// <summary>The same tree with every scalar as a string, as <see cref="Read"/> returns it.</summary>
    public static JsonNode? AsRead(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.Select(p => KeyValuePair.Create(p.Key, AsRead(p.Value)))),
        JsonArray a => new JsonArray([.. a.Select(AsRead)]),
        JsonValue v => JsonValue.Create(Scalar(v)),
        _ => null
    };

    private static string Scalar(JsonValue value) => value.TryGetValue<bool>(out var b) ? (b ? "true" : "false")
        : value.TryGetValue<int>(out var i) ? i.ToString(CultureInfo.InvariantCulture)
        : value.GetValue<string>();

    private static object? Plain(JsonNode? node) => node switch
    {
        JsonObject o => o.ToDictionary(p => p.Key, p => Plain(p.Value)),
        JsonArray a => a.Select(Plain).ToList(),
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when v.TryGetValue<int>(out var i) => i,
        JsonValue v => v.GetValue<string>(),
        _ => null
    };

    private static JsonNode? Json(object? value) => value switch
    {
        null => null,
        IDictionary<object, object?> map => new JsonObject(map.Select(p => KeyValuePair.Create(
            p.Key as string ?? throw new YamlException("Keys must be text."), Json(p.Value)))),
        IList<object?> list => new JsonArray([.. list.Select(Json)]),
        string text => JsonValue.Create(text),
        _ => throw new YamlException("Unexpected YAML value.")
    };

    private sealed class LiteralMultiline(IEventEmitter next) : ChainedEventEmitter(next)
    {
        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is string text && text.Contains('\n'))
                eventInfo.Style = ScalarStyle.Literal;
            base.Emit(eventInfo, emitter);
        }
    }
}
