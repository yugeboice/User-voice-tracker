// Search.js - Search functionality and loading states
// Handles search form submission and loading UI

// Load company information using Find API independently
function loadCompanyInfo(companyName) {
    if (!companyName) {
        console.error('Company name is required');
        return;
    }
    
    // Show loading state in Company Overview card
    const companyCard = document.getElementById('companyOverviewCard');
    if (!companyCard) {
        console.error('Company overview card not found');
        return;
    }
    
    companyCard.innerHTML = `
        <div class="loading-spinner-container">
            <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Loading...</span>
            </div>
            <p>Extracting company info using <strong>Find API</strong>...</p>
        </div>
    `;
    
    // Call backend to get Find API data
    fetch('/Home/FindCompanyInfo', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify({ companyName: companyName })
    })
    .then(response => response.json())
    .then(data => {
        if (data.success && data.companyInfo) {
            // Build table HTML
            let tableHtml = '<table class="table table-sm table-borderless mb-0">';
            for (const field of data.companyInfo.fields) {
                tableHtml += `
                    <tr>
                        <td class="text-end" style="width: 150px; font-weight: 600; color: #495057;">${escapeHtml(field.fieldName)}</td>
                        <td style="color: #212529;">${escapeHtml(field.content)}</td>
                    </tr>
                `;
            }
            tableHtml += '</table>';
            tableHtml += `<hr class="mt-3 mb-2">`;
            tableHtml += `<small class="text-muted"><a href="${escapeHtml(data.companyInfo.url)}" target="_blank">View full article on Wikipedia →</a></small>`;
            
            companyCard.innerHTML = tableHtml;
        } else {
            // Show error with retry button
            companyCard.innerHTML = `
                <div class="alert alert-warning mb-0">
                    <p class="mb-2">⚠️ ${data.error || 'No company information available'}</p>
                    <button type="button" class="btn btn-sm btn-outline-primary" onclick="loadCompanyInfo('${companyName.replace(/'/g, "\\'")}')">
                        🔄 Retry
                    </button>
                </div>
            `;
        }
        
        // Refresh logs
        if (data.logsUpdated) {
            refreshLogs();
        }
    })
    .catch(error => {
        console.error('Load company info error:', error);
        companyCard.innerHTML = `
            <div class="alert alert-danger mb-0">
                <p class="mb-2">❌ Error: ${error.message}</p>
                <button type="button" class="btn btn-sm btn-outline-primary" onclick="loadCompanyInfo('${companyName.replace(/'/g, "\\'")}')">
                    🔄 Retry
                </button>
            </div>
        `;
    });
}

// Retry search - resubmit the search form with loading state
function retrySearch() {
    const searchForm = document.querySelector('form[action="/Home/Index"]');
    if (searchForm) {
        // Trigger the form submit event
        const event = new Event('submit', { bubbles: true, cancelable: true });
        handleSearchSubmit(event);
        searchForm.submit();
    } else {
        // Fallback: reload page
        window.location.reload();
    }
}

// Handle search form submit with loading state
function handleSearchSubmit(event) {
    const searchButton = document.getElementById('searchButton');
    const searchButtonText = document.getElementById('searchButtonText');
    const searchButtonLoading = document.getElementById('searchButtonLoading');
    const queryInput = document.getElementById('query');
    
    // Check if input is empty
    if (!queryInput.value.trim()) {
        return true; // Let browser's native validation handle it
    }
    
    // Show loading state
    searchButton.disabled = true;
    searchButtonText.style.display = 'none';
    searchButtonLoading.style.display = 'inline-block';
    queryInput.readOnly = true; // Use readOnly instead of disabled to preserve form data
    
    // Show loading placeholders
    showLoadingState();
    
    // Form will submit normally, page will reload with results
    // Auto-refresh will be started after page reload (see Index.cshtml)
    return true;
}

// Show loading skeleton for search results
function showLoadingState() {
    // Create loading placeholder HTML
    const loadingHTML = `
        <div id="loadingPlaceholder" class="mt-4">
            <h3>📊 Searching...</h3>
            
            <!-- Two Column Layout: Company Overview + CUA Loading -->
            <div class="row mb-4">
                <!-- Left Column: Company Overview Loading -->
                <div class="col-md-6">
                    <div class="card h-100" style="border-left: 4px solid #0d6efd;">
                        <div class="card-header" style="background-color: #f8f9fa;">
                            <h5 class="mb-0">🏢 Company Overview</h5>
                            <small class="text-muted">Loading company information using <strong>Find API</strong>...</small>
                        </div>
                        <div class="card-body">
                            <div class="loading-spinner-container">
                                <div class="spinner-border text-primary" role="status">
                                    <span class="visually-hidden">Loading...</span>
                                </div>
                                <p>Extracting company info using <strong>Find API</strong>...</p>
                            </div>
                        </div>
                    </div>
                </div>
                
                <!-- Right Column: CUA Test Loading -->
                <div class="col-md-6">
                    <div class="card h-100" style="border-left: 4px solid #17a2b8;">
                        <div class="card-header" style="background-color: #f8f9fa;">
                            <h5 class="mb-1">🤖 Stock Price Screenshot</h5>
                            <small class="text-muted">Ready for screenshot</small>
                        </div>
                        <div class="card-body">
                            <div class="text-center">
                                <button type="button" class="btn btn-info" disabled>
                                    ▶️ Get Screenshot
                                </button>
                                <p class="text-muted mt-3">Will be available after search completes</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
            
            <div class="row">
                <!-- Stock Results Loading -->
                <div class="col-md-6">
                    <h4 class="text-primary">📈 Stock & Market Data</h4>
                    <p class="text-muted">Loading results...</p>
                    
                    <div class="loading-card">
                        <div class="skeleton skeleton-title"></div>
                        <div class="skeleton skeleton-text" style="width: 60%;"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                    </div>
                    <div class="loading-card">
                        <div class="skeleton skeleton-title"></div>
                        <div class="skeleton skeleton-text" style="width: 60%;"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                    </div>
                </div>
                
                <!-- News Results Loading -->
                <div class="col-md-6">
                    <h4 class="text-success">📰 Recent News</h4>
                    <p class="text-muted">Loading articles...</p>
                    
                    <div class="loading-card">
                        <div class="skeleton skeleton-title"></div>
                        <div class="skeleton skeleton-text" style="width: 60%;"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                    </div>
                    <div class="loading-card">
                        <div class="skeleton skeleton-title"></div>
                        <div class="skeleton skeleton-text" style="width: 60%;"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                        <div class="skeleton skeleton-line"></div>
                    </div>
                </div>
            </div>
        </div>
    `;
    
    // Insert loading placeholder after search form
    const searchContainer = document.querySelector('.main-search-container');
    searchContainer.insertAdjacentHTML('afterend', loadingHTML);
}
