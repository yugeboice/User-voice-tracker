# Lumina API Demo Console

An educational sample application designed to help developers quickly learn and test Lumina API features. 

## What This Demo Does

This demo uses **company name search** as a practical, end-to-end example. Search for a company (e.g., "Microsoft", "Apple", "Tesla") and see how different Lumina APIs work together:

- **Search API** returns comprehensive results: stock information, news articles, and web pages
- **Open API** opens the company's website and extracts navigable links
- **Find API** extracts structured information (founding date, headquarters, revenue) from Wikipedia
- **Computer Use Agent API** automatically captures stock price chart screenshots

The web interface provides real-time logging so you can see the complete request/response flow for each API call.

## Features

### 📊 Stock Market Data & Recent News
**What it does**: Performs a batch search to retrieve stock-related information and recent news articles about a company in a single API call.

**APIs used**:
- **Search API (Batch)**: Sends two queries simultaneously:
  - Query 1: `"{company name} stock price"` - Returns stock market data and financial pages
  - Query 2: `"{company name} latest news"` - Returns recent news articles (past 7 days)
- Both queries use `WebWithBing` source with `Recency=7` filter for fresh results

**How to test**: Enter a company name (e.g., "Microsoft") and click "Search" button

### 📋 Company Overview
**What it does**: Extracts structured company information (founding date, headquarters, revenue, etc.) from the company's Wikipedia page.

**APIs used** (3-step workflow):
1. **Search API**: Searches for the company name with `domains: ["wikipedia.org"]` filter to restrict results to Wikipedia only
2. **Open API**: Opens the Wikipedia page and retrieves its content with a session ID
3. **Find API**: Searches within the page content for specific patterns like "Founded", "Headquarters", "Revenue" and extracts the information

**How to test**: Enter a company name and click "Search" - company overview will be automatically loaded and displayed in the left card

### 📈 Stock Price Screenshot
**What it does**: Automatically navigates to MSN Money, searches for the company, and captures a screenshot of the stock price chart.

**APIs used**:
- **Computer Use Agent (CUA) API - Initialize**: Creates a virtual computer session with unique computer ID
- **CUA API - Actions** (streaming with real-time progress updates):
  1. Navigate to MSN Money: Press `Ctrl+L` → Type URL → Press `Enter` → Wait
  2. Click search box at coordinates (1203, 43)
  3. Type the company name
  4. Press `Enter` to search
  5. Wait for search results page to load
- **CUA API - Screenshot**: Captures the final page showing stock chart and company information

**How to test**: After searching for a company, click "Stock Price Screenshot" button

## Getting Started

### Prerequisites

- **.NET 8.0 SDK** or higher
- **Azure AD App Registration** (see configuration steps below)
- **Lumina API Access**

### Configuration Steps

#### 1. Clone the Repository

```bash
git clone <your-repository-url>
cd lumina-test
```

#### 2. Register an Azure AD Application

