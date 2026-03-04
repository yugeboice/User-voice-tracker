#!/usr/bin/env python3
"""
Standalone infographic generation skill - Python implementation.
Converts text content into professional educational infographics using LLM-powered
content analysis and image generation.

Usage:
    python generate_image.py --input <file> --output <path> [--llm-endpoint <url>] [--llm-model <model>]

Example:
    python generate_image.py --input astronomy.txt --output infographic.png
    python generate_image.py --input content.txt --output result.png --llm-endpoint http://localhost:4242
"""

import argparse
import json
import sys
import base64
import uuid
import re
import requests
from pathlib import Path
from typing import Dict, List, Optional, Any


# ANSI color codes for terminal output
class Colors:
    CYAN = '\033[96m'
    GREEN = '\033[92m'
    RED = '\033[91m'
    GRAY = '\033[90m'
    YELLOW = '\033[93m'
    RESET = '\033[0m'


def write_step(message: str) -> None:
    """Write step indicator in cyan."""
    print(f"{Colors.CYAN}[Infographic] {message}{Colors.RESET}")


def write_success(message: str) -> None:
    """Write success message in green."""
    print(f"{Colors.GREEN}[Infographic] {message}{Colors.RESET}")


def write_error(message: str) -> None:
    """Write error message in red."""
    print(f"{Colors.RED}[Infographic] ERROR: {message}{Colors.RESET}", file=sys.stderr)


def write_progress(message: str) -> None:
    """Write progress message in gray."""
    print(f"{Colors.GRAY}[Infographic] {message}{Colors.RESET}")


def word_boundary_match(word: str, text: str) -> bool:
    """Check if word exists as a whole word in text (not partial match)."""
    pattern = r'\b' + re.escape(word.lower()) + r'\b'
    return bool(re.search(pattern, text.lower()))


def detect_domain(description: str, style_config: List[Dict[str, Any]]) -> Optional[Dict[str, Any]]:
    """
    Detect the most appropriate domain based on description content.
    
    Algorithm:
    1. Start with base priority score from config
    2. Check anti-keywords (disqualify if match)
    3. Count keyword matches (+5 points each)
    4. Return domain with highest score
    """
    description_lower = description.lower()
    best_domain = None
    best_score = 0
    
    for domain in style_config:
        score = domain.get('priority', 0)
        
        # Check anti-keywords (disqualifiers)
        anti_keywords = domain.get('antiKeywords', [])
        skip_domain = False
        for anti_keyword in anti_keywords:
            if word_boundary_match(anti_keyword, description_lower):
                skip_domain = True
                break
        
        if skip_domain:
            continue
        
        # Count keyword matches (+5 points each)
        keywords = domain.get('keywords', [])
        for keyword in keywords:
            if word_boundary_match(keyword, description_lower):
                score += 5
        
        if score > best_score:
            best_score = score
            best_domain = domain
    
    return best_domain


def select_variant(description: str, domain: Dict[str, Any]) -> Dict[str, Any]:
    """
    Select the most appropriate variant within a domain.
    
    Algorithm:
    1. Default to first variant
    2. Count keyword matches (+3 points each)
    3. Count tone matches (+2 points each)
    4. Return variant with highest score
    """
    variants = domain.get('variants', [])
    if not variants:
        return {}
    
    best_variant = variants[0]
    best_score = 0
    
    for variant in variants:
        score = 0
        
        # Keyword matches: +3 points
        keywords = variant.get('keywords', [])
        for keyword in keywords:
            if word_boundary_match(keyword, description):
                score += 3
        
        # Tone matches: +2 points
        preferred_tones = variant.get('preferredTones', [])
        for tone in preferred_tones:
            if word_boundary_match(tone, description):
                score += 2
        
        if score > best_score:
            best_score = score
            best_variant = variant
    
    return best_variant


