import weaviate
from agents import function_tool
from config import settings

weaviate_client = None

def init_weaviate_client():
    global weaviate_client
    weaviate_client = weaviate.Client(url=settings.weaviate_url)
    return weaviate_client

@function_tool
def search_knowledge_base(query: str, limit: int = 5) -> str:
    """Search Two Brain knowledge base - ONLY use this information, don't make things up"""
    result = weaviate_client.query.get(
        "LeadershipContent", ["content", "title"]
    ).with_near_text({"concepts": [query]}).with_limit(limit).do()
    
    if not result or "data" not in result:
        return "No relevant knowledge found."
    
    content = result["data"]["Get"]["LeadershipContent"]
    if not content:
        return "No relevant knowledge found."
    
    formatted = "TWO BRAIN KNOWLEDGE:\n\n"
    for idx, item in enumerate(content, 1):
        formatted += f"{idx}. {item.get('title', '')}\n   {item.get('content', '')[:200]}...\n\n"
    return formatted

@function_tool
def search_curriculum_modules(query: str, limit: int = 3) -> list:
    """Search Two Brain curriculum modules and courses"""
    result = weaviate_client.query.get(
        "CurriculumModule", ["title", "description", "url"]
    ).with_near_text({"concepts": [query]}).with_limit(limit).do()
    
    if not result or "data" not in result:
        return []
    modules = result["data"]["Get"]["CurriculumModule"]
    return [{"title": m.get("title"), "description": m.get("description"), "url": m.get("url")} for m in modules] if modules else []
