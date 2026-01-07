// Navigation.js - Content navigation and breadcrumb management
// Handles Open API and Click API interactions for deep content navigation

// Global state for navigation
var currentSession = {
    sessionId: null,
    pageContext: null,
    breadcrumb: [],
    history: []  // Store full state for each navigation step
};

// Open full content using Open API
function openFullContent(url, title) {
    // Reset session for new content
    currentSession = {
        sessionId: null,
        pageContext: null,
        breadcrumb: [title],
        history: []
    };
    
    // Show modal and loading
    const modal = new bootstrap.Modal(document.getElementById('contentModal'));
    document.getElementById('contentModalLabel').textContent = '📄 ' + title;
    document.getElementById('contentLoading').style.display = 'block';
    document.getElementById('contentBody').innerHTML = '';
    document.getElementById('contentLinks').style.display = 'none';
    document.getElementById('breadcrumbNav').style.display = 'none';
    modal.show();
    
    // Make AJAX request to open content
    fetch('/Home/OpenContent', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify({ url: url, sessionId: currentSession.sessionId })
    })
    .then(response => response.json())
    .then(data => {
        document.getElementById('contentLoading').style.display = 'none';
        
        if (data.success) {
            // Save session info
            currentSession.sessionId = data.sessionId;
            currentSession.pageContext = data.pageContext;
            
            // Save to history
            currentSession.history = [{
                title: title,
                content: data.content,
                links: data.links,
                pageContext: data.pageContext
            }];
            
            // Format and display content
            const formattedContent = formatContent(data.content);
            document.getElementById('contentBody').innerHTML = formattedContent;
            
            // Display related links if available
            displayRelatedLinks(data.links);
            
            // Update breadcrumb
            updateBreadcrumb();
        } else {
            document.getElementById('contentBody').innerHTML = 
                '<div class="alert alert-danger">Error: ' + data.error + '</div>';
        }
        
        // Refresh logs to show the Open API call
        if (data.logsUpdated) {
            refreshLogs();
        }
    })
    .catch(error => {
        document.getElementById('contentLoading').style.display = 'none';
        document.getElementById('contentBody').innerHTML = 
            '<div class="alert alert-danger">Error loading content: ' + error.message + '</div>';
    });
}

