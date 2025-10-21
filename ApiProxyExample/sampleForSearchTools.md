Sample for search tools
This page demonstrates how to leverage Lumina search tools to enhance LLM search capabilities. We will try to answer user utterance: help me understand Microsoft Azure Functions hosting.

Let's try to think like a LLM in the rest of this page.

Search
Use search API to first understand what is Microsoft Azure Functions.

var query = "Microsoft Azure Functions";
var searchResponse = await SearchAsync(proxy, query, 3).ConfigureAwait(false);
Console.WriteLine($"action={searchResponse.PageContext.Action}/turn={searchResponse.PageContext.Turn}");
for (int i = 0; i < searchResponse.Results.Count; i++)
{
    Console.WriteLine($"id={i} - {searchResponse.Results[i].Url}");
}
From the response, we can get some URLs that are related to the query, indexed from 0. We can deep dive into some of the pages using the index later.

action=search/turn=0
id=0 - https://learn.microsoft.com/en-us/azure/azure-functions/functions-overview
id=1 - https://azure.microsoft.com/en-us/products/functions/
id=2 - https://learn.microsoft.com/en-us/azure/azure-functions/functions-get-started
In search response, we can also get an identifier sessionId which identifies current session. In the following requests, we should keep this identifier in the requests to let Lumina service be aware the context.

var sessionId = searchResponse!.ToolState!.SessionId!;
Console.WriteLine($"session id: {sessionId}");
session id: cccc20d19d8c4b4e859f76473dba4a30
Open
Suppose we want to get more information from the search result with index 0, we can use open API to move into the URL. In Lumina service, we use PageContextInfo to locate the page.

From the open response, we can get basic information like URL, title and text content. For links in the page, you can get its placeholder and URL from .Doc.Links.

var openPageContext = new PageContextInfo
{
    Action = searchResponse.PageContext.Action,
    Turn = searchResponse.PageContext.Turn,
    Id = 0
};
var openResponse = await OpenAsync(proxy, sessionId, openPageContext);
Console.WriteLine($"URL={openResponse.Pages[0].Url}");
Console.WriteLine($"Title={openResponse.Pages[0].Title}");
Console.WriteLine($"Content(head 50 chars)={openResponse.Pages[0].Content!.Substring(0, 50)}");
Console.WriteLine($"action={openResponse.Pages[0].PageContext.Action}/turn={openResponse.Pages[0].PageContext.Turn}/id={openResponse.Pages[0].PageContext.Id}");
Console.WriteLine($"link13 name={openResponse.Pages[0].Doc.Links[13].Name} URL={openResponse.Pages[0].Doc.Links[13].Url}");
URL=https://learn.microsoft.com/en-us/azure/azure-functions/functions-overview
Title=Azure Functions overview | Microsoft Learn
Content(head 50 chars)=Azure Functions overview | Microsoft Learn[[[link_
action=view/turn=1/id=0
link13 name=Build a scalable web API URL=https://learn.microsoft.com/en-us/azure/azure-functions/functions-scenarios#build-a-scalable-web-api
Find
Use find API to focus on specific pattern within the page, which is similar to use "Control+F" when browse a web page. Use similar page context to tell Lumina service which page you expect to apply the find.

var pattern = "Hosting";
var findPageContext = new PageContextInfo
{
    Action = openResponse.Pages[0].PageContext.Action,
    Turn = openResponse.Pages[0].PageContext.Turn,
    Id = openResponse.Pages[0].PageContext.Id
};
var findResponse = await FindAsync(proxy, sessionId, pattern, findPageContext);
var lines = openResponse.Pages[0].Content!.Split("\n");
for (int i = 0; i < findResponse.Results!.Count; i++)
{
    Console.WriteLine($"{i} - L{findResponse.Results[i].LineIdx} -> {lines[(int)findResponse.Results[i].LineIdx! - 1]}");
}
Find response will return the line numebr of the matched content.

0 - L11 -> [[[link_3]]][[[link_4]]][[[link_5]]][[[link_6]]]
1 - L46 -> ## Hosting options
2 - L48 -> Functions provides various [[[link_22]]] for your business needs and application workload. [[[link_23]]] range from fully serverless, where you only pay for execution time (Consumption plan), to always-warm instances kept ready for the fastest response times (Premium plan).
3 - L50 -> When you have excess App Service hosting resources, you can host your functions in an existing App Service plan. This kind of Dedicated hosting plan is also a good choice when you need predictable scaling behaviors and costs from your functions.
Click
If there is any link that might provide more information, use click API to navigate to it. We can determine the interesting link index via iterating pages' links info.

var clickPageContext = new PageContextInfo
{
    Action = openResponse.Pages[0].PageContext.Action,
    Turn = openResponse.Pages[0].PageContext.Turn,
    Id = openResponse.Pages[0].PageContext.Id
};
var clickResponse = await ClickAsync(proxy, sessionId, "13", clickPageContext);
Console.WriteLine($"URL={clickResponse.Pages[0].Url}");
Console.WriteLine($"Title={clickResponse.Pages[0].Title}");
Console.WriteLine($"Content(head 50 chars)={clickResponse.Pages[0].Content!.Substring(0, 50)}");
Console.WriteLine($"action={clickResponse.Pages[0].PageContext.Action}/turn={clickResponse.Pages[0].PageContext.Turn}/id={clickResponse.Pages[0].PageContext.Id}");
The click result is same as an open result.

URL=https://learn.microsoft.com/en-us/azure/azure-functions/functions-scenarios#build-a-scalable-web-api
Title=Build a scalable web API
Content(head 50 chars)=Azure Functions Scenarios | Microsoft Learn[[[link
action=view/turn=3/id=0
Contextual information
As we mentioned before, Lumina service uses an identifier sessionId to indicate the session. We can find all interactions and browsed pages within the session from toolState. Lumina service provides API to get full tool state.

var toolState = await proxy.GetToolStateAsync(sessionId);