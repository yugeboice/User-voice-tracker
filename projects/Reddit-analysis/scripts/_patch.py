import sys
p = "lab/projects/Reddit-analysis/scripts/analyze.py"
with open(p, "r", encoding="utf-8") as f:
    text = f.read()

NL = "\r\n"
changes = 0

# Edit 1: Insert Step 5 structured insights call  
old = f"        product_summaries[product_name] = summary{NL}{NL}        # Save post analysis to DB"
new_step5 = (
    f'        product_summaries[product_name] = summary{NL}'
    f'{NL}'
    f'        # Step 5: Extract structured insights{NL}'
    f'        log.info("[%s] Step 5: Extracting structured insights...", sub_id){NL}'
    f'        structured = extract_structured_insights({NL}'
    f'            product_name, valid_posts, analysis_map, endpoint, model,{NL}'
    f'        ){NL}'
    f'{NL}'
    f'        # Save post analysis to DB'
)
if old in text:
    text = text.replace(old, new_step5, 1)
    changes += 1
    print("Edit 1 OK: Step 5 inserted")
else:
    print("SKIP Edit 1: pattern not found")

# Edit 2: Add structured fields to product_report
old = f'            "typical_posts": typical_posts_data,{NL}        }}'
new_fields = (
    f'            "typical_posts": typical_posts_data,{NL}'
    f'            "pain_points": structured.get("pain_points", []),{NL}'
    f'            "strengths": structured.get("strengths", []),{NL}'
    f'            "recommendations": structured.get("recommendations", []),{NL}'
    f'            "keywords": structured.get("keywords", []),{NL}'
    f'        }}'
)
if old in text:
    text = text.replace(old, new_fields, 1)
    changes += 1
    print("Edit 2 OK: structured fields added")
else:
    print("SKIP Edit 2: pattern not found")

# Edit 3: Rename Step 5 -> Step 6
old = "    # Step 5: Cross-product comparison"
new = "    # Step 6: Cross-product comparison"
if old in text:
    text = text.replace(old, new, 1)
    changes += 1
    print("Edit 3 OK: renamed to Step 6")
else:
    print("SKIP Edit 3: pattern not found")

# Edit 4: index.json generation call
old = f'    report_path.write_text(json.dumps(all_report_data, indent=2, ensure_ascii=False), encoding="utf-8"){NL}{NL}    conn.close()'
new_idx = (
    f'    report_path.write_text(json.dumps(all_report_data, indent=2, ensure_ascii=False), encoding="utf-8"){NL}'
    f'{NL}'
    f'    # Generate index.json for all reports{NL}'
    f'    _generate_report_index(REPORTS_DIR){NL}'
    f'{NL}'
    f'    conn.close()'
)
if old in text:
    text = text.replace(old, new_idx, 1)
    changes += 1
    print("Edit 4 OK: index.json call added")
else:
    print("SKIP Edit 4: pattern not found")

if changes > 0:
    with open(p, "w", encoding="utf-8") as f:
        f.write(text)
    print(f"Saved {changes} edits, {len(text.splitlines())} lines")
else:
    print("No edits matched!", file=sys.stderr)
    sys.exit(1)
