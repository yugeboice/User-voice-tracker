Sample for CUA tools: test a website
This page demonstrates how to leverage Lumina CUA tools to test a website, such as navigation and functions. We will use va.gov in this sample.

Initialize a computer
To leverage CUA tools, the first thing you need to do is initializing a virtual computer. You can initialize one with API /api/agent/computer/initialize and below is an example:

computer_id = uuid.uuid4().hex
body = {
    "computerId": computer_id,
    "userId": "{user-id}",
    "tenantId": "{tenant-id}",
}
response = client.post(
    api_path="/api/agent/computer/initialize",
    data=body,
)
response.raise_for_status()
computerId can be either a GUID or meaningful string to identify the computer. For userId and tenantId, Lumina service will try to parse it from token; currently for your test, you can manually pass your user id and MSIT tenant 72f988bf-86f1-41af-91ab-2d7cd011db47.

Get computer screenshot
Now you have an exclusive computer that serves for you, but what is its current status? You need API /api/agent/computer/get to understand that:

body = {
    "computerId": computer_id,
}
response = client.post(
    api_path="/api/agent/computer/get",
    data=body,
)
response.raise_for_status()
The response looks like below:

{
    "messageId": "3c594909-6eda-4eb8-984c-89ec44e69499",
    "createTime": 1758095252986,
    "status": "finished_successfully",
    "content": {
        "screenshot": "{base64-encoded-image-string}",
        "width": 1024,
        "height": 768,
        "success": true
    },
    "metadata": {
        "wait_for_resources": false,
        "delay": 0
    }
}
A sample Python function that parses the Base64-encoded image string to a PNG file can help us visualize the computer state. If you are working with a system that orchestrated by LLM, you may directly leverage its vision capability to understand the state.

def get_screenshot_and_visualize(response: requests.Response, filename: str) -> None:
    response_json = response.json()
    screenshot = response_json["content"]["screenshot"]
    image_bytes = base64.b64decode(screenshot)
    with open(filename, mode="wb") as f:
        f.write(image_bytes)
You shall get screenshot similar to below:

initial-screenshot

Operate
To perform operations in the computer, API /api/agent/computer/do is the choice.

Navigate to the website
Back to our ultimate goal in this page, we need to firstly navigate to the website va.gov. This operations can be splitted into several actions:

Focus the cursor on the address bar, which can be achieved with Ctrl+L
Type va.gov and press enter
Wait for the page loaded
According to the API reference, these actions can be translated to below request:

body = {
    "computerId": computer_id,
    "actions": [
        {
            "action": "keypress",
            "keys": [
                "ctrl",
                "l"
            ]
        },
        {
            "action": "type",
            "text": "va.gov",
        },
        {
            "action": "keypress",
            "keys": [
                "enter"
            ]
        },
        {
            "action": "wait"
        },
    ],
}
response = client.post(
    api_path="/api/agent/computer/do",
    data=body,
)
response.raise_for_status()
Then we should be able to see screenshot similar to below:

navigate-to-website

Test search function
Everything looks fine from the screenshot, then let's test the search function in this website. Similar to navigating, there needs some actions to achieve this operation, and here is one solution:

body = {
    "actions": [
        {
            "action": "click",
            "x": 270,
            "y": 652,
            "button": 1
        },
        {
            "action": "type",
            "text": "benefits"
        },
        {
            "action": "keypress",
            "keys": [
                "enter"
            ]
        },
        {
            "action": "wait"
        }
    ],
    "actionDelayMs": "800",
    "computerId": computer_id,
}
response = client.post(
    api_path="/api/agent/computer/do",
    data=body,
)
response.raise_for_status()
And the screenshot:

test-search

Then let's click the first search result, which requires a precise coordinates to click it:

body = {
    "actions": [
        {
            "action": "click",
            "x": 430,
            "y": 148,
            "button": 1
        },
        {
            "action": "wait"
        }
    ],
    "actionDelayMs": "1000",
    "computerId": computer_id,
}
response = client.post(
    api_path="/api/agent/computer/do",
    data=body,
)
response.raise_for_status()
click-search-result

Great! Seems that the search function works as expected in va.gov.

Release computer
Once the task is finished, don't forget to release your virtual computer via API /api/agent/computer/release.

body = {
    "computerId": computer_id,
}
response = client.post(
    api_path="/api/agent/computer/release",
    data=body,
)
response.raise_for_status()