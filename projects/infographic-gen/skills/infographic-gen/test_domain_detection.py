#!/usr/bin/env python3
"""
Test script for infographic-gen skill domain detection
Tests the domain detection algorithm without requiring full API setup
"""

import json
import re
from pathlib import Path

def load_style_config():
    """Load the style configuration"""
    config_path = Path(__file__).parent / "style-config.json"
    with open(config_path, 'r', encoding='utf-8') as f:
        return json.load(f)

def contains_word(text, word):
    """Check if text contains a whole word (not partial match)"""
    pattern = r'\b' + re.escape(word.lower()) + r'\b'
    return bool(re.search(pattern, text.lower()))

def detect_domain(description, config):
    """Detect domain from description using the algorithm from content-analyzer.md"""
    # Sort domains by priority (descending)
    sorted_domains = sorted(config, key=lambda d: d.get('priority', 0), reverse=True)

    # Check anti-keywords first (exclusion)
    for domain in sorted_domains:
        anti_keywords = domain.get('antiKeywords', [])
        if any(contains_word(description, ak) for ak in anti_keywords):
            print(f"  ❌ Excluded {domain['domain']} (anti-keyword match)")
            continue

        # Check keywords (inclusion)
        keywords = domain.get('keywords', [])
        matched_keywords = [kw for kw in keywords if contains_word(description, kw)]
        if matched_keywords:
            print(f"  ✅ Matched {domain['domain']} (keywords: {', '.join(matched_keywords[:3])})")
            return domain['domain'], matched_keywords

    print("  ⚠️  No domain matched, using default: business")
    return "business", []

def select_variant(description, domain_name, config):
    """Select best variant within the domain"""
    domain = next((d for d in config if d['domain'] == domain_name), None)
    if not domain or not domain.get('variants'):
        return "default"

    # Score each variant
    variants_with_scores = []
    for variant in domain['variants']:
        score = 0
        matched = []

        # Keyword matches: +2 points each
        for kw in variant.get('keywords', []):
            if contains_word(description, kw):
                score += 2
                matched.append(kw)

        # Tone matches: +1 point each
        for tone in variant.get('preferredTones', []):
            if contains_word(description, tone):
                score += 1
                matched.append(f"{tone}(tone)")

        variants_with_scores.append({
            'name': variant['name'],
            'score': score,
            'matched': matched
        })

    # Sort by score (descending)
    variants_with_scores.sort(key=lambda v: v['score'], reverse=True)

    # Display scoring
    for v in variants_with_scores:
        status = "🏆" if v == variants_with_scores[0] else "  "
        print(f"    {status} {v['name']}: {v['score']} points", end="")
        if v['matched']:
            print(f" ({', '.join(v['matched'][:3])})")
        else:
            print()

    return variants_with_scores[0]['name']

def test_file(file_path, config):
    """Test domain detection on a file"""
    print(f"\n{'='*60}")
    print(f"Testing: {file_path.name}")
    print('='*60)

    # Read file content
    with open(file_path, 'r', encoding='utf-8') as f:
        content = f.read()

    print(f"Content length: {len(content)} characters")
    print(f"Preview: {content[:150]}...")
    print()

    # Detect domain
    print("Domain Detection:")
    domain, keywords = detect_domain(content, config)
    print(f"\n  🎯 Result: domain = '{domain}'")
    print()

    # Select variant
    print("Variant Selection:")
    variant = select_variant(content, domain, config)
    print(f"\n  🎯 Result: variant = '{variant}'")
    print()

    return domain, variant

def main():
    """Run tests"""
    print("Infographic-Gen Skill - Domain Detection Test")
    print("=" * 60)

    # Load configuration
    config = load_style_config()
    print(f"Loaded {len(config)} domains from style-config.json")

    # Find test files
    test_dir = Path(__file__).parent
    test_files = list(test_dir.glob("test_*.txt"))

    if not test_files:
        print("\n⚠️  No test files found. Create files named test_*.txt to test.")
        return

    # Test each file
    results = []
    for test_file in test_files:
        domain, variant = test_file(test_file, config)
        results.append({
            'file': test_file.name,
            'domain': domain,
            'variant': variant
        })

    # Summary
    print("\n" + "="*60)
    print("Test Summary")
    print("="*60)
    for r in results:
        print(f"  {r['file']:30} → {r['domain']:15} / {r['variant']}")

    print("\n✅ Domain detection test completed!")
    print("\nNext steps:")
    print("1. Set environment variables: LLM_ENDPOINT, LLM_MODEL, IMAGE_ENDPOINT")
    print("2. Test with Claude Code: 'Generate an infographic from test_astronomy.txt'")

if __name__ == "__main__":
    main()
