// <copyright file="LuminaApiController.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Mvc;
using Microsoft.Lumina.Client.ApiProxy;
using Microsoft.Lumina.Client.Models.Agent;
using Microsoft.Lumina.Client.Models.Settings;
using Microsoft.Lumina.Client.Models.Sonicberry;
using Newtonsoft.Json;
using static Microsoft.Lumina.Common.Constants.ConstantStrings;

namespace ApiProxyExample.Controllers;

/// <summary>
/// Controller for handling weather forecast requests.
/// </summary>
[ApiController]
[Route("[controller]")]
public class LuminaApiController : ControllerBase
{
    private readonly LuminaServiceApiProxy proxy;

    /// <summary>
    /// Initializes a new instance of the <see cref="LuminaApiController"/> class.
    /// </summary>
    /// <param name="luminaService"></param>
    public LuminaApiController(LuminaServiceApiProxy luminaService)
    {
        proxy = luminaService;
    }

    /// <summary>
    /// AgentTest
    /// </summary>
    [HttpPost("AgentTest")]
    public async Task AgentTest()
    {
        try
        {
            var computer = await proxy.InitializeAsync(new ComputerInitializeRequest() { ComputerId = "comp-123" } );
            Console.WriteLine($"Computer has been initialized, computer id :{computer?.Content?.ComputerId}");
            if (string.IsNullOrEmpty(computer?.Content?.ComputerId))
            {
                return;
            }
            var computerId = computer.Content.ComputerId;

            var state = await proxy.GetComputerAsync(new ComputerGetRequest()
            {
                ComputerId = computerId
            });
            Console.WriteLine($"Computer state: {JsonConvert.SerializeObject(state.Content)}");

            var actionRes = await proxy.DoAsync(new ComputerDoRequest()
            {
                ComputerId = computerId,
                Actions = new List<ComputerAction>()
                {
                    new ComputerAction
                    {
                        Action = "click",
                        X = 100,
                        Y = 100,
                    }
                }
            });
            Console.WriteLine($"Action response: {JsonConvert.SerializeObject(actionRes.Content)}");

            var keysActions = await proxy.DoAsync(new ComputerDoRequest()
            {
                ComputerId = computerId,
                Actions = new List<ComputerAction>()
                {
                    new ComputerAction
                    {
                        Action = "multi_key_press",
                        Keys = new List<string> { "17", "A" },
                    }
                }
            });

            Console.WriteLine($"Action response: {JsonConvert.SerializeObject(keysActions.Content)}");

            var execRes = await proxy.ExecAsync(new ContainerExecRequest
            {
                ComputerId = computerId,
                Cmd = new List<string>
                {
                    "echo hello world"
                },
            });
            Console.WriteLine($"Exec response: {JsonConvert.SerializeObject(execRes.Content)}");

            var syncRes = await proxy.SyncFileAsync(new ComputerSyncFileRequest()
            {
                FilePath = "C:\\test.txt"
            });
            Console.WriteLine($"Sync response: {JsonConvert.SerializeObject(syncRes.Content)}");

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
                }
            };
            var computerSearch = await proxy.BrowserSearchAsync(searchRequest);
            var pageContext = computerSearch.PageContext;
            Console.WriteLine($"Search response: {JsonConvert.SerializeObject(computerSearch)}");

            var openRequest = new List<OpenRequestItem>()
            {
                new OpenRequestItem()
                {
                    RefId = "http://www.openai.com"
                }
            };
            var openRes = await proxy.BrowserOpenAsync(new BrowserOpenRequestV2
            {
                ComputerId = computerId,
                Requests = openRequest,
            });
            Console.WriteLine($"Open response: {JsonConvert.SerializeObject(openRes.Pages)}");

            var findRequest = new FindRequest()
            {
                Requests = new List<FindRequestItem>()
                {
                    new FindRequestItem()
                    {
                        Pattern = "Azure Functions",
                        PageContext = pageContext
                    }
                }
            };
            var pageRes = await proxy.BrowserFindAsync(findRequest);
            Console.WriteLine($"Find response: {JsonConvert.SerializeObject(pageRes)}");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// BrowserTest
    /// </summary>
    [HttpPost("BrowserTest")]
    public async Task BrowserTest()
    {
        try
        {
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
                }
            };
            var computerSearch = await proxy.SearchAsync(searchRequest);
            var pageContext = computerSearch.PageContext;
            Console.WriteLine($"Search response: {JsonConvert.SerializeObject(computerSearch)}");

            var openRequest = new List<OpenRequestItem>()
            {
                new OpenRequestItem()
                {
                    RefId = "http://www.openai.com"
                }
            };
            var openRes = await proxy.OpenAsync(new OpenRequest()
            {
                Requests = openRequest,
            });
            Console.WriteLine($"Open response: {JsonConvert.SerializeObject(openRes.Pages)}");

            var findRequest = new FindRequest()
            {
                Requests = new List<FindRequestItem>()
                {
                    new FindRequestItem()
                    {
                        Pattern = "Azure Functions",
                        PageContext = pageContext
                    }
                }
            };
            var findRes = await proxy.FindAsync(findRequest);
            Console.WriteLine($"Find response: {JsonConvert.SerializeObject(findRes)}");

            var clickRequest = new ClickRequest()
            {
                Requests = new List<ClickRequestItem>()
                {
                    new ClickRequestItem()
                    {
                        PageContext = pageContext
                    }
                }
            };
            var clickRes = await proxy.ClickAsync(clickRequest);
            Console.WriteLine($"Find response: {JsonConvert.SerializeObject(clickRes)}");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// SettingsTest - Tests the InitUrlACAsync method for URL access control
    /// </summary>
    [HttpPost("SettingsTest")]
    public async Task SettingsTest()
    {
        try
        {
            var initUrlACRequest = new InitUrlACRequest
            {
                TenantId = "test-tenant-123",
                UserId = "test-user-456",
                ComputerId = "test-computer-789",
                AllowList = new List<string>
                {
                    "microsoft.com",
                    "github.com",
                    "stackoverflow.com"
                },
                DenyList = new List<string>
                {
                    "malicious-site.com",
                    "blocked-domain.net"
                }
            };

            var response = await proxy.InitUrlACAsync(initUrlACRequest);
            
            Console.WriteLine($"InitUrlAC response: Success={response.Success}, Message={response.Message}");
            Console.WriteLine($"Full response: {JsonConvert.SerializeObject(response)}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error in SettingsTest: {e.Message}");
            Console.WriteLine($"Full exception: {e}");
            throw;
        }
    }
}