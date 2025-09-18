#!/usr/bin/env python3
"""
Create Weaviate schema with local text2vec-transformers vectorizer
This eliminates OpenAI dependency for embeddings
"""

import requests
import json

WEAVIATE_URL = "http://localhost:8082"

def create_schema():
    """Create the Weaviate schema with local vectorizer"""
    
    # First, check if schema already exists and delete it
    try:
        print("🗑️ Checking for existing schema...")
        response = requests.get(f"{WEAVIATE_URL}/v1/schema")
        if response.status_code == 200:
            schema_data = response.json()
            classes = schema_data.get("classes", [])
            for class_info in classes:
                class_name = class_info.get("class")
                if class_name == "Doc":
                    print(f"   Deleting existing Doc class...")
                    delete_response = requests.delete(f"{WEAVIATE_URL}/v1/schema/Doc")
                    if delete_response.status_code == 200:
                        print("   ✅ Existing Doc class deleted")
                    break
    except:
        pass
    
    schema = {
        "class": "Doc",
        "vectorizer": "text2vec-transformers",
        "moduleConfig": {
            "text2vec-transformers": {
                "vectorizeClassName": False
            }
        },
        "properties": [
            {
                "name": "text",
                "dataType": ["text"],
                "moduleConfig": {
                    "text2vec-transformers": { "skip": False }
                }
            },
            {
                "name": "metadata",
                "dataType": ["text"]
            },
            {
                "name": "userId",
                "dataType": ["string"]
            },
            {
                "name": "type",
                "dataType": ["string"]
            },
            {
                "name": "createdAt",
                "dataType": ["date"]
            }
        ]
    }
    
    try:
        print("🔧 Creating Weaviate schema with local vectorizer...")
        response = requests.post(f"{WEAVIATE_URL}/v1/schema", json=schema)
        
        if response.status_code == 200:
            print("✅ Schema created successfully!")
            print("   - Class: Doc")
            print("   - Vectorizer: text2vec-transformers (local)")
            print("   - Properties: text, metadata, userId, type, createdAt")
            return True
        else:
            print(f"❌ Schema creation failed: {response.status_code}")
            print(f"   Response: {response.text}")
            return False
            
    except requests.exceptions.RequestException as e:
        print(f"❌ Connection error: {e}")
        return False

def test_schema():
    """Test that the schema was created correctly"""
    try:
        print("\n🧪 Testing schema...")
        response = requests.get(f"{WEAVIATE_URL}/v1/schema")
        
        if response.status_code == 200:
            schema_data = response.json()
            classes = schema_data.get("classes", [])
            
            doc_class = next((c for c in classes if c.get("class") == "Doc"), None)
            if doc_class:
                print("✅ Schema verification successful!")
                print(f"   - Vectorizer: {doc_class.get('vectorizer')}")
                print(f"   - Properties: {[p['name'] for p in doc_class.get('properties', [])]}")
                return True
            else:
                print("❌ Doc class not found in schema")
                return False
        else:
            print(f"❌ Schema verification failed: {response.status_code}")
            return False
            
    except requests.exceptions.RequestException as e:
        print(f"❌ Schema test error: {e}")
        return False

def test_embedding():
    """Test that embeddings work with the local vectorizer"""
    try:
        print("\n🧪 Testing local embedding...")
        
        # Create a test object - this should auto-embed using local transformers
        test_object = {
            "class": "Doc",
            "properties": {
                "text": "This is a test of local embeddings without OpenAI",
                "metadata": "test",
                "userId": "test_user",
                "type": "test",
                "createdAt": "2025-01-11T00:00:00Z"
            }
        }
        
        response = requests.post(f"{WEAVIATE_URL}/v1/objects", json=test_object)
        
        if response.status_code == 200:
            print("✅ Local embedding test successful!")
            print("   - Object created with auto-generated local embeddings")
            return True
        else:
            print(f"❌ Embedding test failed: {response.status_code}")
            print(f"   Response: {response.text}")
            return False
            
    except requests.exceptions.RequestException as e:
        print(f"❌ Embedding test error: {e}")
        return False

if __name__ == "__main__":
    print("🚀 Setting up Weaviate with local embeddings")
    print("=" * 50)
    
    # Create schema
    if create_schema():
        # Test schema
        if test_schema():
            # Test embedding
            if test_embedding():
                print("\n🎉 All tests passed! Local Weaviate setup complete.")
                print("   Your app can now use local embeddings without OpenAI quota.")
            else:
                print("\n⚠️ Schema created but embedding test failed.")
        else:
            print("\n⚠️ Schema creation may have failed.")
    else:
        print("\n❌ Schema creation failed.")
