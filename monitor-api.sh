#!/bin/bash

# TinkerGenie API Health Monitor
# Monitors API health and restarts if needed

API_URL="http://localhost:8765/health"
LOG_FILE="/var/log/tinker-api-monitor.log"
MAX_RETRIES=3
RETRY_DELAY=10

log_message() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $1" >> "$LOG_FILE"
}

check_api_health() {
    local response=$(curl -s -o /dev/null -w "%{http_code}" "$API_URL" --max-time 10)
    echo "$response"
}

restart_api() {
    log_message "Restarting TinkerGenie API..."
    systemctl restart tinker-api
    sleep 15
    
    # Verify restart
    local health_check=$(check_api_health)
    if [ "$health_check" = "200" ]; then
        log_message "API restarted successfully"
        return 0
    else
        log_message "API restart failed - health check returned $health_check"
        return 1
    fi
}

# Main monitoring loop
log_message "Starting TinkerGenie API health monitor"

while true; do
    health_status=$(check_api_health)
    
    if [ "$health_status" = "200" ]; then
        # API is healthy
        log_message "API health check: OK ($health_status)"
    else
        # API is unhealthy
        log_message "API health check: FAILED ($health_status)"
        
        # Try to restart
        if restart_api; then
            log_message "API recovery successful"
        else
            log_message "API recovery failed - alerting administrator"
            # Here you could add email/SMS alerts
        fi
    fi
    
    # Wait before next check
    sleep 30
done
