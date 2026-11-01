// Copyright (c) RazorConsole. All rights reserved.

// Checks that the RazorConsole agent plugin (plugins/razorconsole) says the same thing to every client.
//
//   dotnet run scripts/verify-agent-plugin.cs [-- --root <repository root>]
//
// It fails, listing every problem found, unless:
//   - the three manifests (plugin.json, .claude-plugin/plugin.json, .antigravity-plugin/plugin.json) have the same
//     name, version, description and author name, and the version is a semantic version;
//   - the three MCP files (mcp.json, .mcp.json, mcp_config.json) define the same razorconsole server: the same command
//     and args, and "type": "stdio" where the client requires it;
//   - both marketplaces (.claude-plugin/marketplace.json, .agents/plugins/marketplace.json) list the plugin by name and
//     point at ./plugins/razorconsole, without a version of their own;
//   - every skill directory has a SKILL.md whose frontmatter name is the directory name (lower-case kebab-case) and
//     whose description is not empty.

using System.Text.Json;
using System.Text.RegularExpressions;

var root = ResolveRoot(args);
if (root is null)
{
    Console.Error.WriteLine("Could not find the repository root (a directory with plugins/razorconsole).");
    return 2;
}

var plugin = Path.Combine(root, "plugins", "razorconsole");
var problems = new List<string>();

// Manifests.
var manifests = new (string Path, JsonElement? Json)[]
{
    ("plugin.json", Load(plugin, "plugin.json", problems)),
    (".claude-plugin/plugin.json", Load(plugin, ".claude-plugin/plugin.json", problems)),
    (".antigravity-plugin/plugin.json", Load(plugin, ".antigravity-plugin/plugin.json", problems)),
};

foreach (var field in new[] { "name", "version", "description" })
{
    CompareAll(
        $"manifest {field}",
        manifests.Where(m => m.Json is not null).Select(m => (m.Path, Text(m.Json!.Value, field))),
        problems);
}

CompareAll(
    "manifest author name",
    manifests.Where(m => m.Json is not null).Select(m => (m.Path, AuthorName(m.Json!.Value))),
    problems);

foreach (var (path, json) in manifests)
{
    if (json is null)
    {
        continue;
    }

    var version = Text(json.Value, "version");
    if (version is null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$"))
    {
        problems.Add($"{path}: version '{version}' is not a semantic version.");
    }

    var name = Text(json.Value, "name");
    if (name is null || !Regex.IsMatch(name, "^[a-z0-9-]+$"))
    {
        problems.Add($"{path}: name '{name}' must be lower-case kebab-case.");
    }
}

// MCP files.
var mcpFiles = new (string Path, bool RequiresType)[]
{
    ("mcp.json", true),
    (".mcp.json", false),
    ("mcp_config.json", false),
};

var servers = new List<(string Path, string? Definition)>();
foreach (var (path, requiresType) in mcpFiles)
{
    var json = Load(plugin, path, problems);
    if (json is null)
    {
        continue;
    }

    if (!json.Value.TryGetProperty("mcpServers", out var mcpServers)
        || mcpServers.ValueKind != JsonValueKind.Object
        || !mcpServers.TryGetProperty("razorconsole", out var server))
    {
        problems.Add($"{path}: no mcpServers.razorconsole entry.");
        continue;
    }

    var type = Text(server, "type");
    if (requiresType && type != "stdio")
    {
        problems.Add($"{path}: type must be 'stdio', not '{type}'.");
    }

    if (!requiresType && type is not null && type != "stdio")
    {
        problems.Add($"{path}: type must be 'stdio' or absent, not '{type}'.");
    }

    var args2 = server.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array
        ? string.Join(' ', argsElement.EnumerateArray().Select(a => a.GetString()))
        : "";
    servers.Add((path, $"{Text(server, "command")} {args2}".Trim()));
}

CompareAll("MCP server command line", servers, problems);

// Marketplaces.
CheckMarketplace(root, ".claude-plugin/marketplace.json", problems);
CheckMarketplace(root, ".agents/plugins/marketplace.json", problems);

// Skills.
var skillsDirectory = Path.Combine(plugin, "skills");
if (!Directory.Exists(skillsDirectory))
{
    problems.Add("plugins/razorconsole/skills does not exist.");
}
else
{
    var directories = Directory.GetDirectories(skillsDirectory);
    if (directories.Length == 0)
    {
        problems.Add("plugins/razorconsole/skills has no skills.");
    }

    foreach (var directory in directories)
    {
        CheckSkill(directory, problems);
    }
}

if (problems.Count > 0)
{
    Console.Error.WriteLine("The agent plugin is inconsistent:");
    foreach (var problem in problems)
    {
        Console.Error.WriteLine($"  - {problem}");
    }

    return 1;
}

Console.WriteLine("The agent plugin is consistent.");
return 0;

static string? ResolveRoot(string[] args)
{
    var index = Array.IndexOf(args, "--root");
    if (index >= 0 && index + 1 < args.Length)
    {
        return Directory.Exists(Path.Combine(args[index + 1], "plugins", "razorconsole")) ? Path.GetFullPath(args[index + 1]) : null;
    }

    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "plugins", "razorconsole")))
        {
            return directory.FullName;
        }
    }

    return null;
}

