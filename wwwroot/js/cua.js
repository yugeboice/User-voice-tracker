// CUA.js - Computer Use Agent (CUA) functionality
// Handles MSN Money stock price screenshot using CUA API with Server-Sent Events

// MSN Money Search - All-in-One with Real-time SSE Streaming
function cuaMsnMoney(companyName) {
    const btn = document.getElementById('cuaMsnBtn');
    const btnText = document.getElementById('cuaMsnText');
    const btnLoading = document.getElementById('cuaMsnLoading');
    const statusDiv = document.getElementById('cuaStatus');
    const statusText = document.getElementById('cuaStatusText');
    
    btn.disabled = true;
    btnText.style.display = 'none';
    btnLoading.style.display = 'inline-block';
    
    // Show status area
    statusDiv.style.display = 'block';
    statusDiv.className = 'alert alert-info';
    document.getElementById('cuaScreenshot').style.display = 'none';
    
    // Helper function to update status (replaces content, not append)
    function updateStatus(message) {
        statusText.textContent = message;
    }
    
    try {
        console.log('MSN Money: Connecting to SSE stream for:', companyName);
        
        // Create EventSource for Server-Sent Events
        const eventSource = new EventSource('/Home/TestCuaMsnMoneyStream?companyName=' + encodeURIComponent(companyName));
        
        // Handle progress updates
        eventSource.addEventListener('progress', function(e) {
            console.log('Progress:', e.data);
            updateStatus(e.data);
        });
        
        // Handle screenshot data
        eventSource.addEventListener('screenshot', function(e) {
            console.log('Screenshot received');
            try {
                const screenshotData = JSON.parse(e.data);
                document.getElementById('cuaImage').src = screenshotData.image;
                document.getElementById('cuaScreenshot').style.display = 'block';
            } catch (error) {
                console.error('Failed to parse screenshot data:', error);
            }
        });
        
        // Handle completion
        eventSource.addEventListener('complete', function(e) {
            console.log('Complete:', e.data);
            updateStatus(e.data);
            
            // Hide progress after a short delay
            setTimeout(() => {
                statusDiv.style.display = 'none';
            }, 1000);
            
            // Close the connection
            eventSource.close();
            
            // Re-enable button
            btn.disabled = false;
            btnText.style.display = 'inline';
            btnLoading.style.display = 'none';
            
            // Refresh main logs
            refreshLogs();
        });
        
        // Handle errors
        eventSource.addEventListener('error', function(e) {
            console.error('SSE Error:', e);
            
            statusDiv.className = 'alert alert-danger';
            if (e.data) {
                statusText.innerHTML = '❌ Error: ' + e.data + '<br><button type="button" class="btn btn-sm btn-outline-primary mt-2" onclick="retryMsnMoney(\'' + companyName + '\')">🔄 Retry</button>';
            } else {
                statusText.innerHTML = '❌ Connection error. Please try again.<br><button type="button" class="btn btn-sm btn-outline-primary mt-2" onclick="retryMsnMoney(\'' + companyName + '\')">🔄 Retry</button>';
            }
            
            eventSource.close();
            
            // Re-enable button
            btn.disabled = false;
            btnText.style.display = 'inline';
            btnLoading.style.display = 'none';
        });
        
        // Handle connection errors
        eventSource.onerror = function(error) {
            console.error('EventSource failed:', error);
            
            if (eventSource.readyState === EventSource.CLOSED) {
                console.log('EventSource connection closed');
            } else {
                statusDiv.className = 'alert alert-danger';
                statusText.innerHTML = '❌ Connection failed. Please try again.<br><button type="button" class="btn btn-sm btn-outline-primary mt-2" onclick="retryMsnMoney(\'' + companyName + '\')">🔄 Retry</button>';
                
                eventSource.close();
                
                // Re-enable button
                btn.disabled = false;
                btnText.style.display = 'inline';
                btnLoading.style.display = 'none';
            }
        };
        
    } catch (error) {
        console.error('MSN Money error:', error);
        statusDiv.className = 'alert alert-danger';
        statusText.innerHTML = '❌ Error: ' + error.message + '<br><button type="button" class="btn btn-sm btn-outline-primary mt-2" onclick="retryMsnMoney(\'' + companyName + '\')">🔄 Retry</button>';
        
        // Re-enable button
        btn.disabled = false;
        btnText.style.display = 'inline';
        btnLoading.style.display = 'none';
    }
}

// Retry function that shows loading state before calling cuaMsnMoney
function retryMsnMoney(companyName) {
    const statusDiv = document.getElementById('cuaStatus');
    const statusText = document.getElementById('cuaStatusText');
    
    // Show loading state immediately
    statusDiv.className = 'alert alert-info';
    statusText.textContent = '🔄 Retrying...';
    
    // Call the main function after a short delay
    setTimeout(() => {
        cuaMsnMoney(companyName);
    }, 100);
}

