import json
import redis.asyncio as aioredis
from typing import List, Dict, Any, Optional
from datetime import datetime

class RedisSession:
    """Custom Redis session for OpenAI Agents SDK"""
    
    def __init__(self, user_id: str, conversation_id: str, redis_url: str):
        self.user_id = user_id
        self.conversation_id = conversation_id
        self.redis_url = redis_url
        self._client: Optional[aioredis.Redis] = None
    
    async def _get_client(self) -> aioredis.Redis:
        if self._client is None:
            self._client = await aioredis.from_url(self.redis_url, encoding="utf-8", decode_responses=True)
        return self._client
    
    async def get_items(self, limit: Optional[int] = None) -> List[Dict[str, Any]]:
        client = await self._get_client()
        key = f"conversation:{self.conversation_id}"
        items = await client.lrange(key, -limit if limit else 0, -1)
        return [json.loads(item) for item in items]
    
    async def add_items(self, items: List[Dict[str, Any]]) -> None:
        client = await self._get_client()
        key = f"conversation:{self.conversation_id}"
        for item in items:
            if "timestamp" not in item:
                item["timestamp"] = datetime.utcnow().isoformat()
            await client.rpush(key, json.dumps(item))
        await client.ltrim(key, -50, -1)
        await client.expire(key, 30 * 24 * 60 * 60)
    
    async def pop_item(self) -> Optional[Dict[str, Any]]:
        client = await self._get_client()
        key = f"conversation:{self.conversation_id}"
        item = await client.rpop(key)
        return json.loads(item) if item else None
    
    async def clear_session(self) -> None:
        client = await self._get_client()
        await client.delete(f"conversation:{self.conversation_id}")
    
    async def close(self):
        if self._client:
            await self._client.close()