static JsonElement? Load(string directory, string relativePath, List<string> problems)
{
    var path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(path))
    {
        problems.Add($"{relativePath}: file is missing.");
        return null;
    }

    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
    catch (JsonException exception)
    {
        problems.Add($"{relativePath}: invalid JSON ({exception.Message}).");
        return null;
    }
}

static string? Text(JsonElement element, string property) =>
    element.ValueKind == JsonValueKind.Object
    && element.TryGetProperty(property, out var value)
    && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;

static string? AuthorName(JsonElement manifest)
{
    if (!manifest.TryGetProperty("author", out var author))
    {
        return null;
    }

    return author.ValueKind == JsonValueKind.String ? author.GetString() : Text(author, "name");
}

static void CompareAll(string what, IEnumerable<(string Path, string? Value)> values, List<string> problems)
{
    var list = values.ToList();
    if (list.Any(v => string.IsNullOrWhiteSpace(v.Value)))
    {
        foreach (var missing in list.Where(v => string.IsNullOrWhiteSpace(v.Value)))
        {
            problems.Add($"{missing.Path}: {what} is missing or empty.");
        }

        return;
    }

    if (list.Select(v => v.Value).Distinct(StringComparer.Ordinal).Count() > 1)
    {
        problems.Add($"{what} differs: " + string.Join("; ", list.Select(v => $"{v.Path} = '{v.Value}'")));
    }
}

static void CheckMarketplace(string root, string relativePath, List<string> problems)
{
    var json = Load(root, relativePath, problems);
    if (json is null)
    {
        return;
    }

    if (!json.Value.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Array)
    {
        problems.Add($"{relativePath}: no plugins array.");
        return;
    }

    var entry = plugins.EnumerateArray().FirstOrDefault(p => Text(p, "name") == "razorconsole");
    if (entry.ValueKind != JsonValueKind.Object)
    {
        problems.Add($"{relativePath}: no plugin named 'razorconsole'.");
        return;
    }

    if (!entry.TryGetProperty("source", out var source))
    {
        problems.Add($"{relativePath}: the razorconsole entry has no source.");
        return;
    }

    var path = source.ValueKind == JsonValueKind.String ? source.GetString() : Text(source, "path");
    if (path != "./plugins/razorconsole")
    {
        problems.Add($"{relativePath}: source is '{path}', expected './plugins/razorconsole'.");
    }

    if (entry.TryGetProperty("version", out _))
    {
        problems.Add($"{relativePath}: remove 'version' from the entry; the plugin manifest owns it.");
    }
}

static void CheckSkill(string directory, List<string> problems)
{
    var directoryName = Path.GetFileName(directory);
    var relative = $"plugins/razorconsole/skills/{directoryName}/SKILL.md";
    var file = Path.Combine(directory, "SKILL.md");
    if (!File.Exists(file))
    {
        problems.Add($"{relative}: file is missing.");
        return;
    }

    if (!Regex.IsMatch(directoryName, "^[a-z0-9]+(-[a-z0-9]+)*$"))
    {
        problems.Add($"{relative}: directory name must be lower-case kebab-case.");
    }

    var text = File.ReadAllText(file).Replace("\r\n", "\n");
    var match = Regex.Match(text, @"\A---\n(?<front>.*?)\n---\n", RegexOptions.Singleline);
    if (!match.Success)
    {
        problems.Add($"{relative}: no YAML frontmatter.");
        return;
    }

    var front = match.Groups["front"].Value;
    var name = Regex.Match(front, @"^name:\s*(?<v>.+?)\s*$", RegexOptions.Multiline).Groups["v"].Value.Trim('"', '\'');
    var description = Regex.Match(front, @"^description:\s*(?<v>.+?)\s*$", RegexOptions.Multiline).Groups["v"].Value.Trim('"', '\'');

    if (name != directoryName)
    {
        problems.Add($"{relative}: frontmatter name '{name}' must be '{directoryName}'.");
    }

    if (string.IsNullOrWhiteSpace(description))
    {
        problems.Add($"{relative}: description is missing or empty.");
    }
}
