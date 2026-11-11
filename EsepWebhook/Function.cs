using System.Text;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace EsepWebhook;

public class Function
{
    // Reused across warm invocations instead of creating a new client per call.
    private static readonly HttpClient Client = new();

    public string FunctionHandler(object input, ILambdaContext context)
    {
        string raw = input?.ToString() ?? string.Empty;
        context.Logger.LogLine($"Received: {raw}");

        // Plain strings ("test this" in the console, "test" in Postman) are not JSON objects.
        // Keep the starter behaviour for them so the Part 2/3 tests still pass.
        JToken json;
        try
        {
            json = JToken.Parse(raw);
        }
        catch (JsonReaderException)
        {
            return raw.ToUpper();
        }

        // GitHub's "ping" event (and anything else without an issue) has no issue.html_url.
        string? issueUrl = json.Type == JTokenType.Object
            ? (string?)json.SelectToken("issue.html_url")
            : null;

        if (string.IsNullOrEmpty(issueUrl))
        {
            context.Logger.LogLine("No issue.html_url in payload; nothing posted to Slack.");
            return "ignored";
        }

        string? slackUrl = Environment.GetEnvironmentVariable("SLACK_URL");
        if (string.IsNullOrEmpty(slackUrl))
        {
            throw new InvalidOperationException("SLACK_URL environment variable is not set.");
        }

        string payload = JsonConvert.SerializeObject(new { text = $"Issue Created: {issueUrl}" });

        using var request = new HttpRequestMessage(HttpMethod.Post, slackUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = Client.Send(request);
        using var reader = new StreamReader(response.Content.ReadAsStream());
        string body = reader.ReadToEnd();

        context.Logger.LogLine($"Slack responded {(int)response.StatusCode}: {body}");
        return body;
    }
}
