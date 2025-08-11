using SmartFileOrganizer.Proxy.Config;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapPost("/plan", async (PlanRequest req) =>
{
    var apiKey = Secrets.OpenAiApiKey;
    if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("sk-REPLACE_ME"))
        return Results.Problem("OpenAI API key not configured in Secrets.OpenAiApiKey");

    var chatReq = new
    {
        model = "gpt-5", // pick your actual model
        messages = new object[]
        {
            new { role = "system", content = Prompt.System },
            new { role = "user", content = JsonSerializer.Serialize(req, JsonOpts.Default) }
        },
        response_format = new
        {
            type = "json_schema",
            json_schema = new
            {
                name = "clean_plan",
                schema = PlanSchema.Schema
            }
        },
        temperature = 0.2
    };

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", apiKey);

    var body = new StringContent(
        JsonSerializer.Serialize(chatReq),
        Encoding.UTF8,
        "application/json");

    var resp = await http.PostAsync("https://api.openai.com/v1/chat/completions", body);
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
        return Results.Problem($"OpenAI error {resp.StatusCode}: {json}");

    var parsed = JsonDocument.Parse(json);
    var content = parsed.RootElement
        .GetProperty("choices")[0]
        .GetProperty("message")
        .GetProperty("content")
        .GetString();

    if (string.IsNullOrWhiteSpace(content))
        return Results.Problem("Empty plan from model");

    var plan = JsonSerializer.Deserialize<CleanPlanDto>(content, JsonOpts.Default);
    if (plan is null) return Results.Problem("Plan parse failure");

    return Results.Json(plan, JsonOpts.Default);
});


app.MapPost("/commit", (CleanPlanDto plan) =>
{
    // Here you could store plans, audit usage, attach user id, etc.
    // For now just echo back and pretend we “committed”.
    return Results.Json(new { ok = true, planId = plan.PlanId ?? Guid.NewGuid().ToString("N") });
});

app.MapGet("/", () => Results.Ok(new
{
    service = "SmartFileOrganizer Proxy",
    status = "running"
}));
app.MapGet("/.well-known/appspecific/com.chrome.devtools.json",
    () => Results.Json(new { })); // or Results.NoContent()
app.MapGet("/health", () => Results.Ok(new
{
    service = "SmartFileOrganizer Proxy",
    status = "healthy",
    timestamp = DateTime.UtcNow
}));

app.Run();

// ---- DTOs / Schemas / Prompt ----
record PlanRequest(List<FileNodeDigest> roots, string scope, PreferencesDto preferences);
record PreferencesDto(
    bool GroupByType,
    bool GroupByDate,
    bool GroupByProject,
    bool KeepFolderNames,
    bool FlattenSmallFolders
);
record FileNodeDigest(string path, string name, bool isDir, long size, DateTime createdUtc, List<FileNodeDigest>? children);

record CleanPlanDto(
    string? PlanId,
    string Summary,
    List<MoveOpDto> Moves,
    List<string> DeleteEmpty,
    Dictionary<string, string>? RationaleByPath
);
record MoveOpDto(string Source, string Destination);

static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}

static class Prompt
{
    public const string System = """
You are a careful, local-file-organization planner. Output ONLY JSON matching the schema.

Consider user preferences:
- If preferences.groupByType = true, classify by MIME-like buckets (Images, Videos, Audio, Docs, Spreadsheets, Presentations, Code, Archives, Misc).
- If preferences.groupByDate = true, group inside type/project folders by YYYY/YYYY-MM.
- If preferences.groupByProject = true, infer projects by shared prefixes, parent folders, or common tokens (case-insensitive). Only create a project folder when ≥3 files match.
- If preferences.keepFolderNames = true, preserve obvious root folder names when creating destinations.
- If preferences.flattenSmallFolders = true, merge folders that would have < 5 items into their parent.

Hard constraints:
- Never delete non-empty folders.
- Exclude system paths (/System, /Library, C:\Windows, Program Files, etc.).
- Keep all operations inside the user-approved roots.
- Ensure destinations do not collide; propose unique names if necessary.
- Provide rationaleByPath for notable moves (esp. project/date decisions).
""";
}


static class PlanSchema
{
    public static readonly object Schema = new
    {
        type = "object",
        additionalProperties = false,
        required = new[] { "summary", "moves", "deleteEmpty" },
        properties = new Dictionary<string, object>
        {
            ["planId"] = new { type = "string" },
            ["summary"] = new { type = "string" },
            ["moves"] = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    additionalProperties = false,
                    required = new[] { "source", "destination" },
                    properties = new Dictionary<string, object>
                    {
                        ["source"] = new { type = "string" },
                        ["destination"] = new { type = "string" }
                    }
                }
            },
            ["deleteEmpty"] = new { type = "array", items = new { type = "string" } },
            ["rationaleByPath"] = new
            {
                type = "object",
                additionalProperties = new { type = "string" }
            }
        }
    };
}

