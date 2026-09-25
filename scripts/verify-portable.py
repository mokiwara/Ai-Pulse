"""Smoke-test the shipped x64 capture helper without accessing provider accounts."""
import json
import os
from pathlib import Path
import subprocess
import sys
import time
import uuid

root = Path(__file__).resolve().parent.parent
version = sys.argv[1] if len(sys.argv) > 1 else '1.0.1'
exe = root / 'release' / f'AI-Pulse-{version}-win-x64-Portable.exe'
context = uuid.uuid4().hex
feed = Path(os.environ['LOCALAPPDATA']) / 'AIUsageHub' / 'claude-feed' / f'{context}.json'
results = {}

def capture(payload):
    result = subprocess.run([str(exe), '--claude-statusline', context],
                            input=payload, text=True, capture_output=True,
                            timeout=15, creationflags=subprocess.CREATE_NO_WINDOW)
    assert result.returncode == 0, f'Capture exit code: {result.returncode}'

try:
    assert not feed.exists()
    capture(json.dumps({'rate_limits': {'five_hour': {
        'used_percentage': 25, 'resets_at': int(time.time()) + 3600}},
        'session_id': 'PRIVATE_SENTINEL'}))
    normalized = feed.read_text(encoding='utf-8-sig')
    assert 'PRIVATE_SENTINEL' not in normalized
    assert json.loads(normalized)['Windows'][0]['UsedPercent'] == 25
    results['normalized_capture_without_raw_session_data'] = True
    for payload in ('not json', 'x' * 2_000_001):
        capture(payload)
        assert feed.read_text(encoding='utf-8-sig') == normalized
    results['invalid_and_oversized_input_preserves_last_reading'] = True
    with subprocess.Popen([str(exe), '--claude-statusline', context],
                          stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                          stderr=subprocess.PIPE,
                          creationflags=subprocess.CREATE_NO_WINDOW) as process:
        started = time.monotonic()
        try:
            assert process.wait(timeout=9) == 0
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
            raise AssertionError('Capture hung while its caller held stdin open')
        results['idle_stdin_exit_seconds'] = round(time.monotonic() - started, 2)
    report = root / 'docs' / 'production' / 'portable-capture-smoke.json'
    report.write_text(json.dumps(results, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(results))
finally:
    # Only the GUID file created by this invocation is removed.
    feed.unlink(missing_ok=True)
