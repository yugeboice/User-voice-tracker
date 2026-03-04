#!/usr/bin/env python3
"""
Generate test input files for each domain using LLM.
Creates ~500-word test texts that are representative of each domain.
"""

import json
import requests
import argparse
from pathlib import Path


# Domain-specific prompts for generating test content
DOMAIN_PROMPTS = {
    "business": """Generate a ~500-word educational article about business strategy and corporate management. 
Include topics like market analysis, ROI, leadership, quarterly reports, and stakeholder management. 
The content should be suitable for a professional business infographic.""",

    "history": """Generate a ~500-word educational article about a significant historical event or era.
Include specific dates, historical figures, cultural context, and timeline information.
The content should be suitable for a historical documentary-style infographic.""",

    "science": """Generate a ~500-word educational article about a scientific topic like physics, chemistry, or biology.
Include scientific concepts, research findings, experiments, and data.
The content should be suitable for an academic or illustrated science infographic.""",

    "nature-space": """Generate a ~500-word educational article about astronomy, space exploration, or natural phenomena.
Include facts about celestial bodies, cosmic events, nature, or wildlife.
The content should be suitable for a cosmic wonder or nature-themed infographic.""",

    "technology": """Generate a ~500-word educational article about modern technology, software, or digital innovation.
Include topics like AI, programming, cybersecurity, or emerging tech trends.
The content should be suitable for a technology-focused infographic.""",

    "lifestyle": """Generate a ~500-word educational article about wellness, mindfulness, or healthy living.
Include tips for meditation, work-life balance, nutrition, or self-improvement.
The content should be suitable for a zen-minimal or lifestyle infographic.""",

    "social": """Generate a ~500-word educational article about social media, digital marketing, or online trends.
Include topics like content creation, engagement strategies, or influencer culture.
The content should be suitable for a modern social media style infographic.""",

    "family": """Generate a ~500-word educational article for children or families about learning concepts.
Include fun facts, educational content suitable for kids, or family activities.
The content should be suitable for a playful, educational infographic for children."""
}


def generate_test_content(domain: str, llm_endpoint: str, llm_model: str) -> str:
    """Generate test content for a specific domain using LLM."""
    
    prompt = DOMAIN_PROMPTS.get(domain)
    if not prompt:
        raise ValueError(f"Unknown domain: {domain}")
    
    # Use Anthropic API format (same as infographic-gen.py)
    url = f"{llm_endpoint}/v1/messages"
    headers = {
        "Content-Type": "application/json",
        "anthropic-version": "2023-06-01"
    }
    
    system_prompt = "You are a content writer. Generate educational content as requested. Output ONLY the article content, no titles or headers."
    
    payload = {
        "model": llm_model,
        "max_tokens": 1500,
        "system": system_prompt,
        "messages": [
            {
                "role": "user",
                "content": prompt
            }
        ]
    }
    
    response = requests.post(url, headers=headers, json=payload, timeout=120)
    response.raise_for_status()
    
    data = response.json()
    content = data['content'][0]['text']
    
    return content.strip()


def main():
    parser = argparse.ArgumentParser(description='Generate test input files for infographic testing')
    
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
        '--domains',
        nargs='*',
        default=None,
        help='Specific domains to generate (default: all domains)'
    )
    
    parser.add_argument(
        '--output-dir',
        default=None,
        help='Output directory for test files (default: test/inputs/)'
    )
    
    args = parser.parse_args()
    
    # Determine output directory
    script_dir = Path(__file__).parent.absolute()
    output_dir = Path(args.output_dir) if args.output_dir else script_dir / "inputs"
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # Load style config to get domain list
    style_config_path = script_dir.parent / "style-config.json"
    with open(style_config_path, 'r', encoding='utf-8') as f:
        style_config = json.load(f)
    
    all_domains = [d['domain'] for d in style_config]
    
    # Filter domains if specified
    domains_to_generate = args.domains if args.domains else all_domains
    
    # Validate domains
    for domain in domains_to_generate:
        if domain not in all_domains:
            print(f"[ERROR] Unknown domain: {domain}")
            print(f"[INFO] Valid domains: {', '.join(all_domains)}")
            return
    
    print(f"[INFO] Generating test content for {len(domains_to_generate)} domains...")
    print(f"[INFO] Output directory: {output_dir}")
    print(f"[INFO] LLM endpoint: {args.llm_endpoint}")
    print(f"[INFO] LLM model: {args.llm_model}")
    print()
    
    for domain in domains_to_generate:
        print(f"[{domain}] Generating test content...")
        
        try:
            content = generate_test_content(domain, args.llm_endpoint, args.llm_model)
            
            # Save to file
            output_file = output_dir / f"{domain}.txt"
            with open(output_file, 'w', encoding='utf-8') as f:
                f.write(content)
            
            word_count = len(content.split())
            print(f"[{domain}] ✓ Generated {word_count} words -> {output_file.name}")
            
        except Exception as e:
            print(f"[{domain}] ✗ Error: {e}")
    
    print()
    print("[INFO] Test input generation complete!")


if __name__ == '__main__':
    main()
