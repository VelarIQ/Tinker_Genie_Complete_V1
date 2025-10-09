import asyncpg
from agents import function_tool
from config import settings
from typing import Dict, Any

db_pool = None

async def init_db_pool():
    global db_pool
    db_pool = await asyncpg.create_pool(settings.database_url, min_size=2, max_size=10)
    return db_pool

@function_tool
async def get_user_context(user_id: str) -> Dict[str, Any]:
    """Get user's current day, progress, business info"""
    async with db_pool.acquire() as conn:
        row = await conn.fetchrow(
            "SELECT user_id, first_name, business_name, current_day, progress_level FROM users WHERE user_id = $1",
            user_id
        )
        return dict(row) if row else {"user_id": user_id, "current_day": 1}

@function_tool
async def get_user_progress(user_id: str) -> Dict[str, Any]:
    """Get detailed user progress and stats"""
    async with db_pool.acquire() as conn:
        row = await conn.fetchrow(
            "SELECT current_day, progress_level, completed_modules, total_conversations FROM users WHERE user_id = $1",
            user_id
        )
        return dict(row) if row else {"current_day": 1}

@function_tool
async def update_user_progress(user_id: str, increment_day: bool = False) -> bool:
    """Update user progress"""
    async with db_pool.acquire() as conn:
        if increment_day:
            await conn.execute("UPDATE users SET current_day = current_day + 1 WHERE user_id = $1", user_id)
        return True

@function_tool
async def get_daily_prompt(user_id: str) -> Dict[str, Any]:
    """Get current day's prompt"""
    async with db_pool.acquire() as conn:
        user_row = await conn.fetchrow("SELECT current_day FROM users WHERE user_id = $1", user_id)
        if not user_row:
            return {"error": "User not found"}
        prompt_row = await conn.fetchrow(
            "SELECT day_number, prompt_text, category FROM daily_prompts WHERE day_number = $1",
            user_row['current_day']
        )
        return dict(prompt_row) if prompt_row else {"error": "No prompt found"}
