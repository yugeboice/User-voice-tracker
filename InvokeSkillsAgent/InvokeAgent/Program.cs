using InvokeAgent;
using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Agent;
using Microsoft.Lumina.Common.Models.A2A;
using Newtonsoft.Json;

var token = "";  // TODO: Replace with lumina endpoint token.
var computerId = "Test-Skills-Agent";  // Replace with your computer id.
var userQuery = "Get the brand guideline of Anthropic and output it to a markdown file";  // Replace with your user query
var checkResubscribe = true;   // If you need to see and test resubscribe, set it as true.

var options = new LuminaApiOptions
{
    Endpoint = "https://luminaserviceapi-test-westus.copilotlumina.com",  // Replace with lumina endpoint you prefer.
    LuminaApiTokenProvider = async () =>
    {
        return await Task.FromResult(token);
    },
    Scenario = "dev-int-westus2-2",
};

IHttpClientFactory factory = new SimpleHttpClientFactory();

var proxy = new LuminaServiceApiProxy(options, factory);

// Init computer.
var initRequest = new ComputerInitializeRequest
{
    ComputerId = computerId,
};

var initResponse = await proxy.InitializeAsync(initRequest);
Console.WriteLine($"Successfully initialized computer {initResponse?.Content?.ComputerId}");

// List agents and skills
var discoverResponse = await proxy.AgentDiscoveryAsync(computerId);
foreach (var agentCard in discoverResponse)
{
    Console.WriteLine(JsonConvert.SerializeObject(agentCard));
}

// Invoke agent

var lumina = new Dictionary<string, string>
{
    { "computer_id", computerId },
    { "agent_name", "skills-agent" },   // Agent name MUST be skills-agent.
};

var request = new A2ARequest
{
    JsonRpc = "2.0",
    Id = 1,
    Method = "message/stream",
    Params = new A2ARequestParams
    {
        Message = new Microsoft.Lumina.Common.Models.A2A.AgentMessage
        {
            Role = MessageRole.User,
            Parts = new List<Part>
            {
                new DataPart
                {
                    Data = new Dictionary<string, Newtonsoft.Json.Linq.JToken>
                    {
                        { "description", userQuery },
                    }
                }
            },
            MessageId = Guid.NewGuid().ToString()
        },
        Lumina = lumina,
        Metadata = new Dictionary<string, Newtonsoft.Json.Linq.JToken> { {"getTaskHistory", true } },  // Set this metadata if need to get sub-agent context.

    },
};

var outputFilePath = String.Empty;
var taskId = string.Empty;
var cts = new CancellationTokenSource();

// Parse messages.
await foreach (var response in proxy.SendSubAgentA2AMessageStreamAsync(request))
{
    var task = response.Result as AgentTask;
    var artifactUpdateEvent = response.Result as TaskArtifactUpdateEvent;

    if (checkResubscribe)
    {
        taskId = artifactUpdateEvent?.TaskId ?? task?.Id;
        cts.Cancel();
        Console.WriteLine("Cancel A2A request to test resubscribe.");
        break;
    }

    if (task != null)
    {
        Console.WriteLine("Received task object:");
        Console.WriteLine(JsonConvert.SerializeObject(task));
        continue;
    }

    if (artifactUpdateEvent == null)
    {
        Console.WriteLine("[WARNING] artifactUpdateEvent is null");
        continue;
    }


    var firstPart = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault();

    if (artifactUpdateEvent.LastChunk == true)
    {
        // Get final file path if last message.
        // Then extract output file path
        outputFilePath = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault()?.AsFilePart()?.File?.Uri?.ToString();
        if (string.IsNullOrEmpty(outputFilePath))
        {
            throw new Exception("Failed to get output file path");
        }

        taskId = artifactUpdateEvent.TaskId;
        Console.WriteLine($"Successfully retrieved output file path: {outputFilePath}");
    }
    else
    {
        // For not final message, print message.
        var message = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault()?.AsTextPart()?.Text;
        if (message != null)
        {
            Console.WriteLine($"Agent message: {message}");
        }
        else
        {
            Console.WriteLine("[WARNING] Message is null.");
        }
    }
}


if (checkResubscribe)
{
    var resubscribeRequest = new A2ATaskIdParamsRequest
    {
        Params = new A2ATaskIdParams
        {
            Id = taskId,
            Lumina = lumina,
        }
    };

    Console.WriteLine("Sending resubscribe request");
    await foreach (var response in proxy.SendSubAgentA2AResubscribeTasksAsync(resubscribeRequest))
    {
        // Parse message.
        var task = response.Result as AgentTask;
        var artifactUpdateEvent = response.Result as TaskArtifactUpdateEvent;

        if (task != null)
        {
            Console.WriteLine("Received task object:");
            Console.WriteLine(JsonConvert.SerializeObject(task));
            continue;
        }

        if (artifactUpdateEvent == null)
        {
            Console.WriteLine("[WARNING] artifactUpdateEvent is null");
            continue;
        }


        var firstPart = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault();

        if (artifactUpdateEvent.LastChunk == true)
        {
            // Get final file path if last message.
            // Then extract output file path
            outputFilePath = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault()?.AsFilePart()?.File?.Uri?.ToString();
            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw new Exception("Failed to get output file path");
            }

            taskId = artifactUpdateEvent.TaskId;
            Console.WriteLine($"Successfully retrieved output file path: {outputFilePath}");
        }
        else
        {
            // For not final message, print message.
            var message = artifactUpdateEvent.Artifact?.Parts?.FirstOrDefault()?.AsTextPart()?.Text;
            if (message != null)
            {
                Console.WriteLine($"Agent message: {message}");
            }
            else
            {
                Console.WriteLine("[WARNING] Message is null.");
            }
        }
    }
}

// Use sync file to download output file.
var syncFileRequest = new ComputerSyncFileRequest
{
    FilePath = outputFilePath,
    ComputerId = computerId,
};
var syncFileResponse = await proxy.SyncFileAsync(syncFileRequest);
var filePath = syncFileResponse?.Content?.FileName;
if (string.IsNullOrEmpty(filePath))
{
    throw new Exception("File name is null or empty");
}
var fileName = Path.GetFileName(filePath);
var fileContent = syncFileResponse?.Content?.Content;
if (string.IsNullOrEmpty(fileContent))
{
    throw new Exception("File content is null or empty");
}

Console.WriteLine($"Successfully get file {fileName}.");

// Save output file to local.
var fileBytes = Convert.FromBase64String(fileContent);
File.WriteAllBytes(fileName, fileBytes);

Console.WriteLine($"Successfully download file to {fileName}");