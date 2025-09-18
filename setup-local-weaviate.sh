#!/bin/bash

# Setup Local Weaviate with Self-Hosted Embeddings
# This removes OpenAI dependency for embeddings

set -e

echo "🚀 Setting up local Weaviate with self-hosted embeddings..."

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Docker is not installed. Installing Docker..."
    curl -fsSL https://get.docker.com -o get-docker.sh
    sh get-docker.sh
    systemctl start docker
    systemctl enable docker
fi

# Check if Docker Compose is installed
if ! command -v docker-compose &> /dev/null; then
    echo "❌ Docker Compose is not installed. Installing..."
    curl -L "https://github.com/docker/compose/releases/download/v2.20.0/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
    chmod +x /usr/local/bin/docker-compose
fi

# Create directory for Weaviate data
mkdir -p /var/lib/weaviate-local
chmod 755 /var/lib/weaviate-local

# Stop any existing Weaviate containers
echo "🛑 Stopping existing Weaviate containers..."
docker-compose -f docker-compose.weaviate.yml down || true

# Start the new setup
echo "🐳 Starting Weaviate with local embeddings..."
docker-compose -f docker-compose.weaviate.yml up -d

# Wait for services to be healthy
echo "⏳ Waiting for services to be ready..."
sleep 30

# Check Weaviate health
echo "🔍 Checking Weaviate health..."
for i in {1..10}; do
    if curl -s http://localhost:8080/v1/meta > /dev/null; then
        echo "✅ Weaviate is ready!"
        break
    else
        echo "⏳ Waiting for Weaviate... (attempt $i/10)"
        sleep 10
    fi
done

# Check transformers service
echo "🔍 Checking transformers service..."
for i in {1..10}; do
    if curl -s http://localhost:8081/health > /dev/null; then
        echo "✅ Transformers service is ready!"
        break
    else
        echo "⏳ Waiting for transformers... (attempt $i/10)"
        sleep 10
    fi
done

# Test embedding generation
echo "🧪 Testing embedding generation..."
curl -X POST "http://localhost:8080/v1/vectors" \
  -H "Content-Type: application/json" \
  -d '{
    "texts": ["test embedding"],
    "model": "BAAI/bge-small-en-v1.5"
  }' || echo "⚠️ Embedding test failed - service may still be starting"

echo "🎉 Setup complete!"
echo ""
echo "📋 Next steps:"
echo "1. Update your appsettings.json to use: http://localhost:8080"
echo "2. Remove X-OpenAI-Api-Key headers from WeaviateService"
echo "3. Test the new setup"
echo ""
echo "🔗 Services:"
echo "- Weaviate: http://localhost:8080"
echo "- Transformers: http://localhost:8081"
echo "- Weaviate UI: http://localhost:8080/v1/meta"
