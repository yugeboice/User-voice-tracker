#!/usr/bin/env python3
"""
Generate infographic image from existing prompt file.
This is a utility script for testing and debugging - it skips content analysis
and prompt construction, directly generating an image from a saved prompt.

Usage:
    python generate_from_prompt.py --prompt prompt_xxx.txt --output image.png
    python generate_from_prompt.py --prompt prompt_xxx.txt --output image.png --endpoint http://localhost:4141
"""

import argparse
import base64
import json
import sys
import uuid
from pathlib import Path

import requests


def generate_image_from_prompt(prompt_file: str, output_path: str, llm_endpoint: str) -> bool:
    """
    Generate image from existing prompt file.
    
    Args:
        prompt_file: Path to text file containing the prompt
        output_path: Where to save the generated PNG image
        llm_endpoint: LLM API endpoint URL
        
    Returns:
        True if successful, False otherwise
    """
    # Read prompt file
    prompt_path = Path(prompt_file)
    if not prompt_path.exists():
        print(f"[ERROR] Prompt file not found: {prompt_file}")
        return False
    
    prompt = prompt_path.read_text(encoding='utf-8')
    print(f"[Info] Loaded prompt from {prompt_file} ({len(prompt)} characters)")
    
    # Generate UUIDs for request
    cv_id = str(uuid.uuid4())
    interaction_id = str(uuid.uuid4())
    message_id = str(uuid.uuid4())
    scenario_guid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"
    
    # Build image generation payload
    image_payload = {
        "messages": [
            {
                "id": message_id,
                "author": {"role": "user"},
                "content": {
                    "content_type": "multimodal_text",
                    "parts": [prompt]
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
    
    print(f"[Info] Generating image via {llm_endpoint}/chatgpt/convo2im")
    print(f"[Info] This may take 30-90 seconds...")
    
    try:
        # Call image generation API with streaming
        response = requests.post(
            f"{llm_endpoint}/chatgpt/convo2im",
            headers=image_headers,
            json=image_payload,
            stream=True,
            timeout=120
        )
        
        response.raise_for_status()
        
        # Parse SSE response
        image_found = False
        output_file = Path(output_path)
        
        for line in response.iter_lines(decode_unicode=True):
            if not line:
                continue
            
            if line.startswith("data: "):
                data_content = line[6:].strip()
                
                if data_content in ["[DONE]", "DONE"]:
                    break
                
                try:
                    data = json.loads(data_content)
                    
                    # Look for image in content.parts
                    if 'content' in data and 'parts' in data['content']:
                        for part in data['content']['parts']:
                            if part.get('content_type') == 'image' and 'payload' in part:
                                # Decode base64 image
                                image_bytes = base64.b64decode(part['payload'])
                                
                                # Create output directory if needed
                                output_file.parent.mkdir(parents=True, exist_ok=True)
                                
                                # Save to file
                                output_file.write_bytes(image_bytes)
                                
                                file_size_kb = round(len(image_bytes) / 1024, 2)
                                print(f"[Success] Image saved to {output_path} ({file_size_kb} KB)")
                                image_found = True
                                break
                
                except json.JSONDecodeError:
                    # Skip non-JSON lines
                    continue
        
        if not image_found:
            print("[ERROR] No image data found in API response")
            return False
        
        return True
    
    except requests.RequestException as e:
        print(f"[ERROR] API call failed: {e}")
        return False
    except Exception as e:
        print(f"[ERROR] Unexpected error: {e}")
        return False


def main():
    parser = argparse.ArgumentParser(
        description='Generate infographic image from existing prompt file',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s --prompt prompt_abc123.txt --output result.png
  %(prog)s --prompt saved_prompt.txt --output test_output.png --endpoint http://localhost:4242
  
This script is useful for:
- Testing prompt changes without re-running content analysis
- Debugging image generation issues
- Quick iteration on visual styles
"""
    )
    
    parser.add_argument('--prompt', required=True,
                        help='Path to text file containing the image generation prompt')
    parser.add_argument('--output', required=True,
                        help='Output path for generated PNG image')
    parser.add_argument('--endpoint', default='http://localhost:4141',
                        help='LLM API endpoint URL (default: http://localhost:4141)')
    
    args = parser.parse_args()
    
    success = generate_image_from_prompt(args.prompt, args.output, args.endpoint)
    
    sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
