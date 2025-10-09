"""Configuration for Python Agents Service"""
import os
from pydantic_settings import BaseSettings

class Settings(BaseSettings):
    openai_api_key: str
    openai_model: str = "gpt-4"
    database_url: str
    redis_url: str
    weaviate_url: str
    weaviate_api_key: str = ""
    dotnet_api_url: str = "http://localhost:5000"
    port: int = 5001
    host: str = "0.0.0.0"
    environment: str = "development"
    log_level: str = "INFO"
    max_agent_turns: int = 10
    agent_timeout: int = 120
    
    class Config:
        env_file = ".env"
        case_sensitive = False

settings = Settings()
