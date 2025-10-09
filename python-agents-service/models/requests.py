from pydantic import BaseModel, Field
from typing import Optional

class ChatRequest(BaseModel):
    user_id: str
    message: str
    conversation_id: Optional[str] = None
    message_type: Optional[str] = None
    first_name: Optional[str] = "User"
    business_name: Optional[str] = "Your Business"
