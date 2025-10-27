Search APIs
This document provides examples and documentation for Lumina's search APIs. Lumina offers two primary search APIs: the general search endpoint and the agent browser search endpoint, each designed for different use cases and integration scenarios.

Overview
Lumina's search capabilities are built on a robust architecture that supports multiple search providers, primarily leveraging Bing Search API for web content retrieval. The system provides both traditional search functionality and agent-based browser interactions for more complex search scenarios.

Key Features
Multi-provider support: Configurable search providers with Bing as the primary source
Batch search capabilities: Process multiple search queries in a single request
Session management: Maintain search context across multiple operations
Rich content extraction: Semantic document content and metadata retrieval
Agent integration: Browser automation for complex search interactions
Available Search APIs
Core Search API
Batch Search
POST /api/sonicberry/search
The primary search endpoint for retrieving content from multiple sources with batch processing capabilities.

Request Parameters:

Parameter	Type	Required	Description
requests	SearchRequestItem[]	Yes	Array of search request items
toolState	ToolState	No	Session state for maintaining context
SearchRequestItem Parameters:

Parameter	Type	Required	Description
q	string	Yes	The search query string
topn	integer	No	Maximum number of webPage results to return (default: 10)
recency	integer	No	Number of days for recency filtering
source	string	No	Search source (default: "web_with_bing")
domains	string[]	No	Specific domains to restrict search results to
countryCode	string	No	Country code for localized search (default: "us")
language	string	No	Language preference for search results (default: "en")
market	string	No	Bing search market (default: "en-US")
userIpAddress	string	No	User IP address for location-based results
additionalConfig	SearchRequestAdditionalConfig	No	Advanced search configuration options
SearchRequestAdditionalConfig Parameters:

Parameter	Type	Default	Description
maxGroundingResults	integer	50	Maximum number of grounding results
maxGroundingResultSize	integer	-1	Maximum size of grounding results (-1 = no limit)
maxNonWebAnswers	integer	-1	Maximum number of non-web answers
maxSnippetLength	integer	-1	Maximum snippet length
maxSemanticDocumentLength	integer	-1	Maximum semantic document length
maxNewsItems	integer	-1	Maximum number of news items
maxWeatherItems	integer	-1	Maximum number of weather items
maxFinanceItems	integer	-1	Maximum number of finance items
filter	string[]	[]	Filter criteria for search results
features	string[]	[]	Features enabled for the search request
sf	string[]	["gndcombineweb", "enawebanscon"]	Search flags for result context
Example Request:

{
  "requests": [
    {
      "q": "Microsoft Azure Functions serverless computing",
      "topn": 5,
      "recency": 30,
      "source": "web_with_bing",
      "countryCode": "US",
      "language": "en"
    },
    {
      "q": "What is Large Language Model?",
      "topn": 3,
      "source": "web_with_bing",
      "domains": ["wikipedia.org", "arxiv.org"]
    }
  ],
  "toolState": {
    "sessionId": "existing-session-id"
  }
}
Example Response:

{
  "pageId": "29893556ad1845189e439459355201d3",
  "result": "Search results content formatted for display...",
  "results": [
    {
      "answerType": "WebPages",
      "url": "https://learn.microsoft.com/en-us/azure/azure-functions/",
      "title": "Azure Functions overview | Microsoft Learn",
      "semanticDocument": "Azure Functions is a serverless solution that allows you to write less code..."
    }
  ],
  "toolState": {
    "sessionId": "995e40972e2c4cf0af8c50d5efd045e9"
  }
}
Core Open
POST /api/sonicberry/open
Open and retrieve content from specific URLs or page references from previous operations.

Request Parameters:

Parameter	Type	Required	Description
requests	OpenRequestItem[]	Yes	Array of open request items
toolState	ToolState	No	Session state for maintaining context
OpenRequestItem Parameters:

Parameter	Type	Required	Description
refId	string	No	Direct URL or page identifier (e.g., "https://example.com" or "turn0search0")
lineNo	integer	No	Starting line number within the content (default: 0)
numLines	integer	No	Number of lines to retrieve (null = no limit)
pageContext	PageContextInfo	No	Reference to page from previous operation
cursorRequest	CursorRequestInfo	No	Cursor-based navigation information
approach	string	No	Approach to use for retrieving content
source	string	No	Source for content retrieval (default: "web_with_bing")
Example Request:

{
  "requests": [
    {
      "refId": "https://learn.microsoft.com/en-us/azure/azure-functions/"
    }
  ],
  "toolState": {
    "sessionId": "session-id-here"
  }
}
Example Response:

{
  "result": "Turn0fetch0:\nAzure Functions overview content with deep links...",
  "toolState": {
    "sessionId": "session-id-here"
  }
}
Core Find
POST /api/sonicberry/find
Search for specific patterns within opened pages or content.

Request Parameters:

Parameter	Type	Required	Description
requests	FindRequestItem[]	Yes	Array of find request items
toolState	ToolState	Yes	Session state with page references
FindRequestItem Parameters:

