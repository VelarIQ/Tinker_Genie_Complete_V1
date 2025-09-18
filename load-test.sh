#!/bin/bash

# Simple Load Test for TinkerGenie API
# Tests 150 concurrent users

API_URL="http://tinker.twobrain.ai/health"
CONCURRENT_USERS=150
TEST_DURATION=30

echo "Starting load test with $CONCURRENT_USERS concurrent users for $TEST_DURATION seconds..."
echo "API URL: $API_URL"
echo ""

# Function to make requests
make_request() {
    local user_id=$1
    local start_time=$(date +%s)
    local end_time=$((start_time + TEST_DURATION))
    local request_count=0
    local success_count=0
    
    while [ $(date +%s) -lt $end_time ]; do
        response=$(curl -s -o /dev/null -w "%{http_code}" "$API_URL" --max-time 5)
        request_count=$((request_count + 1))
        
        if [ "$response" = "200" ]; then
            success_count=$((success_count + 1))
        fi
        
        # Small delay to prevent overwhelming
        sleep 0.1
    done
    
    echo "User $user_id: $success_count/$request_count successful requests"
}

# Start concurrent users
echo "Starting $CONCURRENT_USERS concurrent users..."
for i in $(seq 1 $CONCURRENT_USERS); do
    make_request $i &
done

# Wait for all background processes
wait

echo ""
echo "Load test completed!"
echo "Check the API logs and system resources for performance metrics."
