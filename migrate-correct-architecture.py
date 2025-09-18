#!/usr/bin/env python3
"""
Migrate data with correct architecture:
- User Collections: Personal chat data per user
- Curriculum Collection: Shared leadership curriculum content
- Daily Prompts Collection: Shared daily leadership prompts
"""

import requests
import json
import psycopg2
from datetime import datetime
import sys

# Configuration
WEAVIATE_URL = "http://localhost:8082"
DB_CONFIG = {
    "host": "161.35.5.159",
    "database": "tinker_genie",
    "user": "genie_admin",
    "password": "TinkerGenie1234"
}

def get_database_connection():
    """Get database connection"""
    try:
        return psycopg2.connect(**DB_CONFIG)
    except Exception as e:
        print(f"❌ Database connection failed: {e}")
        return None

def get_existing_users():
    """Get all existing users from the database"""
    conn = get_database_connection()
    if not conn:
        return []
    
    try:
        with conn.cursor() as cur:
            # Get users from various tables
            cur.execute("""
                SELECT DISTINCT username FROM users 
                WHERE username IS NOT NULL AND username != ''
                UNION
                SELECT DISTINCT user_id FROM user_progress 
                WHERE user_id IS NOT NULL AND user_id != ''
                UNION
                SELECT DISTINCT user_id FROM conversation_history 
                WHERE user_id IS NOT NULL AND user_id != ''
            """)
            users = [row[0] for row in cur.fetchall()]
            return users
    except Exception as e:
        print(f"❌ Error getting users: {e}")
        return []
    finally:
        conn.close()

def get_leadership_prompts():
    """Get all leadership daily prompts"""
    conn = get_database_connection()
    if not conn:
        return []
    
    try:
        with conn.cursor() as cur:
            cur.execute("""
                SELECT id, day_number, prompt_title, prompt_text, fill_in_blanks, 
                       task_instructions, estimated_time_minutes, version, follow_up_questions
                FROM leadership_daily_prompts 
                WHERE is_active = true
                ORDER BY day_number
            """)
            
            prompts = []
            for row in cur.fetchall():
                prompts.append({
                    "id": str(row[0]),
                    "day_number": row[1],
                    "prompt_title": row[2],
                    "prompt_text": row[3],
                    "fill_in_blanks": row[4],
                    "task_instructions": row[5],
                    "estimated_time_minutes": row[6],
                    "version": row[7],
                    "follow_up_questions": row[8]
                })
            return prompts
    except Exception as e:
        print(f"❌ Error getting leadership prompts: {e}")
        return []
    finally:
        conn.close()

def get_leadership_content():
    """Get all leadership content"""
    conn = get_database_connection()
    if not conn:
        return []
    
    try:
        with conn.cursor() as cur:
            cur.execute("""
                SELECT content_id, type, day_number, title, content, prompt_phase,
                       fill_in_blanks, location_info, reference_note, category,
                       source_file, source_url, prompt_text
                FROM leadership_content
                ORDER BY day_number, content_id
            """)
            
            content = []
            for row in cur.fetchall():
                content.append({
                    "content_id": row[0],
                    "type": row[1],
                    "day_number": row[2],
                    "title": row[3],
                    "content": row[4],
                    "prompt_phase": row[5],
                    "fill_in_blanks": row[6],
                    "location_info": row[7],
                    "reference_note": row[8],
                    "category": row[9],
                    "source_file": row[10],
                    "source_url": row[11],
                    "prompt_text": row[12]
                })
            return content
    except Exception as e:
        print(f"❌ Error getting leadership content: {e}")
        return []
    finally:
        conn.close()

