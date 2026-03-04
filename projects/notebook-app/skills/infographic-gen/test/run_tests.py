#!/usr/bin/env python3
"""
Batch test runner for infographic generation.
Tests all domain/variant combinations and generates an HTML report.
"""

import json
import subprocess
import argparse
import sys
from pathlib import Path
from datetime import datetime
from typing import List, Dict, Tuple, Optional


def get_all_combinations(style_config: List[Dict]) -> List[Tuple[str, str]]:
    """Get all domain/variant combinations from style config."""
    combinations = []
    for domain in style_config:
        domain_name = domain['domain']
        for variant in domain.get('variants', []):
            variant_name = variant['name']
            combinations.append((domain_name, variant_name))
    return combinations


def run_single_test(
    domain: str,
    variant: str,
    input_file: Path,
    output_file: Path,
    skill_path: Path,
    llm_endpoint: str,
    llm_model: str
) -> Dict:
    """Run a single infographic generation test."""
    
    result = {
        "domain": domain,
        "variant": variant,
        "input_file": str(input_file),
        "output_file": str(output_file),
        "success": False,
        "duration": 0,
        "error": None
    }
    
    start_time = datetime.now()
    
    try:
        cmd = [
            sys.executable,
            str(skill_path / "infographic-gen.py"),
            "--input", str(input_file),
            "--output", str(output_file),
            "--llm-endpoint", llm_endpoint,
            "--llm-model", llm_model,
            "--force-domain", domain,
            "--force-variant", variant
        ]
        
        process = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            timeout=300  # 5 minute timeout per test
        )
        
        result["success"] = process.returncode == 0
        result["stdout"] = process.stdout
        result["stderr"] = process.stderr
        
        if not result["success"]:
            result["error"] = process.stderr or "Unknown error"
            
    except subprocess.TimeoutExpired:
        result["error"] = "Timeout (5 minutes)"
    except Exception as e:
        result["error"] = str(e)
    
    end_time = datetime.now()
    result["duration"] = (end_time - start_time).total_seconds()
    
    return result


def generate_html_report(results: List[Dict], output_dir: Path, timestamp: str) -> Path:
    """Generate an HTML report from test results."""
    
    # Group results by domain
    domains = {}
    for r in results:
        domain = r["domain"]
        if domain not in domains:
            domains[domain] = []
        domains[domain].append(r)
    
    # Calculate statistics
    total = len(results)
    passed = sum(1 for r in results if r["success"])
    failed = total - passed
    
    # Generate HTML
    html = f"""<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Infographic Test Report - {timestamp}</title>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f5f5f5; padding: 20px; }}
        .container {{ max-width: 1400px; margin: 0 auto; }}
        h1 {{ color: #333; margin-bottom: 10px; }}
        .summary {{ background: white; padding: 20px; border-radius: 8px; margin-bottom: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .summary-stats {{ display: flex; gap: 30px; margin-top: 15px; }}
        .stat {{ text-align: center; }}
        .stat-value {{ font-size: 32px; font-weight: bold; }}
        .stat-value.passed {{ color: #22c55e; }}
        .stat-value.failed {{ color: #ef4444; }}
        .stat-label {{ color: #666; font-size: 14px; }}
        .domain-section {{ background: white; border-radius: 8px; margin-bottom: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); overflow: hidden; }}
        .domain-header {{ background: #1e40af; color: white; padding: 15px 20px; font-size: 18px; font-weight: 600; }}
        .variant-grid {{ display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 15px; padding: 20px; }}
        .variant-card {{ border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; }}
        .variant-card.success {{ border-color: #22c55e; }}
        .variant-card.failed {{ border-color: #ef4444; }}
        .variant-header {{ padding: 10px 15px; background: #f9fafb; border-bottom: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; }}
        .variant-name {{ font-weight: 600; color: #333; }}
        .variant-status {{ padding: 3px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; }}
        .variant-status.success {{ background: #dcfce7; color: #166534; }}
        .variant-status.failed {{ background: #fee2e2; color: #991b1b; }}
        .variant-image {{ width: 100%; aspect-ratio: 9/16; background: #f3f4f6; display: flex; align-items: center; justify-content: center; overflow: hidden; }}
        .variant-image img {{ width: 100%; height: 100%; object-fit: contain; }}
        .variant-image .placeholder {{ color: #9ca3af; font-size: 14px; }}
        .variant-meta {{ padding: 10px 15px; font-size: 12px; color: #666; }}
        .error-message {{ background: #fee2e2; color: #991b1b; padding: 10px; font-size: 12px; margin: 10px; border-radius: 4px; }}
    </style>
</head>
<body>
    <div class="container">
        <h1>🎨 Infographic Style Test Report</h1>
        <p style="color: #666; margin-bottom: 20px;">Generated: {timestamp}</p>
        
        <div class="summary">
            <h2>Summary</h2>
            <div class="summary-stats">
                <div class="stat">
                    <div class="stat-value">{total}</div>
                    <div class="stat-label">Total Tests</div>
                </div>
                <div class="stat">
                    <div class="stat-value passed">{passed}</div>
                    <div class="stat-label">Passed</div>
                </div>
                <div class="stat">
                    <div class="stat-value failed">{failed}</div>
                    <div class="stat-label">Failed</div>
                </div>
                <div class="stat">
                    <div class="stat-value">{passed/total*100:.1f}%</div>
                    <div class="stat-label">Success Rate</div>
                </div>
            </div>
        </div>
"""
    
    for domain_name, domain_results in domains.items():
        domain_passed = sum(1 for r in domain_results if r["success"])
        html += f"""
        <div class="domain-section">
            <div class="domain-header">{domain_name.upper()} ({domain_passed}/{len(domain_results)} passed)</div>
            <div class="variant-grid">
"""
        
        for r in domain_results:
            status_class = "success" if r["success"] else "failed"
            status_text = "✓ Passed" if r["success"] else "✗ Failed"
            
            # Check if image exists
            output_path = Path(r["output_file"])
            if output_path.exists():
                # Use relative path for HTML
                relative_path = output_path.name
                image_html = f'<img src="{r["domain"]}/{relative_path}" alt="{r["variant"]}">'
            else:
                image_html = '<div class="placeholder">No image generated</div>'
            
            error_html = ""
            if r.get("error"):
                error_html = f'<div class="error-message">{r["error"][:200]}...</div>' if len(r.get("error", "")) > 200 else f'<div class="error-message">{r.get("error", "")}</div>'
            
            html += f"""
                <div class="variant-card {status_class}">
                    <div class="variant-header">
                        <span class="variant-name">{r["variant"]}</span>
                        <span class="variant-status {status_class}">{status_text}</span>
                    </div>
                    <div class="variant-image">{image_html}</div>
                    <div class="variant-meta">Duration: {r["duration"]:.1f}s</div>
                    {error_html}
                </div>
"""
        
        html += """
            </div>
        </div>
"""
    
    html += """
    </div>
</body>
</html>
"""
    
    report_path = output_dir / "report.html"
    with open(report_path, 'w', encoding='utf-8') as f:
        f.write(html)
    
    return report_path