// Display related links in the modal
function displayRelatedLinks(links) {
    const linksContainer = document.getElementById('contentLinks');
    const linksList = document.getElementById('linksList');
    
    if (!links || links.length === 0) {
        linksContainer.style.display = 'none';
        return;
    }
    
    // Filter out navigation/internal links
    const contentLinks = links.filter(function(link) {
        return link.name && 
               !link.name.startsWith('[[[') && 
               link.name.indexOf('Skip to') === -1 &&
               link.name.indexOf('Table of contents') === -1 &&
               link.name.length > 2;
    });
    
    if (contentLinks.length === 0) {
        linksContainer.style.display = 'none';
        return;
    }
    
    // Build links HTML
    let linksHtml = '';
    for (let i = 0; i < Math.min(contentLinks.length, 10); i++) {
        const link = contentLinks[i];
        const escapedName = escapeHtml(link.name).replace(/'/g, '&#39;');
        const btnStart = '<button class="list-group-item list-group-item-action d-flex justify-content-between align-items-center" ';
        const btnClick = 'onclick="clickRelatedLink(\'' + link.linkId + '\', \'' + escapedName + '\')">';
        const btnContent = '<div' + '><i class="bi bi-link-45deg"><' + '/i> <strong' + '>' + escapeHtml(link.name) + '<' + '/strong><' + '/div>';
        const btnBadge = '<span class="badge bg-primary rounded-pill">&#8594;<' + '/span><' + '/button>';
        linksHtml += btnStart + btnClick + btnContent + btnBadge;
    }
    
    linksList.innerHTML = linksHtml;
    linksContainer.style.display = 'block';
}

// Click a related link using Click API
function clickRelatedLink(linkId, linkName) {
    if (!currentSession.sessionId || !currentSession.pageContext) {
        alert('Session expired. Please open the article again.');
        return;
    }
    
    // Add to breadcrumb
    currentSession.breadcrumb.push(linkName);
    
    // Show loading
    document.getElementById('contentLoading').style.display = 'block';
    document.getElementById('contentBody').innerHTML = '';
    document.getElementById('contentLinks').style.display = 'none';
    
    // Update modal title
    document.getElementById('contentModalLabel').textContent = '📄 ' + linkName;
    
    // Make AJAX request to click link
    fetch('/Home/ClickLink', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        },
        body: JSON.stringify({
            sessionId: currentSession.sessionId,
            linkId: linkId,
            pageContext: currentSession.pageContext
        })
    })
    .then(function(response) { return response.json(); })
    .then(function(data) {
        document.getElementById('contentLoading').style.display = 'none';
        
        if (data.success) {
            // Update session context
            currentSession.pageContext = data.pageContext;
            
            // Add to history
            currentSession.history.push({
                title: linkName,
                content: data.content,
                links: data.links,
                pageContext: data.pageContext
            });
            
            // Format and display content
            const formattedContent = formatContent(data.content);
            document.getElementById('contentBody').innerHTML = formattedContent;
            
            // Display new related links
            displayRelatedLinks(data.links);
            
            // Update breadcrumb
            updateBreadcrumb();
        } else {
            document.getElementById('contentBody').innerHTML = 
                '<div class="alert alert-danger">Error: ' + data.error + '</div>';
        }
        
        // Refresh logs
        if (data.logsUpdated) {
            refreshLogs();
        }
    })
    .catch(function(error) {
        document.getElementById('contentLoading').style.display = 'none';
        document.getElementById('contentBody').innerHTML = 
            '<div class="alert alert-danger">Error clicking link: ' + error.message + '</div>';
    });
}

// Update breadcrumb navigation
function updateBreadcrumb() {
    const breadcrumbContainer = document.getElementById('breadcrumbNav');
    const breadcrumbList = document.getElementById('breadcrumbList');
    
    if (currentSession.breadcrumb.length <= 1) {
        breadcrumbContainer.style.display = 'none';
        return;
    }
    
    let breadcrumbHtml = '<small class="text-muted">📍 Reading path: ';
    for (let i = 0; i < currentSession.breadcrumb.length; i++) {
        const item = currentSession.breadcrumb[i];
        if (i === currentSession.breadcrumb.length - 1) {
            breadcrumbHtml += '<strong>' + escapeHtml(item) + '</strong>';
        } else {
            const escapedItem = escapeHtml(item).replace(/'/g, '&#39;');
            breadcrumbHtml += '<a href="#" onclick="navigateToBreadcrumb(' + i + '); return false;" style="color: #0d6efd; text-decoration: none;">' + escapeHtml(item) + '</a>';
        }
        if (i < currentSession.breadcrumb.length - 1) {
            breadcrumbHtml += ' &gt; ';
        }
    }
    breadcrumbHtml += '</small>';
    
    breadcrumbList.innerHTML = breadcrumbHtml;
    breadcrumbContainer.style.display = 'block';
}

// Navigate back to a previous breadcrumb item
function navigateToBreadcrumb(index) {
    if (index < 0 || index >= currentSession.history.length) {
        return;
    }
    
    // Get the state at that point
    const state = currentSession.history[index];
    
    // Truncate breadcrumb and history
    currentSession.breadcrumb = currentSession.breadcrumb.slice(0, index + 1);
    currentSession.history = currentSession.history.slice(0, index + 1);
    currentSession.pageContext = state.pageContext;
    
    // Update modal title
    document.getElementById('contentModalLabel').textContent = '📄 ' + state.title;
    
    // Display the content
    const formattedContent = formatContent(state.content);
    document.getElementById('contentBody').innerHTML = formattedContent;
    
    // Display links
    displayRelatedLinks(state.links);
    
    // Update breadcrumb
    updateBreadcrumb();
}
