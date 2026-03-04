# Style System Tests

Testing tools for the 8-domain intelligent style detection system.

## Files

- **test_style_system.py** (209 lines) - Python API integration tests
  - Creates test notebooks via HTTP API
  - Tests domain detection across 8 domains (business, science, technology, history, nature, lifestyle, social, family)
  - Validates keyword-based style variant selection

- **test_style.bat** (30 lines) - Windows batch script test
  - Quick smoke test for style system
  - Creates business-themed test notebook
  - Triggers infographic generation

- **style-test.html** (224 lines) - Interactive web UI test
  - Browser-based test interface
  - Test cases for each of the 8 domains
  - Visual verification of style selection

## Usage

### Prerequisites
1. Start the API server:
   ```powershell
   dotnet run
   ```
   Server should be running on http://localhost:8400

2. Ensure egress-llm service is running on port 4141

### Running Tests

**Python Integration Tests:**
```powershell
cd tests/style-system
python test_style_system.py
```

**Batch Script Test:**
```powershell
tests\style-system\test_style.bat
```

**Web UI Test:**
Open `tests/style-system/style-test.html` in your browser

## Test Coverage

The style system intelligently selects from:
- **8 Domains**: Business, History, Science, Nature/Space, Technology, Lifestyle, Social, Family
- **27 Style Variants**: Each domain has 3-4 specialized variants
- **Priority-based Detection**: Keywords are weighted to handle ambiguous content

### Domain Examples

| Domain | Keywords | Expected Variants |
|--------|----------|-------------------|
| Business | strategy, market, corporate | Modern Minimalist, Data-Driven Executive, Strategic Planning |
| Science | research, experiment, theory | Scientific Method, Lab Research, Academic Publication |
| Technology | AI, software, coding | Tech Innovation, Digital Blueprint, AI/ML Focus |
| History | WWII, timeline, ancient | Timeline Classic, Documentary Style, Heritage Archive |

## Expected Results

All tests should:
- ✅ Successfully detect the correct domain from content keywords
- ✅ Select appropriate style variant within the domain
- ✅ Generate infographic with consistent visual styling
- ✅ Return structured generation metadata

## Troubleshooting

**Test fails with connection error:**
- Verify API server is running: `curl http://localhost:8400/api/notebooks`
- Check egress-llm status: `curl http://localhost:4141/health`

**Style detection seems incorrect:**
- Check keywords in test content match domain definitions in `style-config.json`
- Verify priority weights are configured correctly
- Review anti-keywords that might override detection

**Generated images don't match expected style:**
- Confirm egress-llm is using correct model (Claude Sonnet 4)
- Check prompt construction in `StudioApi.cs` or `ImageGenerationService.cs`
- Verify style variant configuration in `style-config.json`