Parameter	Type	Required	Description
pattern	string	Yes	The pattern to search for in the content
pageContext	PageContextInfo	No	Page context specifying which page to search
queryType	string	No	Query type (default: "pattern")
refCursor	integer	No	Cursor to locate page index in page stack (default: 0)
skipCache	boolean	No	Skip fetch cache when finding (default: false)
lineNo	integer	No	Starting line number for search (default: 0)
numLines	integer	No	Number of lines to search (null = no limit)
Example Request:

{
  "requests": [
    {
      "pattern": "serverless",
      "pageContext": {
        "turn": 0,
        "action": "view",
        "id": 0
      }
    }
  ],
  "toolState": {
    "sessionId": "session-id-here"
  }
}
Example Response:

{
  "results": [
    {
      "template": "Azure Functions is a serverless solution...",
      "lineIdx": 5,
      "links": [
        {
          "linkId": 1,
          "url": "https://learn.microsoft.com/serverless",
          "name": "Serverless Guide"
        }
      ],
      "pageId": "turn0fetch0"
    }
  ],
  "toolState": {
    "sessionId": "session-id-here"
  }
}
Agent Browser Search API
For more complex search scenarios involving browser automation and interaction.

Enhanced Features:

Browser context management
Screenshot capabilities
Browser Search
POST /api/agent/browser/search
Note: Uses the same SearchRequest model as Core Search API above, with identical request and response structure.

Browser Find
POST /api/agent/browser/find
Note: Uses the same FindRequest model as Core Find API above, with identical request and response structure. Only adds optional auth parameter for authentication information.

Browser Open
POST /api/agent/browser/open
Note: Uses enhanced BrowserOpenRequestV2 model with additional browser context management capabilities.

Enhanced Browser Context Management:

Computer identifier for browser operations
Screenshot capture capabilities
User utterance tracking
Advanced browser automation features
Additional Parameters (extends Core Open API):

Parameter	Type	Required	Description
computerId	string	Yes	Computer identifier for browser operations
screenshot	boolean	No	Capture screenshot after opening (default: false)
utterance	string	No	User's utterance for the task
All OpenRequestItem parameters from Core Open API are inherited and available.

Example Request:

{
  "requests": [
    {
      "q": "Microsoft Azure Functions",
      "topn": 3,
      "source": "web_with_bing",
      "countryCode": "US",
      "language": "en"
    }
  ],
  "toolState": {
    "sessionId": "existing-session-id"
  }
}
Implementation Guide
SDK Setup and Initialization
To use Lumina search APIs, initialize the LuminaServiceApiProxy:

using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Settings;

// Configure API options
var options = new LuminaApiOptions
{
    Endpoint = "https://luminaexpenv.eastus2.cloudapp.azure.com",
    LuminaApiTokenProvider = () => Task.FromResult("your-access-token")
};

// Initialize the API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);
Search API Methods
The SDK provides two main search approaches:

Core Search API (simple search operations):

SearchAsync(SearchRequest) → /api/sonicberry/search
OpenAsync(OpenRequest) → /api/sonicberry/open
FindAsync(FindRequest) → /api/sonicberry/find
Browser Agent Search API (browser automation with computer context):

BrowserSearchAsync(SearchRequest) → /api/agent/browser/search
BrowserOpenAsync(BrowserOpenRequestV2) → /api/agent/browser/open
BrowserFindAsync(FindRequest) → /api/agent/browser/find
Authentication
The SDK automatically handles Bearer token authentication using your LuminaApiTokenProvider.

Search Provider Configuration
The search system supports multiple providers configured through the source parameter:

web_with_bing (default): Bing Web Search
Multiple providers can be specified with semicolon separation: web_with_bing;wikipedia
Error Handling
All endpoints return structured error responses for validation failures and service errors:

{
  "error": {
    "code": "InvalidRequest",
    "message": "Request must contain at least one item.",
    "details": [
      {
        "field": "requests",
        "message": "This field is required."
      }
    ]
  }
}
Code Examples
Basic Search Implementation
// Initialize API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);

// Create search request - uses Core API
var searchRequest = new SearchRequest
{
    Requests = new List<SearchRequestItem>
    {
        new SearchRequestItem
        {
            Q = "What is LLM?",
            TopN = 3,
            Source = "web_with_bing" // Optional, defaults to web_with_bing
        }
    }
};

// Execute search using Core API
var searchResponse = await apiProxy.SearchAsync(searchRequest);

// Process results
foreach (var result in searchResponse.Results)
{
    Console.WriteLine($"Title: {result.Title}");
    Console.WriteLine($"URL: {result.Url}");
    Console.WriteLine($"Content: {result.SemanticDocument}");
}

// Session ID available for follow-up operations
Console.WriteLine($"Session ID: {searchResponse.ToolState?.SessionId}");
Open and Find - Using RefId (Direct URL)
// Initialize API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);