def extract_rendering_instructions(image_prompter_content: str, domain_name: str, variant_name: str) -> str:
    """
    Extract rendering instructions from image-prompter.md for specific domain/variant.
    
    Searches for sections like:
    ## Business Domain
    ### Editorial Magazine Style
    """
    lines = image_prompter_content.split('\n')
    
    # Normalize domain name: "nature-space" -> "Nature-Space"
    domain_display = '-'.join(word.capitalize() for word in domain_name.split('-'))
    
    # Look for domain section (## Domain Name Domain)
    domain_pattern = re.compile(r'^##\s+' + re.escape(domain_display) + r'\s+Domain', re.IGNORECASE)
    variant_pattern = re.compile(r'^###\s+' + re.escape(variant_name) + r'\s+(Style|Variant)', re.IGNORECASE)
    
    in_domain = False
    in_variant = False
    instructions = []
    
    for line in lines:
        # Check if entering target domain
        if domain_pattern.match(line):
            in_domain = True
            continue
        
        # Check if leaving current domain (entering new domain)
        if in_domain and line.startswith('## ') and not domain_pattern.match(line):
            break
        
        # Check if entering target variant
        if in_domain and variant_pattern.match(line):
            in_variant = True
            continue
        
        # Check if leaving current variant (only true variant headers, not content within code blocks)
        if in_variant:
            # Only exit if we hit a NEW variant/style header (###) or domain header (##)
            # Content like "### SPACE/COSMIC STYLE" inside code blocks should NOT trigger exit
            if line.startswith('## '):
                break
            if line.startswith('### ') and (line.lower().endswith(' variant') or line.lower().endswith(' style')):
                break
        
        # Collect variant content
        if in_variant:
            instructions.append(line)
    
    return '\n'.join(instructions).strip()


