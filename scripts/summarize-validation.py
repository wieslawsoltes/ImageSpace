#!/usr/bin/env python3
"""Summarize emitted validation reports; missing checks are never treated as success."""
import json
import os
from pathlib import Path

root = Path('artifacts')
summary = {'commit': os.environ.get('GITHUB_SHA', 'local'), 'engine': {}, 'browser': {}, 'failures': [], 'performance': {}}
for file in sorted(root.glob('*tests.json')):
    data = json.loads(file.read_text())
    if isinstance(data, dict):
        summary['engine'][file.name] = {key: data[key] for key in ('total', 'passed', 'failed') if key in data}

def failures(suite):
    for child in suite.get('suites', []):
        failures(child)
    for spec in suite.get('specs', []):
        for case in spec.get('tests', []):
            for result in case.get('results', []):
                if result.get('status') not in ('passed', 'skipped'):
                    summary['failures'].append({'file': spec.get('file'), 'title': spec.get('title'),
                        'status': result.get('status'), 'errors': result.get('errors', [])})

for name in ('browser-results.json', 'resident-gpu-results.json'):
    file = root / name
    if file.exists():
        data = json.loads(file.read_text())
        summary['browser'][name] = data.get('stats', {})
        failures(data)
for name in ('resident-gpu-performance.json', 'resident-gpu-ab-performance.json'):
    file = root / name
    if file.exists():
        summary['performance'][name] = json.loads(file.read_text())
root.mkdir(exist_ok=True)
(root / 'validation-summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print('IMAGESPACE_VALIDATION_SUMMARY ' + json.dumps(summary))
