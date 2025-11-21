// Utils.js - Utility functions for UI and content formatting
// Shared helper functions used across the application

// Toggle API logs panel
function toggleLogs() {
    const logsBody = document.getElementById('apiLogsBody');
    const toggleIcon = document.getElementById('logsToggleIcon');
    
    if (logsBody.style.display === 'none') {
        logsBody.style.display = 'block';
        toggleIcon.textContent = '🔽';
    } else {
        logsBody.style.display = 'none';
        toggleIcon.textContent = '▶️';
    }
}

// Refresh API logs from server
function refreshLogs() {
    // Fetch updated logs from server
    fetch('/Home/GetLogs')
        .then(response => response.json())
        .then(data => {
            if (data.success && data.logsHtml) {
                const logsContainer = document.querySelector('.api-logs');
                if (logsContainer) {
                    logsContainer.outerHTML = data.logsHtml;
                }
            }
        })
        .catch(error => console.error('Failed to refresh logs:', error));
}

// Format content for display
function formatContent(content) {
    // Basic formatting for better readability
    let formatted = content
        .replace(/\n/g, '<br>')
        .replace(/\t/g, '&nbsp;&nbsp;&nbsp;&nbsp;')
        .replace(/(https?:\/\/[^\s]+)/g, '<a href="$1" target="_blank">$1</a>');
    
    return '<div class="content-display">' + formatted + '</div>';
}

// Escape HTML to prevent XSS
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