You need to register an application in the [Azure Portal](https://portal.azure.com):

1. Navigate to **Azure Active Directory** → **App registrations** → **New registration**
2. Enter an application name (e.g., "Lumina API Demo")
3. Select account type: **Accounts in this organizational directory only**
4. Set Redirect URI:
   - Platform type: **Web**
   - URI: `http://localhost:8401`
5. After registration, record:
   - **Tenant ID** (Directory (tenant) ID)
   - **Client ID** (Application (client) ID)

#### 3. Configure API Permissions

In your Azure AD application:

1. Go to **API permissions** → **Add a permission**
2. Select **APIs my organization uses**
3. Search for and add Lumina API permissions
4. Grant necessary scopes

#### 4. Create Configuration File

```bash
cd LuminaSearchConsole
cp appsettings.Template.json appsettings.json
```

Edit `appsettings.json` with your configuration:

```json
{
  "AzureAd": {
    "TenantId": "YOUR-TENANT-ID",
    "ClientId": "YOUR-CLIENT-ID",
    "RedirectUri": "http://localhost:8401"
  }
}
```

> **Note**: `appsettings.json` is excluded in `.gitignore` and will not be committed to Git. Keep your credentials secure.

#### 5. Run the Application

```bash
# Restore NuGet packages
dotnet restore

# Build and run
dotnet run
```

The application will start at **http://localhost:8400**.

### Usage Guide

1. **Login**: First visit will redirect to Microsoft login page
2. **Search Testing**: 
   - Use example buttons (Microsoft, Apple, Tesla...) for quick start
   - Or enter your own search keywords
   - View real-time logs on the right to understand API call flow
3. **Explore Other Features**:
   - Click **"Open Content"** button in search results to test Open API
   - Click **"Find Company Info"** button to test Find API
   - Click **"Stock Price Screenshot"** button to test CUA API

## Code Structure

```
lumina-test/
├── LuminaSearchConsole/           # Main application
│   ├── Controllers/               # MVC controllers
│   │   └── HomeController.cs      # Main controller handling all page requests
│   │
│   ├── Services/                  # Lumina API usage examples (core learning content)
│   │   ├── LuminaSearchService.cs    # Search API demo
│   │   ├── LuminaOpenService.cs      # Open & Click API demo
│   │   ├── LuminaFindService.cs      # Find API demo
│   │   ├── LuminaCuaService.cs       # Computer Use Agent API demo
│   │   ├── OboTokenService.cs        # MSAL authentication
│   │   ├── ApiLogService.cs          # Logging service for UI
│   │   └── SharedModels.cs           # Internal helper models
│   │
│   ├── Models/                    # View models and business models
│   │   └── ViewModels.cs          # UI data models
│   │
│   ├── Views/                     # Razor view pages
│   │   ├── Home/
│   │   │   └── Index.cshtml       # Main page
│   │   └── Shared/
│   │       ├── _Layout.cshtml     # Layout template
│   │       └── Error.cshtml       # Error page
│   │
│   ├── appsettings.Template.json  # Configuration template (committed to Git)
│   ├── appsettings.json           # Your actual config (not committed)
│   ├── AppConfiguration.cs        # Configuration class definitions
│   └── Program.cs                 # Application entry point
│
├── CUA.md                         # Computer Use Agent API documentation
├── SearchAPIs.md                  # Search API documentation
└── README.md                      # This file
```

## Learning Each API

Each Service file contains detailed teaching comments:

### LuminaSearchService.cs
- **Purpose**: Demonstrates Search API batch and simple search
- **Key Methods**: 
  - `BatchSearchAsync()` - Batch search across multiple content types
  - `SimpleSearchAsync()` - Single content type search
- **How to Test**: Enter keywords on the main page, click "Search (Batch)" or "Search (Simple)"

### LuminaOpenService.cs
- **Purpose**: Demonstrates how to open web pages and extract links
- **Key Methods**: 
  - `OpenContentWithLinksAsync()` - Open URL and return content
  - `ClickLinkAsync()` - Navigate by clicking links
- **How to Test**: After searching, click "Open Content", then click any link on the page

### LuminaFindService.cs
- **Purpose**: Demonstrates how to find specific content within web pages
- **Key Methods**: 
  - `FindContentAsync()` - Use Find API to locate content
  - `ExtractCompanyInfoAsync()` - Extract company info (Wikipedia example)
- **How to Test**: After searching for a company, click "Find Company Info"

### LuminaCuaService.cs
- **Purpose**: Demonstrates Computer Use Agent automation capabilities
- **Key Methods**: 
  - `GetStockPriceScreenshotAsync()` - Capture stock price chart screenshot
- **How to Test**: After searching for a company, click "Stock Price Screenshot"

### OboTokenService.cs
- **Purpose**: Handles On-Behalf-Of (OBO) authentication
- **Key Methods**: 
  - `GetOboTokenAsync()` - Get access token for Lumina API
- **Features**: Automatic token caching and refresh on expiration

## Troubleshooting

### Q: Why ports 8400 and 8401?
A: 8400 is the application port, 8401 is the Azure AD callback port. To change, update both `appsettings.json` and the Redirect URI in your Azure AD app registration.

### Q: What if my token expires?
A: Click the **Sign Out** button in the top-right corner, then log in again. The token cache will be automatically cleared.

### Q: Build fails?
A: 
1. Check .NET SDK version: `dotnet --version` (requires 8.0+)
2. Clean and rebuild: `dotnet clean && dotnet build`
3. Delete `bin/` and `obj/` folders and retry

### Q: Getting 401 Unauthorized error?
A: 
1. Verify TenantId and ClientId in `appsettings.json` are correct
2. Confirm your Azure AD app has been granted Lumina API permissions
3. Try signing out and signing in again

### Q: Search returns no results?
A: Check that `LuminaConfiguration.ApiEndpoint` is correct and you have access to that environment.

## Logging and Debugging

### View Real-time Logs
The right side of the application displays detailed logs for all API calls, including:
- Request URL and parameters
- Response status code
- Response content (formatted JSON)
- Error messages (if any)

### Enable Verbose Logging
Modify `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

---

**Tip**: This program is designed to help you quickly understand how to use Lumina APIs. We recommend:
1. Run the program first and try out all features
2. Read the corresponding Service code to understand API call details
3. Reference the code examples when integrating Lumina APIs into your own projects
