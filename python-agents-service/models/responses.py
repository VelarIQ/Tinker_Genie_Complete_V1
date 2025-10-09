from pydantic import BaseModel, Field
from typing import Optional, Dict, Any
from datetime import datetime

class ChatResponse(BaseModel):
    response: str
    conversation_id: str
    agent_name: Optional[str] = None
    message_type: Optional[str] = None
    timestamp: datetime = Field(default_factory=datetime.utcnow)
    metadata: Optional[Dict[str, Any]] = Field(default_factory=dict)
