"""One-shot Frida driver: inject a script, collect N messages, detach.

Use this when you want to validate a hook without the interactive REPL.
Run from the repo root:

    python tools\frida-scripts\verify-hook.py [script.js] [seconds] [process]

Defaults: trace-template.js, 6 seconds, nba2k16.exe.
"""
import sys
import time
import frida

script_path = sys.argv[1] if len(sys.argv) > 1 else r"tools\frida-scripts\trace-template.js"
duration   = float(sys.argv[2]) if len(sys.argv) > 2 else 6.0
target     = sys.argv[3] if len(sys.argv) > 3 else "nba2k16.exe"

with open(script_path, "r", encoding="utf-8") as f:
    src = f.read()

session = frida.attach(target)
script  = session.create_script(src)

received = []
def on_message(message, data):
    if message["type"] == "send":
        received.append(message["payload"])
        print("[msg]", message["payload"], flush=True)
    elif message["type"] == "error":
        print("[err]", message.get("description"), flush=True)
        print(message.get("stack"), flush=True)

script.on("message", on_message)
script.load()
print(f"[+] Script loaded, listening for {duration}s...", flush=True)
time.sleep(duration)
script.unload()
session.detach()
print(f"[+] Done. Received {len(received)} send() messages.", flush=True)