def generate_infographic(input_file: str, output_path: str, llm_endpoint: str, llm_model: str, custom_style: Optional[str] = None) -> bool:
    """
    Main infographic generation workflow.
    
    Steps:
    1. Validation: Check files and connectivity
    2. Content Analysis: Analyze input and detect domain/variant
    3. Prompt Construction: Build complete image prompt
    4. Image Generation: Call API and generate image
    
    Args:
        input_file: Path to input text file
        output_path: Path for output PNG image
        llm_endpoint: LLM API endpoint URL
        llm_model: LLM model name
        custom_style: Optional user custom style/content prompt
    """
    
    script_dir = Path(__file__).parent.absolute()
    
    # === Step 0: Validation ===
    write_step("Step 0/4: Validating environment...")
    
    # Check input file
    input_path = Path(input_file)
    if not input_path.exists():
        write_error(f"Input file not found: {input_file}")
        return False
    
    # Check LLM connectivity
    try:
        health_response = requests.get(f"{llm_endpoint}/health", timeout=5)
        if health_response.status_code != 200:
            write_error(f"LLM endpoint health check failed (status {health_response.status_code})")
            return False
        write_progress("LLM endpoint is healthy")
    except requests.RequestException as e:
        write_error(f"Cannot connect to LLM endpoint: {e}")
        return False
    
    # Create output directory if needed
    output_file = Path(output_path)
    output_file.parent.mkdir(parents=True, exist_ok=True)
    
    write_success("Environment validation passed")
    
    # === Step 1: Content Analysis & Domain Detection ===
    write_step("Step 1/4: Analyzing content and detecting domain...")
    
    # Read input content (raw notebook content from C#)
    content = input_path.read_text(encoding='utf-8')
    write_progress(f"Input content: {len(content)} characters")
    
    # Load content analyzer prompt
    content_analyzer_path = script_dir / "content-analyzer.md"
    if not content_analyzer_path.exists():
        write_error(f"content-analyzer.md not found at {content_analyzer_path}")
        return False
    
    content_analyzer_prompt = content_analyzer_path.read_text(encoding='utf-8')
    
    # Load style configuration
    style_config_path = script_dir / "style-config.json"
    if not style_config_path.exists():
        write_error(f"style-config.json not found at {style_config_path}")
        return False
    
    with open(style_config_path, 'r', encoding='utf-8') as f:
        style_config = json.load(f)
    
    # Call LLM for content analysis
    write_progress("Calling LLM for content analysis...")
    
    # Build combined prompt (system + user content)
    user_prompt = f"""Analyze the following content and generate a detailed infographic description.

INPUT CONTENT:
{content}

IMPORTANT OUTPUT FORMAT REQUIREMENTS:
- Output ONLY the formatted markdown description text
- DO NOT wrap output in JSON or code blocks
- DO NOT include any preamble or explanatory text
- Start directly with: # INFOGRAPHIC BRIEF
- Follow the exact section structure from the system prompt:
  * # INFOGRAPHIC BRIEF
  * # CONTENT OUTLINE (sections a-e)
  * # LAYOUT PLAN
  * # STYLE GUIDE (DOMAIN Domain)
  * **Specific text to render**: (list all exact text for UI elements)

Generate a comprehensive infographic specification following the Output Structure in the system prompt.
Focus on creating an engaging, educational, and visually compelling design.
"""
    
    # Inject custom style into content analysis if provided
    if custom_style:
        user_prompt += f"""

---
## USER CUSTOMIZATION REQUEST (Please consider the following when analyzing content):
{custom_style}
"""
        write_success(f"Custom style injected into content analysis ({len(custom_style)} chars)")
    
    full_prompt = content_analyzer_prompt + "\n\n" + user_prompt
    
    llm_request = {
        "model": llm_model,
        "max_tokens": 4000,
        "messages": [
            {
                "role": "user",
                "content": full_prompt
            }
        ]
    }
    
    try:
        llm_response = requests.post(
            f"{llm_endpoint}/v1/messages",
            json=llm_request,
            headers={"anthropic-version": "2023-06-01"},
            timeout=300  # 5 minutes for long prompt
        )
        llm_response.raise_for_status()
        
        llm_data = llm_response.json()
        description = llm_data['content'][0]['text'].strip()
        
        # Validate format: should start with markdown header, not JSON
        if description.startswith('```json') or description.startswith('{'):
            write_error("LLM returned JSON format instead of markdown. Check user prompt.")
            write_error(f"Response preview: {description[:500]}")
            return False
        
        if not description.startswith('#'):
            write_error("LLM description doesn't start with markdown header (expected '# INFOGRAPHIC BRIEF')")
            write_error(f"Response preview: {description[:500]}")
            return False
        
        write_progress(f"LLM description length: {len(description)} characters")
        write_progress(f"Description preview: {description[:300]}...")
        
    except requests.RequestException as e:
        write_error(f"LLM API call failed: {e}")
        return False
    except (KeyError, IndexError) as e:
        write_error(f"Unexpected LLM response format: {e}")
        return False
    
    # Detect domain and select variant based on description
    description_lower = description.lower()
    
    best_domain = None
    best_score = 0
    
    for domain in style_config:
        score = domain.get('priority', 0)
        domain_name_temp = domain['domain']
        
        # Check anti-keywords (disqualifiers)
        has_anti_keyword = False
        for anti_keyword in domain.get('antiKeywords', []):
            if re.search(r'\b' + re.escape(anti_keyword.lower()) + r'\b', description_lower):
                has_anti_keyword = True
                write_progress(f"Domain '{domain_name_temp}' disqualified by anti-keyword: {anti_keyword}")
                break
        
        if has_anti_keyword:
            continue
        
        # Count keyword matches
        keyword_matches = 0
        matched_keywords = []
        for keyword in domain.get('keywords', []):
            if re.search(r'\b' + re.escape(keyword.lower()) + r'\b', description_lower):
                keyword_matches += 1
                matched_keywords.append(keyword)
        
        score += keyword_matches * 5
        
        write_progress(f"Domain '{domain_name_temp}': priority={domain.get('priority', 0)}, matches={keyword_matches} ({', '.join(matched_keywords[:5])}{'...' if len(matched_keywords) > 5 else ''}), score={score}")
        
        if score > best_score:
            best_score = score
            best_domain = domain
    
    if not best_domain:
        write_error("Could not detect suitable domain from description")
        return False
    
    domain_name = best_domain['domain']
    
    # Select variant based on keywords and tone
    best_variant = None
    best_variant_score = 0
    
    for variant in best_domain.get('variants', []):
        variant_score = 0
        
        # Count variant keyword matches
        for keyword in variant.get('keywords', []):
            if re.search(r'\b' + re.escape(keyword.lower()) + r'\b', description_lower):
                variant_score += 1
        
        if variant_score > best_variant_score:
            best_variant_score = variant_score
            best_variant = variant
    
    # Fallback to first variant if no matches
    if not best_variant and best_domain.get('variants'):
        best_variant = best_domain['variants'][0]
    
    if not best_variant:
        write_error(f"No variants found for domain {domain_name}")
        return False
    
    variant_name = best_variant['name']
    
    write_success(f"Domain: {domain_name}, Variant: {variant_name} (detected from content, score: {best_score})")
    
    # === Step 2: Image Prompt Construction ===
    write_step("Step 2/4: Building image prompt with style-specific rendering...")
    
    # Load image prompter
    image_prompter_path = script_dir / "image-prompter.md"
    if not image_prompter_path.exists():
        write_error(f"image-prompter.md not found at {image_prompter_path}")
        return False
    
    image_prompter_content = image_prompter_path.read_text(encoding='utf-8')
    
    # Extract rendering instructions
    rendering_instructions = extract_rendering_instructions(
        image_prompter_content,
        domain_name,
        variant_name
    )
    
    if not rendering_instructions:
        write_error(f"Could not find rendering instructions for {domain_name}/{variant_name}")
        return False
    
    write_progress(f"Rendering instructions: {len(rendering_instructions)} characters")
    
    # Append custom style to rendering instructions if provided
    if custom_style:
        rendering_instructions += f"""

## USER CUSTOMIZATION (Higher Priority - Override any conflicts above)
{custom_style}
"""
        write_success(f"Custom style appended to rendering instructions")
    
    # Build complete image prompt (following format from image-prompter.md)
    from datetime import datetime
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    
    full_prompt = f"""Generated at: {timestamp}

Create a professional, educational infographic in LANDSCAPE format (16:9 or 4:3 aspect ratio) based on this specification:

{description}

{rendering_instructions}

## GENERAL RENDERING RULES:
- Follow the specified layout type from description precisely
- Maintain visual hierarchy and clear information flow
- ALL TEXT MUST BE IN ENGLISH ONLY - no Chinese, Japanese, Korean, or other non-English characters
- Do NOT add decorative foreign language text (e.g., Japanese characters in Japanese Minimal style)
- Balance aesthetics with readability - information must be clear
- Create depth through layering, shadows, and visual rhythm
- Include source citations in footer (minimalist style)

CRITICAL LANGUAGE ENFORCEMENT:
- Render ALL visible text in English alphabet characters only
- If style mentions Asian aesthetics, use the aesthetic principles but English text only
- No foreign language decorations, no untranslated text, English only everywhere
"""
    
    write_success(f"Complete prompt constructed: {len(full_prompt)} characters")
    
    # Save prompt to file for debugging/reference
    output_file_path = Path(output_path)
    prompt_file = output_file_path.parent / f"prompt_{output_file_path.stem}.txt"
    try:
        # Save the complete prompt as-is (same format sent to image API)
        prompt_file.write_text(full_prompt, encoding='utf-8')
        write_progress(f"Prompt saved to {prompt_file.name}")
    except Exception as e:
        write_progress(f"Warning: Could not save prompt file: {e}")
    
    # === Step 3: Image Generation ===
    write_step("Step 3/4: Generating infographic image via egress-llm...")
    write_progress("This may take 30-90 seconds...")
    
    # Generate UUIDs for request
    cv_id = str(uuid.uuid4())
    scenario_guid = "347061d6-d666-4e7e-a34e-38bb75bd7a38"
    interaction_id = str(uuid.uuid4())
    
    # Build image generation payload - using NEW format with all required fields
    # Note: This API uses size="image" - actual resolution is determined server-side
    image_payload = {
        "messages": [
            {
                "id": str(uuid.uuid4()),
                "author": {"role": "user"},
                "content": {
                    "content_type": "multimodal_text",
                    "parts": [full_prompt]
                }
            }
        ],
        "virtual_model": "gpt-image-1-5",
        "zdr_type": 1,
        "size": "image",  # Use standard "image" size - explicit sizes may not be supported
        "orientation": "landscape",
        "stream": True,
        "image_format": "png",
        "n": 1,
        "encode_user_images_as_vq": True
    }
    
    # Build headers - matching working C# version
    image_headers = {
        "Content-Type": "application/json",
        "Accept": "text/event-stream",
        "X-CV": cv_id,
        "X-ChatGPT-User-Email": "infographic-skill@microsoft.com",
        "X-ChatGPT-User-Id": "infographic-skill-001",
        "X-ModelType": "dev-gpt-image-1-5",
        "X-ScenarioGUID": scenario_guid,
        "x-imagegen-api-use-mainline": "true",
        "X-InteractionId": interaction_id,
        "X-Tag": json.dumps({"Client": "InfographicSkill"})
    }
    
    # Call image generation API with streaming
    try:
        image_response = requests.post(
            f"{llm_endpoint}/chatgpt/convo2im",
            json=image_payload,
            headers=image_headers,
            timeout=300,  # 5 minutes for image generation
            stream=True   # Enable streaming for SSE
        )
        image_response.raise_for_status()
        
    except requests.RequestException as e:
        write_error(f"Image generation API call failed: {e}")
        return False
    
    # === Step 4: Parse SSE Stream and Save Image ===
    write_step("Step 4/4: Saving infographic...")
    
    image_found = False
    base64_image = None
    line_count = 0
    
    # Parse Server-Sent Events stream
    # NEW API format: content.parts[].payload (primary)
    # OLD API format: choices[0].message.imagePayload.data (fallback)
    for line in image_response.iter_lines(decode_unicode=True):
        if not line:
            continue
        
        line_count += 1
        
        if line.startswith("data: "):
            data_content = line[6:].strip()
            
            if data_content in ["[DONE]", "DONE"]:
                write_progress(f"Stream completed after {line_count} lines")
                break
            
            try:
                data = json.loads(data_content)
                
                # Debug: Print keys of each chunk
                if line_count <= 5:
                    write_progress(f"DEBUG chunk {line_count} keys: {list(data.keys())}")
                
                # NEW API format (primary): content.parts[].payload
                if 'content' in data and 'parts' in data['content']:
                    for part in data['content']['parts']:
                        if part.get('content_type') == 'image' and 'payload' in part:
                            base64_image = part['payload']
                            image_found = True
                            write_progress(f"Found image in NEW format (chunk {line_count})")
                            break
                
                # OLD API format (fallback): choices[0].message.imagePayload.data
                if not image_found and 'choices' in data and len(data['choices']) > 0:
                    message = data['choices'][0].get('message', {})
                    img_payload = message.get('imagePayload', {})
                    if 'data' in img_payload:
                        base64_image = img_payload['data']
                        image_found = True
                        write_progress(f"Found image in OLD format (chunk {line_count})")
                    
            except json.JSONDecodeError:
                # Skip non-JSON lines
                continue
            except Exception as e:
                write_error(f"Error processing SSE data: {e}")
                continue
    
    if not image_found or not base64_image:
        write_error("No image data found in API response")
        return False
    
    # Decode base64 and save image
    try:
        image_bytes = base64.b64decode(base64_image)
        output_file.write_bytes(image_bytes)
        
        file_size_kb = round(len(image_bytes) / 1024, 2)
        write_success(f"Image saved to {output_path} ({file_size_kb} KB)")
    except Exception as e:
        write_error(f"Failed to decode/save image: {e}")
        return False
    
    write_success("Infographic generation completed successfully!")
    return True


