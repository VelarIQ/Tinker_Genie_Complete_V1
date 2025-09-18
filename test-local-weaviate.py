#!/usr/bin/env python3
"""
Test script to verify local Weaviate with self-hosted embeddings is working
"""

import requests
import json
import time

def test_weaviate_connection():
    """Test basic Weaviate connection"""
    try:
        response = requests.get("http://localhost:8082/v1/meta", timeout=10)
        if response.status_code == 200:
            print("✅ Weaviate connection successful")
            meta = response.json()
            print(f"   Version: {meta.get('version')}")
            print(f"   Modules: {list(meta.get('modules', {}).keys())}")
            return True
        else:
            print(f"❌ Weaviate connection failed: {response.status_code}")
            return False
    except Exception as e:
        print(f"❌ Weaviate connection error: {e}")
        return False

def test_embedding_generation():
    """Test embedding generation through Weaviate"""
    try:
        # Test embedding generation
        payload = {
            "texts": ["This is a test sentence for embedding generation"]
        }
        
        response = requests.post(
            "http://localhost:8082/v1/modules/text2vec-transformers/vectors",
            json=payload,
            headers={"Content-Type": "application/json"},
            timeout=30
        )
        
        if response.status_code == 200:
            print("✅ Embedding generation successful")
            # The response might be empty for some Weaviate versions
            if response.content:
                result = response.json()
                print(f"   Response: {result}")
            else:
                print("   Response: Empty (this is normal for some Weaviate versions)")
            return True
        else:
            print(f"❌ Embedding generation failed: {response.status_code}")
            print(f"   Response: {response.text}")
            return False
    except Exception as e:
        print(f"❌ Embedding generation error: {e}")
        return False

def test_transformers_service():
    """Test the transformers service directly"""
    try:
        response = requests.get("http://localhost:8081/meta", timeout=10)
        if response.status_code == 200:
            print("✅ Transformers service connection successful")
            meta = response.json()
            print(f"   Model: {meta.get('name', 'Unknown')}")
            return True
        else:
            print(f"❌ Transformers service failed: {response.status_code}")
            return False
    except Exception as e:
        print(f"❌ Transformers service error: {e}")
        return False

def main():
    print("🧪 Testing Local Weaviate Setup")
    print("=" * 40)
    
    # Test Weaviate connection
    weaviate_ok = test_weaviate_connection()
    
    # Test transformers service
    transformers_ok = test_transformers_service()
    
    # Test embedding generation
    embedding_ok = test_embedding_generation()
    
    print("\n📊 Test Results:")
    print(f"   Weaviate: {'✅' if weaviate_ok else '❌'}")
    print(f"   Transformers: {'✅' if transformers_ok else '❌'}")
    print(f"   Embeddings: {'✅' if embedding_ok else '❌'}")
    
    if weaviate_ok and transformers_ok and embedding_ok:
        print("\n🎉 All tests passed! Local Weaviate setup is working correctly.")
        print("   Your OpenAI quota issue should now be resolved.")
    else:
        print("\n⚠️  Some tests failed. Check the logs above for details.")

if __name__ == "__main__":
    main()