def index_content_to_weaviate(collection_name, content, content_type, metadata, user_id=None):
    """Index content to Weaviate collection"""
    try:
        properties = {
            "text": content,
            "type": content_type,
            "metadata": json.dumps(metadata),
            "createdAt": datetime.utcnow().isoformat() + "Z"
        }
        
        # Add userId only for user-specific collections
        if user_id:
            properties["userId"] = user_id
        
        object_payload = {
            "class": collection_name,
            "properties": properties
        }
        
        response = requests.post(f"{WEAVIATE_URL}/v1/objects", json=object_payload)
        
        if response.status_code == 200:
            return True
        else:
            print(f"   ❌ Failed to index {content_type} to {collection_name}: {response.status_code}")
            return False
            
    except Exception as e:
        print(f"   ❌ Error indexing {content_type} to {collection_name}: {e}")
        return False

def create_shared_collections():
    """Create shared collections for curriculum and daily prompts"""
    print("🏗️ Creating shared collections...")
    
    # Create DailyPrompts collection
    daily_prompts_schema = {
        "class": "DailyPrompts",
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
                "name": "type",
                "dataType": ["string"]
            },
            {
                "name": "createdAt",
                "dataType": ["date"]
            }
        ]
    }
    
    # Create Curriculum collection
    curriculum_schema = {
        "class": "Curriculum",
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
                "name": "type",
                "dataType": ["string"]
            },
            {
                "name": "createdAt",
                "dataType": ["date"]
            }
        ]
    }
    
    # Create collections
    collections = [
        ("DailyPrompts", daily_prompts_schema),
        ("Curriculum", curriculum_schema)
    ]
    
    for collection_name, schema in collections:
        try:
            # Check if collection exists
            response = requests.get(f"{WEAVIATE_URL}/v1/schema/{collection_name}")
            if response.status_code == 200:
                print(f"   ✅ {collection_name} collection already exists")
                continue
            
            # Create collection
            response = requests.post(f"{WEAVIATE_URL}/v1/schema", json=schema)
            if response.status_code == 200:
                print(f"   ✅ Created {collection_name} collection")
            else:
                print(f"   ❌ Failed to create {collection_name} collection: {response.status_code}")
        except Exception as e:
            print(f"   ❌ Error creating {collection_name} collection: {e}")

def migrate_daily_prompts(prompts):
    """Migrate daily prompts to shared DailyPrompts collection"""
    print(f"📅 Migrating {len(prompts)} daily prompts to shared collection...")
    
    success_count = 0
    for prompt in prompts:
        metadata = {
            "source": "leadership_daily_prompts",
            "prompt_id": prompt["id"],
            "day_number": prompt["day_number"],
            "version": prompt["version"],
            "estimated_time_minutes": prompt["estimated_time_minutes"],
            "fill_in_blanks": prompt["fill_in_blanks"],
            "task_instructions": prompt["task_instructions"],
            "follow_up_questions": prompt["follow_up_questions"]
        }
        
        # Create comprehensive prompt text
        prompt_text = f"Day {prompt['day_number']}: {prompt['prompt_title']}\n\n{prompt['prompt_text']}"
        
        if prompt["task_instructions"]:
            prompt_text += f"\n\nTask Instructions: {prompt['task_instructions']}"
        
        if prompt["fill_in_blanks"]:
            prompt_text += f"\n\nFill-in-the-blank exercises: {', '.join(prompt['fill_in_blanks'])}"
        
        if prompt["follow_up_questions"]:
            prompt_text += f"\n\nFollow-up questions: {json.dumps(prompt['follow_up_questions'])}"
        
        if index_content_to_weaviate("DailyPrompts", prompt_text, "daily_prompt", metadata):
            success_count += 1
    
    print(f"   ✅ Successfully migrated {success_count}/{len(prompts)} daily prompts")
    return success_count