def main():
    parser = argparse.ArgumentParser(
        description='Run batch tests for infographic generation',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  %(prog)s --all                    # Test all domain/variant combinations
  %(prog)s --domain business        # Test all variants in business domain
  %(prog)s --domain science --variant academic-paper  # Test specific combination
"""
    )
    
    parser.add_argument(
        '--all',
        action='store_true',
        help='Test all domain/variant combinations'
    )
    
    parser.add_argument(
        '--domain',
        default=None,
        help='Test specific domain (all variants)'
    )
    
    parser.add_argument(
        '--variant',
        default=None,
        help='Test specific variant (requires --domain)'
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
        '--input-dir',
        default=None,
        help='Directory containing test input files (default: test/inputs/)'
    )
    
    args = parser.parse_args()
    
    # Validate arguments
    if not args.all and not args.domain:
        parser.error("Must specify --all or --domain")
    
    if args.variant and not args.domain:
        parser.error("--variant requires --domain")
    
    # Setup paths
    script_dir = Path(__file__).parent.absolute()
    skill_dir = script_dir.parent
    input_dir = Path(args.input_dir) if args.input_dir else script_dir / "inputs"
    
    # Load style config
    style_config_path = skill_dir / "style-config.json"
    with open(style_config_path, 'r', encoding='utf-8') as f:
        style_config = json.load(f)
    
    # Get combinations to test
    all_combinations = get_all_combinations(style_config)
    
    if args.all:
        combinations = all_combinations
    elif args.variant:
        combinations = [(args.domain, args.variant)]
    else:
        # All variants for specific domain
        combinations = [(d, v) for d, v in all_combinations if d == args.domain]
    
    if not combinations:
        print(f"[ERROR] No valid combinations found for domain='{args.domain}', variant='{args.variant}'")
        sys.exit(1)
    
    # Check input files exist
    domains_needed = set(d for d, v in combinations)
    missing_inputs = []
    for domain in domains_needed:
        input_file = input_dir / f"{domain}.txt"
        if not input_file.exists():
            missing_inputs.append(domain)
    
    if missing_inputs:
        print(f"[ERROR] Missing input files for domains: {', '.join(missing_inputs)}")
        print(f"[INFO] Run 'python generate_test_inputs.py' first to generate test inputs")
        sys.exit(1)
    
    # Create timestamped output directory
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    output_dir = script_dir / "outputs" / timestamp
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # Create domain subdirectories
    for domain in domains_needed:
        (output_dir / domain).mkdir(exist_ok=True)
    
    print(f"[INFO] Running {len(combinations)} test(s)...")
    print(f"[INFO] Output directory: {output_dir}")
    print(f"[INFO] LLM endpoint: {args.llm_endpoint}")
    print()
    
    results = []
    
    for i, (domain, variant) in enumerate(combinations, 1):
        print(f"[{i}/{len(combinations)}] Testing {domain}/{variant}...", end=" ", flush=True)
        
        input_file = input_dir / f"{domain}.txt"
        output_file = output_dir / domain / f"{variant}.png"
        
        result = run_single_test(
            domain=domain,
            variant=variant,
            input_file=input_file,
            output_file=output_file,
            skill_path=skill_dir,
            llm_endpoint=args.llm_endpoint,
            llm_model=args.llm_model
        )
        
        results.append(result)
        
        if result["success"]:
            print(f"✓ ({result['duration']:.1f}s)")
        else:
            print(f"✗ ({result['duration']:.1f}s) - {result.get('error', 'Unknown error')[:50]}")
    
    # Generate HTML report
    print()
    print("[INFO] Generating HTML report...")
    report_path = generate_html_report(results, output_dir, timestamp)
    
    # Save JSON results
    json_path = output_dir / "results.json"
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump(results, f, indent=2)
    
    # Summary
    passed = sum(1 for r in results if r["success"])
    failed = len(results) - passed
    
    print()
    print("=" * 50)
    print(f"SUMMARY: {passed}/{len(results)} passed ({passed/len(results)*100:.1f}%)")
    print(f"Report: {report_path}")
    print(f"Results: {json_path}")
    print("=" * 50)
    
    sys.exit(0 if failed == 0 else 1)


if __name__ == '__main__':
    main()
