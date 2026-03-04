#!/usr/bin/env python3
"""
Test image generation only (skip LLM content analysis)
"""
import requests
import json
import uuid
import base64
from pathlib import Path

# Test prompt (pre-generated)
test_prompt = """Generate a professional educational infographic about stargazing and astronomy.

Title: "Unlock the Night Sky: Your Complete Stargazing Guide"

Content:
- Introduction to stargazing basics
- Key equipment: binoculars, telescopes, star charts
- Best practices: dark locations, eye adaptation, red flashlights
- What to observe: constellations, planets, deep sky objects
- Tips for beginners

Use Space/Cosmic Style with dark backgrounds, starlight colors, and astronomical themes.
Professional, educational, visually compelling design.
"""

# Configuration
llm_endpoint = "http://localhost:4141"
output_path = Path("test_image_only_output.png")

print("[Test] Generating image from pre-written prompt...")
print(f"[Test] Prompt length: {len(test_prompt)} characters")

# Generate UUIDs
cv_id = str(uuid.uuid4())
interaction_id = str(uuid.uuid4())
message_id = str(uuid.uuid4())
scenario_guid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"

# Build payload
image_payload = {
    "messages": [
        {
            "id": message_id,
            "author": {"role": "user"},
            "content": {
                "content_type": "multimodal_text",
                "parts": [test_prompt]
            }
        }
    ],
    "virtual_model": "gpt-image-1-5",
    "zdr_type": 1,
    "size": "image",
    "orientation": "landscape",
    "stream": True,
    "image_format": "png",
    "n": 1,
    "encode_user_images_as_vq": True
}

# Build headers
image_headers = {
    "Content-Type": "application/json",
    "Accept": "text/event-stream",
    "X-cv": cv_id,
    "X-ChatGPT-User-Email": "infographic-skill@microsoft.com",
    "X-ChatGPT-User-Id": "infographic-skill-001",
    "X-ModelType": "dev-gpt-image-1-5",
    "X-ScenarioGUID": scenario_guid,
    "x-imagegen-api-use-mainline": "true",
    "X-InteractionId": interaction_id,
    "X-Tag": json.dumps({"Client": "InfographicSkill"})
}

print("[Test] Calling image generation API...")
print("[Test] This may take 30-90 seconds...")

try:
    response = requests.post(
        f"{llm_endpoint}/chatgpt/convo2im",
        json=image_payload,
        headers=image_headers,
        timeout=300,
        stream=True
    )
    response.raise_for_status()
    
    print("[Test] Parsing response stream...")
    image_found = False
    
    for line in response.iter_lines(decode_unicode=True):
        if not line:
            continue
        
        if line.startswith("data: "):
            data_content = line[6:].strip()
            
            if data_content in ["[DONE]", "DONE"]:
                break
            
            try:
                data = json.loads(data_content)
                
                if 'content' in data and 'parts' in data['content']:
                    for part in data['content']['parts']:
                        if part.get('content_type') == 'image' and 'payload' in part:
                            # Decode and save
                            image_bytes = base64.b64decode(part['payload'])
                            output_path.write_bytes(image_bytes)
                            
                            file_size_kb = round(len(image_bytes) / 1024, 2)
                            print(f"[Test] ✅ Image saved to {output_path} ({file_size_kb} KB)")
                            image_found = True
                            break
                
                if image_found:
                    break
                    
            except json.JSONDecodeError:
                continue
    
    if not image_found:
        print("[Test] ❌ No image data found in response")
        exit(1)
    
    print("[Test] ✅ Test completed successfully!")
    
except Exception as e:
    print(f"[Test] ❌ Error: {e}")
    exit(1)
