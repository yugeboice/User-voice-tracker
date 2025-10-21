Installation & Usage
This document explains how to install and use the Lumina SDK.

Prerequisites
.NET development environment
Valid access token (see Access for details)
Step 1: Install NuGet Package
Package Information
Feed URL: https://o365exchange.pkgs.visualstudio.com/_packaging/Enzyme/nuget/v3/index.json
Package Name: Microsoft.Lumina
Installation Command
Install-Package Microsoft.Lumina -Version {latestVersion}
Example:

Install-Package Microsoft.Lumina -Version 2025.8.27.441
Step 2: Initialize LuminaServiceApiProxy
Please refer to Access to obtain your access token.

Option A: Without Dependency Injection
var options = new LuminaApiOptions
{
    Endpoint = "https://luminaserviceapi-test-westus.copilotlumina.com",
    LuminaApiTokenProvider = async () =>
    {
        // Get access token
        return await Task.FromResult("fake_token_for_testing");
    },
    // Optional. Recommended as identity of the caller service
    Scenario = "<Any scenario>",
    // Optional. Recommended as category defined by caller service, such as PROD, TEST, Runner and so on
    TrafficType = "<Any traffic type>"
};

// If without DI, implement a custom HTTP factory
IHttpClientFactory factory = new DefaultHttpClientFactory();

var proxy = new LuminaServiceApiProxy(options, factory);
Option B: With Dependency Injection
For a complete example, see: AutofacStartupModule.cs

1. Register HTTP Client
builder.Services.AddHttpClient();
2. Register LuminaServiceApiProxy
builder.RegisterType<LuminaServiceApiProxy>()
    .AsSelf()
    .SingleInstance();
3. Configure LuminaApiOptions
builder.Register(c =>
{
    var luminaApiOption = c.Resolve<IConfiguration>()
        .GetSection("LuminaApiOptions")
        .Get<LuminaApiOptions>() ?? new LuminaApiOptions();

    luminaApiOption.LuminaApiTokenProvider = async () =>
    {
        // Get access token
        return await Task.FromResult("fake_token_for_testing");
    };

    return luminaApiOption;
}).As<LuminaApiOptions>().SingleInstance();
Step 3: Use LuminaServiceApiProxy
Usage Example
For a complete usage example, see: LuminaApiController.cs

Search Request Example
var searchRequest = new SearchRequest
{
    Requests = new List<SearchRequestItem>
    {
        new SearchRequestItem
        {
            Q = "Microsoft Azure Functions",
            TopN = 3,
            Source = SearchProviders.WebWithBing,
            Language = "en",
            Market = "en-US",
            CountryCode = "us",
            AdditionalConfig = new SearchRequestAdditionalConfig
            {
                MaxSemanticDocumentLength = 50
            }
        }
    },
    // Optional. For easier investigation, caller service is encouraged to provide as many identity fields as possible.
    Context = new LuminaContext
    {
        TraceId = "<Any trace Id>",
        RequestId = "<Any request Id>",
        CorrelationId = "<Any correlation Id>",
        ClientRequestId = "<Any client request Id>",
        ConversationId = "<Any conversation Id>",
        // True to enable debug mode (include more logs in HTTP response)
        IsDebugMode = true
    }
};

var searchResult = await proxy.SearchAsync(searchRequest);