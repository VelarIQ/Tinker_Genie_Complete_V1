#!/bin/bash

# Add using statement if not present
if ! grep -q "using TinkerGenie.API.Services;" Hubs/ChatHub.cs; then
    sed -i '1a using TinkerGenie.API.Services;' Hubs/ChatHub.cs
fi

# Add the enhanced service field
sed -i '/private readonly IConnectionMonitorService\? _connectionMonitor;/a\        private readonly IEnhancedPromptService? _enhancedPromptService;' Hubs/ChatHub.cs 2>/dev/null || true

# Update constructor parameters
sed -i '/IConnectionMonitorService\? connectionMonitor = null)/a\            IEnhancedPromptService? enhancedPromptService = null,' Hubs/ChatHub.cs 2>/dev/null || true

# Add field assignment in constructor
sed -i '/_connectionMonitor = connectionMonitor;/a\            _enhancedPromptService = enhancedPromptService;' Hubs/ChatHub.cs 2>/dev/null || true

echo "ChatHub updated to include enhanced service"