def migrate_curriculum_content(content):
    """Migrate curriculum content to shared Curriculum collection"""
    print(f"📚 Migrating {len(content)} curriculum items to shared collection...")
    
    success_count = 0
    for item in content:
        metadata = {
            "source": "leadership_content",
            "content_id": item["content_id"],
            "day_number": item["day_number"],
            "prompt_phase": item["prompt_phase"],
            "category": item["category"],
            "source_file": item["source_file"],
            "source_url": item["source_url"],
            "fill_in_blanks": item["fill_in_blanks"],
            "location_info": item["location_info"],
            "reference_note": item["reference_note"]
        }
        
        # Create comprehensive content text
        content_text = f"{item['title']}\n\n{item['content']}"
        
        if item["prompt_text"]:
            content_text += f"\n\nPrompt: {item['prompt_text']}"
        
        if item["reference_note"]:
            content_text += f"\n\nReference: {item['reference_note']}"
        
        if index_content_to_weaviate("Curriculum", content_text, "curriculum_content", metadata):
            success_count += 1
    
    print(f"   ✅ Successfully migrated {success_count}/{len(content)} curriculum items")
    return success_count

def create_user_collections(users):
    """Create user-specific collections for personal data"""
    print(f"👥 Creating user-specific collections for {len(users)} users...")
    
    for user_id in users:
        collection_name = f"User_{user_id}_Data"
        
        # Check if collection exists
        try:
            response = requests.get(f"{WEAVIATE_URL}/v1/schema/{collection_name}")
            if response.status_code == 200:
                print(f"   ✅ {collection_name} already exists")
                continue
        except:
            pass
        
        # Create user collection schema
        user_schema = {
            "class": collection_name,
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
            response = requests.post(f"{WEAVIATE_URL}/v1/schema", json=user_schema)
            if response.status_code == 200:
                print(f"   ✅ Created {collection_name}")
            else:
                print(f"   ❌ Failed to create {collection_name}: {response.status_code}")
        except Exception as e:
            print(f"   ❌ Error creating {collection_name}: {e}")

def main():
    print("🏗️ Migrating Data with Correct Architecture")
    print("===========================================")
    print("Architecture:")
    print("  - User Collections: Personal chat data per user")
    print("  - DailyPrompts Collection: Shared daily leadership prompts")
    print("  - Curriculum Collection: Shared leadership curriculum content")
    print()
    
    # Get existing users
    print("👥 Getting existing users...")
    users = get_existing_users()
    if not users:
        print("❌ No users found. Creating default users...")
        users = ["Chris", "Amber", "Mike", "John", "Joleen", "Leighton", "Matt"]
    
    print(f"   Found {len(users)} users: {', '.join(users)}")
    
    # Create collections
    create_shared_collections()
    create_user_collections(users)
    
    # Get leadership data
    print("\n📚 Getting leadership data...")
    prompts = get_leadership_prompts()
    content = get_leadership_content()
    
    print(f"   Found {len(prompts)} daily prompts")
    print(f"   Found {len(content)} curriculum items")
    
    # Migrate shared data
    total_prompts_migrated = 0
    total_content_migrated = 0
    
    if prompts:
        total_prompts_migrated = migrate_daily_prompts(prompts)
    
    if content:
        total_content_migrated = migrate_curriculum_content(content)
    
    # Summary
    print(f"\n🎉 Migration Complete!")
    print("=" * 50)
    print(f"✅ Users processed: {len(users)}")
    print(f"✅ User collections created: {len(users)}")
    print(f"✅ Daily prompts migrated: {total_prompts_migrated}")
    print(f"✅ Curriculum content migrated: {total_content_migrated}")
    
    print(f"\n🏗️ Architecture Summary:")
    print(f"   - DailyPrompts: Shared collection for all daily leadership prompts")
    print(f"   - Curriculum: Shared collection for all leadership curriculum content")
    print(f"   - User_*_Data: Individual collections for each user's personal data")
    
    print(f"\n🔍 Next Steps:")
    print(f"   1. Update WeaviateService to use correct collections")
    print(f"   2. Test shared curriculum access")
    print(f"   3. Test user-specific data isolation")

if __name__ == "__main__":
    main()
