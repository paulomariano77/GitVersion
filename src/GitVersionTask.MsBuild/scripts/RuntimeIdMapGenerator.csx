#r "Newtonsoft.Json.dll"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

// The RIDs supported by LibGit2 - these match the directory names (runtimes/{name}/native).
var availableRids = new[]
{
    "alpine-x64",
    "alpine.3.9-x64",
    "debian.9-x64",
    "fedora-x64",
    "linux-x64",
    "osx",
    "rhel-x64",
    "ubuntu.18.04-x64",
    "win-x64",
    "win-x86",
};

var url = "https://raw.githubusercontent.com/dotnet/runtime/master/src/libraries/pkg/Microsoft.NETCore.Platforms/runtime.json";
var runtimeJsonContent = (new WebClient()).DownloadString(url);

var runtimes = BuildRuntimeGraph();

var map = new Dictionary<string, string>(StringComparer.Ordinal);

foreach (var entry in runtimes)
{
    // Compatible rids are sorted from most specific to least specific, find the most specific that's available:
    var rid = GetCompatibleRuntimeIdentifiers(runtimes, entry.Key).FirstOrDefault(compatibleRid => availableRids.Contains(compatibleRid));
    if (rid != null)
    {
        map.Add(entry.Key, rid);
    }
}

var orderedMap = map.OrderBy(e => e.Key, StringComparer.Ordinal);

var sb = new StringBuilder();
sb.Append(@"#if !NETFRAMEWORK
namespace GitVersion.MSBuildTask.LibGit2Sharp
{
    internal static partial class RuntimeIdMap
    {
        // The following tables were generated by scripts/RuntimeIdMapGenerator.csx.
        // Regenerate when upgrading LibGit2Sharp to a new version that supports more platforms.
");

var indent = "        ";

sb.AppendLine(indent + "private static readonly string[] SRids = new[]");
sb.AppendLine(indent + "{");

foreach (var entry in orderedMap)
{
    sb.AppendLine(indent + $"    \"{entry.Key}\",");
}

sb.AppendLine(indent + "};");
sb.AppendLine();
sb.AppendLine(indent + "private static readonly string[] SDirectories = new[]");
sb.AppendLine(indent + "{");

foreach (var entry in orderedMap)
{
    sb.AppendLine(indent + $"    \"{entry.Value}\",");
}

sb.Append(indent + "};");
sb.AppendLine(@"
    }
}
#endif
");

Console.Write(sb);

var outputFile = Path.Combine(GetScriptDir(), "RuntimeIdMap.cs");
File.WriteAllText(outputFile, sb.ToString());
string GetScriptDir([CallerFilePath] string path = null) => Path.GetDirectoryName(path);

Dictionary<string, Runtime> BuildRuntimeGraph()
{
    var rids = new Dictionary<string, Runtime>();

    var json = JObject.Parse(runtimeJsonContent);
    var runtimes = (JObject)json["runtimes"];

    foreach (var runtime in runtimes)
    {
        var imports = (JArray)((JObject)runtime.Value)["#import"];
        rids.Add(runtime.Key, new Runtime(runtime.Key, imports.Select(import => (string)import).ToArray()));
    }

    return rids;
}

struct Runtime
{
    public string RuntimeIdentifier { get; }
    public string[] ImportedRuntimeIdentifiers { get; }

    public Runtime(string runtimeIdentifier, string[] importedRuntimeIdentifiers)
    {
        RuntimeIdentifier = runtimeIdentifier;
        ImportedRuntimeIdentifiers = importedRuntimeIdentifiers;
    }
}

List<string> GetCompatibleRuntimeIdentifiers(Dictionary<string, Runtime> runtimes, string runtimeIdentifier)
{
    var result = new List<string>();

    if (runtimes.TryGetValue(runtimeIdentifier, out var initialRuntime))
    {
        var queue = new Queue<Runtime>();
        var hash = new HashSet<string>();

        hash.Add(runtimeIdentifier);
        queue.Enqueue(initialRuntime);

        while (queue.Count > 0)
        {
            var runtime = queue.Dequeue();
            result.Add(runtime.RuntimeIdentifier);

            foreach (var item in runtime.ImportedRuntimeIdentifiers)
            {
                if (hash.Add(item))
                {
                    queue.Enqueue(runtimes[item]);
                }
            }
        }
    }
    else
    {
        result.Add(runtimeIdentifier);
    }

    return result;
}
