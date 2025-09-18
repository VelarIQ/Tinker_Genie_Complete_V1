import openai
import json

# Read the API key from appsettings.json
with open('appsettings.json', 'r') as f:
    config = json.load(f)
    api_key = config['OpenAI']['ApiKey']

client = openai.OpenAI(api_key=api_key)

try:
    response = client.chat.completions.create(
        model="gpt-4o-mini",
        messages=[{"role": "user", "content": "Say hello"}],
        max_tokens=10
    )
    print("OpenAI API Test: SUCCESS")
    print(f"Response: {response.choices[0].message.content}")
except Exception as e:
    print(f"OpenAI API Test: FAILED - {e}")