def main():
    """Main entry point."""
    parser = argparse.ArgumentParser(
        description='Generate professional educational infographics from text files',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s --input astronomy.txt --output infographic.png
  %(prog)s --input content.txt --output result.png --llm-endpoint http://localhost:4242
  %(prog)s --input business.txt --output output.png --llm-model claude-3-sonnet
"""
    )
    
    parser.add_argument(
        '--input',
        required=True,
        help='Path to input text file containing content to visualize'
    )
    
    parser.add_argument(
        '--output',
        required=True,
        help='Output path for generated PNG infographic'
    )
    
    parser.add_argument(
        '--llm-endpoint',
        default='http://localhost:4141',
        help='LLM endpoint URL (default: http://localhost:4141)'
    )
    
    parser.add_argument(
        '--llm-model',
        default='claude-sonnet-4',
        help='LLM model name (default: claude-sonnet-4)'
    )
    
    parser.add_argument(
        '--custom-style',
        default=None,
        help='Custom style/content prompt from user (optional). Will be injected into both content analysis and rendering instructions.'
    )
    
    args = parser.parse_args()
    
    # Run generation
    success = generate_infographic(
        args.input,
        args.output,
        args.llm_endpoint,
        args.llm_model,
        args.custom_style
    )
    
    sys.exit(0 if success else 1)


if __name__ == '__main__':
    main()
