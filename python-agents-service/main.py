"""Python Agents Service - FastAPI REST API"""
import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../agents-sdk/src'))

from fastapi import FastAPI, HTTPException, status
from fastapi.middleware.cors import CORSMiddleware
from contextlib import asynccontextmanager
import uvicorn
from datetime import datetime
import traceback

from agents import Runner
from config import settings
from models.requests import ChatRequest
from models.responses import ChatResponse
from agents.triage_agent import triage_agent
from services.session_service import RedisSession
from tools.database_tools import init_db_pool
from tools.redis_tools import init_redis_client
from tools.weaviate_tools import init_weaviate_client

@asynccontextmanager
async def lifespan(app: FastAPI):
    """Initialize connections on startup"""
    print("🚀 Starting Python Agents Service...")
    try:
        await init_db_pool()
        print("✅ PostgreSQL initialized")
        await init_redis_client()
        print("✅ Redis initialized")
        init_weaviate_client()
        print("✅ Weaviate initialized")
        print(f"🎯 Ready on port {settings.port}")
    except Exception as e:
        print(f"❌ Startup error: {e}")
        traceback.print_exc()
    yield
    print("👋 Shutting down...")

app = FastAPI(title="TinkerGenie Agents", version="1.0.0", lifespan=lifespan)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_credentials=True, allow_methods=["*"], allow_headers=["*"])

@app.get("/health")
async def health_check():
    return {"status": "healthy", "service": "python-agents-service", "timestamp": datetime.utcnow().isoformat()}

@app.post("/api/agents/chat", response_model=ChatResponse)
async def chat(request: ChatRequest):
    """Main chat endpoint - routes to appropriate agent via triage"""
    try:
        print(f"📨 Chat from user {request.user_id}: {request.message[:50]}...")
        conversation_id = request.conversation_id or f"conv_{request.user_id}_{datetime.utcnow().timestamp()}"
        session = RedisSession(request.user_id, conversation_id, settings.redis_url)
        
        result = await Runner.run(triage_agent, request.message, session=session, max_turns=settings.max_agent_turns)
        
        print(f"✅ Response generated: {len(result.final_output)} chars")
        await session.close()
        
        return ChatResponse(
            response=result.final_output,
            conversation_id=conversation_id,
            agent_name=getattr(result, 'agent_name', 'Triage'),
            message_type=request.message_type,
            metadata={"user_id": request.user_id, "first_name": request.first_name}
        )
    except Exception as e:
        print(f"❌ Error: {e}")
        traceback.print_exc()
        raise HTTPException(status_code=status.HTTP_500_INTERNAL_SERVER_ERROR, detail=str(e))

if __name__ == "__main__":
    print(f"🌐 Starting on {settings.host}:{settings.port}")
    uvicorn.run("main:app", host=settings.host, port=settings.port, reload=settings.environment == "development", log_level=settings.log_level.lower())
