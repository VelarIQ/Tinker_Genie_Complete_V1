# OpenAI Quota Issue - Root Cause & Solutions

## 🔍 **Root Cause Analysis**

Your 429 "insufficient_quota" error is caused by **shared OpenAI quota** between:
1. **Chat Service** - Uses OpenAI for chat completions
2. **Weaviate Cloud** - Uses `text2vec-openai` module for embeddings

Both services hit the same OpenAI project, sharing rate limits and quota.

## 📊 **Current Configuration**

```json
{
  "OpenAI": {
    "ApiKey": "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"
  },
  "Weaviate": {
    "ApiKey": "cHc2YzhwMFV3OHpZQ1hrVV8xU2Q4cmhKTEk3ZE5ZQ1JoY0ZwTGdNMlZkRUNqQm1NYVhKLzZ0NllYK0JzPV92MjAw",
    "Url": "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud"
  }
}
```

## 🛠️ **Solution Options**

### **Option 1: Split OpenAI Projects (Recommended)**

1. **Create separate OpenAI projects:**
   - **Project A**: "TinkerGenie-Chat" (for chat completions)
   - **Project B**: "TinkerGenie-Embeddings" (for Weaviate embeddings)

2. **Update configuration:**
   ```json
   {
     "OpenAI": {
       "ApiKey": "sk-proj-CHAT_KEY_HERE",
       "Model": "gpt-4o-mini"
     },
     "Weaviate": {
       "ApiKey": "cHc2YzhwMFV3OHpZQ1hrVV8xU2Q4cmhKTEk3ZE5ZQ1JoY0ZwTGdNMlZkRUNqQm1NYVhKLzZ0NllYK0JzPV92MjAw",
       "Url": "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud",
       "OpenAI": {
         "ApiKey": "sk-proj-EMBEDDINGS_KEY_HERE",
         "Model": "text-embedding-3-small"
       }
     }
   }
   ```

3. **Configure Weaviate Cloud:**
   - Set `X-OpenAI-Api-Key` header to embeddings key
   - Use `text-embedding-3-small` (cheaper than `text-embedding-3-large`)

### **Option 2: Switch to Non-OpenAI Embeddings**

1. **Use text2vec-transformers** (local model)
2. **Use text2vec-cohere** (separate provider)
3. **Use text2vec-bge** (open source)

### **Option 3: Implement Caching & Rate Limiting**

1. **Cache embeddings** by content hash
2. **Batch embedding requests**
3. **Add exponential backoff** for 429 errors
4. **Use cheaper embedding model** (`text-embedding-3-small`)

## 🚀 **Immediate Action Plan**

### **Step 1: Check Current Weaviate Configuration**
```bash
# Check if Weaviate is using text2vec-openai
curl -H "X-Weaviate-Api-Key: YOUR_KEY" \
  "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud/v1/schema"
```

### **Step 2: Create Separate OpenAI Projects**
1. Go to OpenAI Dashboard
2. Create new project "TinkerGenie-Embeddings"
3. Generate new API key for embeddings
4. Update Weaviate configuration

### **Step 3: Update Weaviate Headers**
```bash
# Set OpenAI key for embeddings only
curl -H "X-Weaviate-Api-Key: YOUR_WEAVIATE_KEY" \
     -H "X-OpenAI-Api-Key: YOUR_EMBEDDINGS_KEY" \
     "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud/v1/schema"
```

### **Step 4: Test Separation**
```bash
# Test chat service (should use chat key)
curl http://127.0.0.1:8765/api/health

# Test Weaviate (should use embeddings key)
curl -H "X-Weaviate-Api-Key: YOUR_KEY" \
  "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud/v1/schema"
```

## 📈 **Cost Optimization**

1. **Use `text-embedding-3-small`** (10x cheaper than large)
2. **Cache embeddings** by content hash
3. **Batch requests** when possible
4. **Monitor usage** in OpenAI dashboard

## 🔧 **Code Changes Needed**

1. **Update WeaviateService** to use separate OpenAI key
2. **Add embedding caching** logic
3. **Implement rate limiting** for 429 errors
4. **Add monitoring** for quota usage

## ✅ **Expected Results**

- **Chat service** uses chat project quota
- **Embeddings** use separate embeddings project quota
- **No more 429 errors** from shared quota
- **Better cost control** and monitoring
