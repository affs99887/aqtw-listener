"""Compatibility entry point for exporting the selected Shidian logo.

Editable geometry lives in src/Listener.App/Assets/listener-{mark,app}.svg.
The Windows exporter renders each ICO size from those paths and writes both
PNG resources. This command no longer redraws the retired waveform logo.
Usage: python scripts/draw-logo.py
"""
from pathlib import Path
import subprocess

if __name__ == "__main__":
    subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-STA", "-File",
         str(Path(__file__).with_name("export-icon.ps1"))],
        check=True,
    )
