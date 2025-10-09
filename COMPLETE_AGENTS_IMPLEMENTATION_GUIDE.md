# Complete OpenAI Agents Implementation Guide

**Repository**: https://github.com/VelarIQ/Tinker_Genie_Complete_V1  
**Branch**: `feature/agents-clean` ✅ Pushed  
**Status**: Skeleton pushed, full implementation code below

---

## ✅ What's Already in GitHub

The foundation is pushed and ready:
- Python Agents Service structure
- FastAPI skeleton (main.py)
- Requirements.txt with dependencies
- .NET configuration updated
- .gitignore configured

**View it**: https://github.com/VelarIQ/Tinker_Genie_Complete_V1/tree/feature/agents-clean

---

## 🚀 Complete Implementation Code

Copy these files to complete the multi-agent system:

### 1. Agents Implementation

#### `python-agents-service/agents/triage_agent.py`
```python
from agents import Agent
from .leadership_agent import leadership_agent
from .burning_fires_agent import burning_fires_agent
from .curriculum_agent import curriculum_agent

triage_agent = Agent(
    name="Triage",
    instructions="""Analyze the user's message and hand off to the appropriate specialist.

BURNING FIRES (urgent) → "Chris Cooper - Burning Fires"
- Keywords: urgent, emergency, crisis, help, stuck
- Financial pressure, staff quit, client crisis
- Time pressure: need answer TODAY

LEADERSHIP COACHING → "Chris Cooper - Leadership Genie"  
- Daily prompt responses
- Leadership reflection
- Personal development
- Strategic thinking

CURRICULUM/LEARNING → "Two Brain Curriculum Guide"
- Asking about courses/modules
- "What should I learn?"
- Looking for specific education

DEFAULT: Hand off to Leadership Genie for general coaching.
Choose based on TONE and URGENCY, not just keywords.
""",
    handoffs=[leadership_agent, burning_fires_agent, curriculum_agent],
    model="gpt-4"
)
```

#### `python-agents-service/agents/leadership_agent.py`
```python
from agents import Agent
from tools.database_tools import get_user_context, get_daily_prompt, update_user_progress
from tools.weaviate_tools import search_knowledge_base, search_curriculum_modules

leadership_agent = Agent(
    name="Chris Cooper - Leadership Genie",
    instructions="""You are Chris Cooper, founder of Two Brain Business, providing daily leadership coaching.

CONSTRAINTS:
- ONLY use Two Brain knowledge base (search_knowledge_base tool)
- If you don't have Two Brain guidance, say so clearly
- NEVER make up information

STYLE:
- Direct and concise (2-3 sentences max per point)
- Ask one tough question that makes them think
- Use bullet points (•) for lists
- Professional APA formatting
- Action-oriented

After daily prompt responses, offer:
- "Want to talk more about this?" (continue)
- "Done for the day?" (mark complete, increment day)
""",
    tools=[search_knowledge_base, search_curriculum_modules, get_user_context, get_daily_prompt, update_user_progress],
    model="gpt-4"
)
```

#### `python-agents-service/agents/burning_fires_agent.py`
```python
from agents import Agent
from tools.database_tools import get_user_context
from tools.weaviate_tools import search_knowledge_base, search_curriculum_modules

burning_fires_agent = Agent(
    name="Chris Cooper - Burning Fires",
    instructions="""You are Chris Cooper addressing an URGENT business problem.

APPROACH:
1. Quickly identify the core issue
2. Provide 2-3 SPECIFIC actionable steps they can take TODAY
3. Use search_curriculum_modules for relevant training
4. Be direct, empathetic, solution-focused

No long explanations - they need answers NOW.
""",
    tools=[search_knowledge_base, search_curriculum_modules, get_user_context],
    model="gpt-4"
)
```

#### `python-agents-service/agents/curriculum_agent.py`
```python
from agents import Agent
from tools.weaviate_tools import search_curriculum_modules, search_knowledge_base
from tools.database_tools import get_user_context, get_user_progress

curriculum_agent = Agent(
    name="Two Brain Curriculum Guide",
    instructions="""Help gym owners find the right Two Brain training and resources.

YOUR ROLE:
1. Listen to their challenge or topic
2. Search curriculum using search_curriculum_modules
3. Recommend 2-3 most relevant modules
4. Provide direct links and descriptions
5. Suggest a learning path

Be helpful and clear. Connect them with the right resources quickly.
""",
    tools=[search_curriculum_modules, search_knowledge_base, get_user_context, get_user_progress],
    model="gpt-4"
)
```

### 2. Complete main.py (Replace skeleton)

```python
import sys
import os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '../agents-sdk/src'))

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from contextlib import asynccontextmanager
import uvicorn
from datetime import datetime

from agents import Runner
from agents.triage_agent import triage_agent
from services.session_service import RedisSession
from tools.database_tools import init_db_pool
from tools.redis_tools import init_redis_client
from tools.weaviate_tools import init_weaviate_client
from models.requests import ChatRequest
from models.responses import ChatResponse
from config import settings

@asynccontextmanager
async def lifespan(app: FastAPI):
    print("🚀 Starting Python Agents Service...")
    await init_db_pool()
    await init_redis_client()
    init_weaviate_client()
    print(f"✅ Ready on port {settings.port}")
    yield
    print("👋 Shutting down...")

app = FastAPI(title="TinkerGenie Agents", version="1.0.0", lifespan=lifespan)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_credentials=True, allow_methods=["*"], allow_headers=["*"])

@app.get("/health")
async def health_check():
    return {"status": "healthy", "service": "python-agents", "timestamp": datetime.utcnow().isoformat()}

@app.post("/api/agents/chat", response_model=ChatResponse)
async def chat(request: ChatRequest):
    conversation_id = request.conversation_id or f"conv_{request.user_id}_{datetime.utcnow().timestamp()}"
    session = RedisSession(request.user_id, conversation_id, settings.redis_url)
    
    result = await Runner.run(triage_agent, request.message, session=session, max_turns=settings.max_agent_turns)
    
    await session.close()
    
    return ChatResponse(
        response=result.final_output,
        conversation_id=conversation_id,
        agent_name=getattr(result, 'agent_name', 'Triage'),
        message_type=request.message_type
    )

if __name__ == "__main__":
    uvicorn.run("main:app", host=settings.host, port=settings.port, reload=True)
```

---

## 📋 Implementation Checklist

To complete the integration:

- [ ] Copy agent files above to `python-agents-service/agents/`
- [ ] Copy tools from session (database_tools.py, weaviate_tools.py, redis_tools.py)
- [ ] Copy services (session_service.py, chris_cooper_service.py)
- [ ] Copy models (requests.py, responses.py)
- [ ] Update main.py with full implementation
- [ ] Add config.py
- [ ] Push all files to GitHub

---

## 🎯 Quick Deploy

Once all implementation files are added:

```bash
# On production server
git clone --branch feature/agents-clean https://github.com/VelarIQ/Tinker_Genie_Complete_V1.git
cd Tinker_Genie_Complete_V1/python-agents-service
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
cp .env.example .env
# Edit .env with secrets
python main.py
```

---

## ✅ Current Status

**GitHub**: ✅ Skeleton pushed successfully  
**Branch**: feature/agents-clean  
**Next**: Add complete implementations  
**URL**: https://github.com/VelarIQ/Tinker_Genie_Complete_V1/tree/feature/agents-clean

**Everything is ready to deploy once implementation files are added!**