// Open page using direct URL
var openRequest = new OpenRequest
{
    Requests = new List<OpenRequestItem>
    {
        new OpenRequestItem
        {
            RefId = "https://learn.microsoft.com/en-us/azure/azure-functions/"
        }
    }
};

var pageResponse = await apiProxy.OpenAsync(openRequest);

// Find content in the opened page
var findRequest = new FindRequest
{
    Requests = new List<FindRequestItem>
    {
        new FindRequestItem
        {
            Pattern = "serverless",
            PageContext = new PageContextInfo
            {
                Turn = 0,
                Action = "view",
                Id = 0
            }
        }
    },
    ToolState = pageResponse.ToolState
};

var findResponse = await apiProxy.FindAsync(findRequest);
Console.WriteLine($"Found {findResponse.Results?.Count ?? 0} matches for 'serverless'");
Open and Find - Using PageContext (Reference Previous Results)
// Initialize API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);

// First, perform a search to get pages in ToolState
var searchRequest = new SearchRequest
{
    Requests = new List<SearchRequestItem>
    {
        new SearchRequestItem
        {
            Q = "Azure Functions serverless",
            TopN = 3,
        }
    }
};

var searchResponse = await apiProxy.SearchAsync(searchRequest);

// Open a specific page from search results using PageContext
var openRequest = new OpenRequest
{
    Requests = new List<OpenRequestItem>
    {
        new OpenRequestItem
        {
            PageContext = new PageContextInfo
            {
                Turn = 0,        // Turn from the search operation
                Action = "search", // Previous action was search
                Id = 0           // First result from search
            }
        }
    },
    ToolState = searchResponse.ToolState
};

var pageResponse = await apiProxy.OpenAsync(openRequest);

// Find content within the opened page
var findRequest = new FindRequest
{
    Requests = new List<FindRequestItem>
    {
        new FindRequestItem
        {
            Pattern = "compute",
            PageContext = new PageContextInfo
            {
                Turn = 1,      // Turn from the open operation
                Action = "view", // Previous action was view/open
                Id = 0         // Reference to the opened page
            }
        }
    },
    ToolState = pageResponse.ToolState
};

var findResponse = await apiProxy.FindAsync(findRequest);
Console.WriteLine($"Found {findResponse.Results?.Count ?? 0} matches for 'compute'");
Agent Browser APIs - Enhanced Approach with Browser context
Browser Search
// Initialize API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);

// Browser search - enhanced with additional parameters
var searchRequest = new SearchRequest
{
    Requests = new List<SearchRequestItem>
    {
        new SearchRequestItem
        {
            Q = "Microsoft latest stock price",
            TopN = 3,
            Source = "web_with_bing",
            CountryCode = "US", // Browser API supports additional parameters
            Language = "en"
        }
    }
};

// Execute browser search using Browser Agent API
var searchResponse = await apiProxy.BrowserSearchAsync(searchRequest);
Console.WriteLine($"Session ID: {searchResponse.ToolState?.SessionId}");
Browser Open and Find
// Initialize API proxy
var apiProxy = new LuminaServiceApiProxy(options, httpClientFactory);

// Browser open - requires ComputerId and uses BrowserOpenRequestV2
var openRequest = new BrowserOpenRequestV2
{
    ComputerId = "comp-123", // Required for browser operations
    Screenshot = false, // Optional: capture screenshot after opening
    Requests = new List<OpenRequestItem>
    {
        new OpenRequestItem
        {
            RefId = "https://en.wikipedia.org/wiki/Large_language_model" // Can also use PageContext
        }
    }
};

var pageResponse = await apiProxy.BrowserOpenAsync(openRequest);

// Browser find - same FindRequest as core API
var findRequest = new FindRequest
{
    Requests = new List<FindRequestItem>
    {
        new FindRequestItem
        {
            Pattern = "language",
            QueryType = "pattern",
            SkipCache = true,
            PageContext = new PageContextInfo
            {
                Turn = 0,
                Action = "view",
                Id = 0
            }
        }
    },
    ToolState = pageResponse.ToolState
};

var findResponse = await apiProxy.BrowserFindAsync(findRequest);

// Process find results
foreach (var result in findResponse.Results)
{
    Console.WriteLine($"Found pattern at line {result.LineIdx}: {result.Template}");
}
Best Practices
Search Optimization
Specific queries: Use detailed, specific search terms for better results
Batch processing: Group related searches in a single request for efficiency
Result limiting: Use topn and max{..}Items in additionalConfigs to control result volume and response time
Recency filtering: Apply recency parameter for time-sensitive searches
Session Management
Maintain state: Use ToolState to preserve context across related operations
Session cleanup: Properly manage session lifecycle to prevent resource leaks
Error recovery: Implement robust error handling for session-related failures
Performance Considerations
Request batching: Combine multiple searches into batched requests when possible
Rate limiting: Respect API rate limits and implement proper retry mechanisms
Security
Input validation: Sanitize search queries to prevent injection attacks
Domain filtering: Use domain restrictions when appropriate for security
Content filtering: Apply appropriate content filters based on use case requirements